using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Core.Services;
using EmoTracker.Data;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Scripting;
using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.AutoTracker
{
    /// <summary>
    /// Per-state auto-tracker runtime. One instance per
    /// <see cref="TrackerState"/>: owns the state's selected provider,
    /// active provider, connection status, and the 30 ms polling timer
    /// that drives reads.
    ///
    /// <para>
    /// Phase 7.13: memory segments + timers are now owned by the
    /// per-state <see cref="ScriptManager"/> directly — pack scripts call
    /// <c>ScriptHost:AddMemoryWatch(...)</c> which mints a
    /// <see cref="LuaMemorySegment"/>, registers it on the state's
    /// resolver (so the LuaStateCloner can remap pack-cached references
    /// across forks), and stores it on
    /// <c>ScriptManager.MemorySegments</c>. This extension just iterates
    /// those collections each poll tick and pumps each entry through
    /// <c>UpdateWithConnector</c> with this state's active provider.
    /// </para>
    ///
    /// <para>
    /// <b>Fork support.</b> <see cref="Fork"/> allocates a fresh,
    /// disconnected instance bound to the destination state. The
    /// memory-segment list lives on the state's ScriptManager and forks
    /// natively (segments are <see cref="ModelTypeBase"/>); no replay or
    /// re-registration is needed here. We do NOT carry the source's
    /// active-provider connection across — each state owns its
    /// connection.
    /// </para>
    /// </summary>
    public class AutoTrackerExtension : ObservableObject, ITrackerExtension, IDisposable
    {
        public string Name => "Auto Tracking";
        public string UID => "emotracker_auto_tracking";
        public int Priority => -100;

        TrackerState mState;
        public TrackerState State => mState;

        public AutoTrackerExtension()
        {
            StartCommand = new DelegateCommand(StartAutoTracking, CanStartAutoTracking);
            StopCommand = new DelegateCommand(StopAutoTracking, CanStopAutoTracking);
            SetProviderCommand = new DelegateCommand(SetProvider);
            SetDeviceCommand = new DelegateCommand(SetDevice);
        }

        // ---------- ITrackerExtension lifecycle ---------------------------

        public void OnAttachedToState(TrackerState state)
        {
            mState = state ?? throw new ArgumentNullException(nameof(state));

            // Wire the AutoTracker bridge so pack scripts (init.lua) can
            // find providers / device info. Memory segments themselves
            // are NOT owned by us any more — they live on
            // state.Scripts.MemorySegments (Phase 7.13).
            state.Scripts.SetGlobalObject("AutoTracker", this);

            // Hook PackageLoader's OnPackageLoadComplete to refresh the
            // platform-driven provider list when the state's pack reloads.
            // (OnPackageLoadStarting used to clear our owned segment list
            // before the new init.lua ran; that's now handled by
            // ScriptManager.Reset which the load goes through.)
            PackageLoader.OnPackageLoadStarting += OnAnyPackageLoadStarting;
            PackageLoader.OnPackageLoadComplete += OnAnyPackageLoadComplete;

            // Seed providers from THIS state's PackageInstance — by the
            // time OnAttachedToState fires, the state has been registered
            // with its PackageInstance (and for primary states forked
            // from a loaded definitional, the pack data is already
            // populated).
            var pkg = state.PackageInstance?.GamePackage;
            if (pkg != null)
                ActivePlatform = pkg.Platform;

            // Boot the polling timer.
            BootTimer();
        }

        public void OnDetachedFromState(TrackerState state)
        {
            PackageLoader.OnPackageLoadStarting -= OnAnyPackageLoadStarting;
            PackageLoader.OnPackageLoadComplete -= OnAnyPackageLoadComplete;
            StopAutoTracking();
            // Unsubscribe from this AT's owned provider — defensive,
            // since DisposeProviders below will also dispose them.
            if (mSelectedProvider != null)
            {
                mSelectedProvider.AvailableDevicesChanged -= SelectedProvider_AvailableDevicesChanged;
                mSelectedProvider = null;
            }
            // Dispose the per-AT provider instances we minted in
            // ActivePlatform's setter. Each AT owns its provider
            // instances; releasing them here releases their underlying
            // OS handles (USB / serial / network) so a fresh AT can
            // bind cleanly without inheriting a previous AT's state.
            DisposeProviders();
            Clear();
            Error = false;
            DisposeTimer();
            mState = null;
        }

        void DisposeProviders()
        {
            foreach (var provider in mApplicableProviders)
            {
                try { provider?.Dispose(); } catch { }
            }
            mApplicableProviders.Clear();
        }

        public ITrackerExtension Fork(TrackerState destState)
        {
            // Fresh, disconnected instance bound to the destination
            // state. Memory watches will be re-registered by the fork's
            // ScriptManager.RunCloneFrom + RewireForkedLuaItem path during
            // TrackerState.Fork — by the time OnAttachedToState fires here
            // the fork's scripts are ready to (re-)register watches.
            return new AutoTrackerExtension();
        }

        // Fresh status-bar control instance per call (Avalonia visuals
        // are single-parent — multiple windows binding the per-state
        // indicator each get their own instance pointing at this DC).
        public object StatusBarControl => new AutoTrackerExtensionView { DataContext = this };

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;

        void OnAnyPackageLoadStarting(object sender, EmoTracker.Data.Sessions.PackageLoader.PackageLoadEventArgs e)
        {
            // Filter to OUR state. PackageLoader fires the event for every
            // state load; we only react to our own.
            if (e == null) return;
            if (!ReferenceEquals(e.Target, mState)) return;

            // The state's Lua interpreter is about to be Reset() — close +
            // re-open. Drop our memory segments now so we don't carry
            // callbacks that reference the soon-to-be-closed interpreter.
            // StopAutoTracking first to drain any in-flight poll cleanly;
            // the active provider connection itself is preserved (the user
            // chose to stay connected across reloads), but the watch list
            // is rebuilt by the new init.lua's AddMemoryWatch calls.
            //
            // Without this hook, the next memory poll after reload+
            // reconnect invokes SafeCall on a LuaFunction whose underlying
            // Lua state is closed, which hangs the UI thread inside NLua.
            bool wasConnected = ActiveProvider != null;
            var preservedProvider = SelectedProvider;

            // Drain in-flight + dispose stale segments. Note Clear() also
            // clears the pending update queue.
            Clear();

            // Restore the connection target so the user doesn't have to
            // re-pick it after init.lua repopulates the watch list.
            if (preservedProvider != null)
                SelectedProvider = preservedProvider;
        }

        void OnAnyPackageLoadComplete(object sender, EmoTracker.Data.Sessions.PackageLoader.PackageLoadEventArgs e)
        {
            // Filter to OUR state. PackageLoader fires the event for every
            // state load; we only care about our own pack changes.
            if (e == null) return;
            if (!ReferenceEquals(e.Target, mState)) return;

            if (e.Package != null)
                ActivePlatform = e.Package.Platform;
            else
                ActivePlatform = default;

            NotifyPropertyChanged(nameof(Active));
        }

        public bool Active
        {
            get
            {
                if (mApplicableProviders.Count == 0) return false;
                var scripts = mState?.Scripts;
                if (scripts == null) return false;
                return scripts.MemorySegments.Count > 0 || scripts.MemoryTimers.Count > 0;
            }
        }

        bool mbError = false;
        public bool Error
        {
            get { return mbError; }
            private set { SetProperty(ref mbError, value); }
        }

        // ---------- Provider Management -----------------------------------

        bool mbConnected = false;
        public bool Connected
        {
            get { return mbConnected; }
            private set { SetProperty(ref mbConnected, value); }
        }

        GamePlatform mActivePlatform;
        public GamePlatform ActivePlatform
        {
            get { return mActivePlatform; }
            private set
            {
                if (SetProperty(ref mActivePlatform, value))
                {
                    // Dispose previously-owned provider instances before
                    // replacing the list. GetProvidersForPack now mints
                    // fresh per-state instances, so leaving old ones
                    // un-disposed leaks their underlying OS handles
                    // (USB / serial / network sockets).
                    DisposeProviders();

                    // Use THIS state's pack rather than the app's primary —
                    // multiple states across windows may have different
                    // packs loaded.
                    var pkg = mState?.PackageInstance?.GamePackage;
                    if (pkg != null)
                    {
                        var providers = AutoTrackingProviderRegistry.Instance.GetProvidersForPack(pkg);
                        foreach (var provider in providers)
                            mApplicableProviders.Add(provider);
                    }
                    NotifyPropertyChanged(nameof(ApplicableProviders));
                }
            }
        }

        ObservableCollection<IAutoTrackingProvider> mApplicableProviders = new ObservableCollection<IAutoTrackingProvider>();
        public IEnumerable<IAutoTrackingProvider> ApplicableProviders => mApplicableProviders;

        IAutoTrackingProvider mSelectedProvider;
        public IAutoTrackingProvider SelectedProvider
        {
            get { return mSelectedProvider; }
            private set
            {
                var prev = mSelectedProvider;
                if (SetProperty(ref mSelectedProvider, value))
                {
                    if (prev != null)
                        prev.AvailableDevicesChanged -= SelectedProvider_AvailableDevicesChanged;

                    if (mSelectedProvider != null)
                        mSelectedProvider.AvailableDevicesChanged += SelectedProvider_AvailableDevicesChanged;

                    InvalidateCommandAvailability();
                }
            }
        }

        private void SelectedProvider_AvailableDevicesChanged(object sender, EventArgs e)
        {
            // SNI fires AvailableDevicesChanged from a worker thread on
            // device hot-plug / disconnect. Anything that touches provider
            // state (including DefaultDevice writes, which can trigger
            // device verification inside SNI) MUST run on the UI thread —
            // otherwise SNI logs "Call from invalid thread" and the
            // shared singleton ends up in a corrupted state visible to
            // every per-state AT subscribed to it.
            Dispatch.BeginInvoke(() =>
            {
                if (SelectedProvider != null && SelectedProvider.DefaultDevice == null && SelectedProvider.AvailableDevices.Count > 0)
                    SelectedProvider.DefaultDevice = SelectedProvider.AvailableDevices[0];

                InvalidateCommandAvailability();
                NotifyPropertyChanged(nameof(SelectedProvider));
            });
        }

        IAutoTrackingProvider mActiveProvider;
        public IAutoTrackingProvider ActiveProvider
        {
            get { return mActiveProvider; }
            private set
            {
                Connected = false;
                WaitForPendingMemoryUpdate();

                IAutoTrackingProvider prev = mActiveProvider;
                if (SetProperty(ref mActiveProvider, value))
                {
                    if (prev != null)
                    {
                        prev.ConnectionStatusChanged -= ActiveProvider_ConnectionStatusChanged;
                        prev.DisconnectAsync().GetAwaiter().GetResult();
                    }

                    if (mActiveProvider != null)
                    {
                        Connected = mActiveProvider.IsConnected;
                        mActiveProvider.ConnectionStatusChanged += ActiveProvider_ConnectionStatusChanged;
                    }

                    InvalidateCommandAvailability();
                }
            }
        }

        public IAutoTrackingProvider ActiveConnector => ActiveProvider;

        private void ActiveProvider_ConnectionStatusChanged(object sender, bool connected)
        {
            // SNI fires ConnectionStatusChanged from worker threads on
            // socket-level connect/disconnect. Marshal to the UI thread
            // before mutating Connected (which fires PropertyChanged
            // observed by Avalonia bindings).
            Dispatch.BeginInvoke(() =>
            {
                if (mActiveProvider != null)
                    Connected = connected;
            });
        }

        // ---------- Raw Read API ------------------------------------------

        public byte ReadU8(ulong address, byte defaultVal = 0)
        {
            try
            {
                if (ActiveProvider != null && Connected)
                {
                    byte val = defaultVal;
                    if (ActiveProvider.Read8(address, out val))
                        return val;
                }
            }
            catch (Exception e)
            {
                mState?.Scripts?.OutputError("Error occurred during raw byte read via AutoTracker");
                mState?.Scripts?.OutputException(e);
            }
            return defaultVal;
        }

        public sbyte Read8(ulong address, sbyte defaultVal = 0)
            => unchecked((sbyte)ReadU8(address, unchecked((byte)defaultVal)));

        public ushort ReadU16(ulong address, ushort defaultVal = 0)
        {
            try
            {
                if (ActiveProvider != null && Connected)
                {
                    ushort val = defaultVal;
                    if (ActiveProvider.Read16(address, out val))
                        return val;
                }
            }
            catch (Exception e)
            {
                mState?.Scripts?.OutputError("Error occurred during raw word read via AutoTracker");
                mState?.Scripts?.OutputException(e);
            }
            return defaultVal;
        }

        public short Read16(ulong address, short defaultVal = 0)
            => unchecked((short)ReadU16(address, unchecked((ushort)defaultVal)));

        // ---------- Commands ----------------------------------------------

        public DelegateCommand StartCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand SetProviderCommand { get; }
        public DelegateCommand SetDeviceCommand { get; }

        void InvalidateCommandAvailability()
        {
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }

        private async void SetProvider(object obj)
        {
            IAutoTrackingProvider provider = obj as IAutoTrackingProvider;
            if (provider != null)
            {
                SelectedProvider = provider;
                await provider.RefreshDevicesAsync();

                if (provider.DefaultDevice == null && provider.AvailableDevices.Count > 0)
                    provider.DefaultDevice = provider.AvailableDevices[0];

                InvalidateCommandAvailability();
                NotifyPropertyChanged(nameof(SelectedProvider));
            }
        }

        private void SetDevice(object obj)
        {
            IAutoTrackingDevice device = obj as IAutoTrackingDevice;
            if (device != null && SelectedProvider != null)
            {
                SelectedProvider.DefaultDevice = device;
                if (CanStartAutoTracking())
                    StartAutoTracking();
            }
        }

        private bool CanStopAutoTracking(object obj = null) => ActiveProvider != null;

        private void StopAutoTracking(object obj = null)
        {
            WaitForPendingMemoryUpdate();
            bool bWasActive = ActiveProvider != null;
            ActiveProvider = null;

            if (bWasActive && mState != null)
                ((IScriptManager)mState.Scripts).InvokeStandardCallback(StandardCallback.AutoTrackerStopped);
        }

        private bool CanStartAutoTracking(object obj = null)
            => ActiveProvider == null && SelectedProvider != null && SelectedProvider.DefaultDevice != null;

        private async void StartAutoTracking(object obj = null)
        {
            if (CanStartAutoTracking(obj))
            {
                if (SelectedProvider != null)
                {
                    // Mark every per-state segment dirty so the next poll
                    // ignores its "I read recently, skip me" guard and
                    // forces a fresh read against the just-connected
                    // device.
                    var scripts = mState?.Scripts;
                    if (scripts != null)
                    {
                        foreach (var seg in scripts.MemorySegments) seg.MarkDirty();
                        foreach (var t in scripts.MemoryTimers) t.MarkDirty();
                    }

                    try
                    {
                        await SelectedProvider.ConnectAsync();
                        ActiveProvider = SelectedProvider;
                        if (mState != null)
                            ((IScriptManager)mState.Scripts).InvokeStandardCallback(StandardCallback.AutoTrackerStarted);
                    }
                    catch
                    {
                    }
                }
            }
        }

        // ---------- Memory polling ----------------------------------------

        System.Timers.Timer mUpdateTimer;
        Task mActiveUpdateTask = null;

        void BootTimer()
        {
            if (mUpdateTimer != null) return;
            mUpdateTimer = new System.Timers.Timer(30);
            mUpdateTimer.Elapsed += (s, e) => UpdateMemoryHooks(s, e);
            mUpdateTimer.AutoReset = true;
            mUpdateTimer.Start();
        }

        void DisposeTimer()
        {
            if (mUpdateTimer != null)
            {
                mUpdateTimer.Stop();
                mUpdateTimer.Dispose();
                mUpdateTimer = null;
            }
        }

        private bool HasPendingMemoryUpdate() => mActiveUpdateTask != null;

        public void WaitForPendingMemoryUpdate()
        {
            if (mActiveUpdateTask != null)
                mActiveUpdateTask.Wait(1000);
            mActiveUpdateTask = null;
        }

        private void UpdateMemoryHooks(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            if (HasPendingMemoryUpdate()) return;

            if (ActiveProvider != null && Connected)
            {
                // Phase 7.13: source-of-truth for what to poll is the
                // per-state ScriptManager — it owns LuaMemorySegment +
                // MemoryTimer instances directly. Iterate both.
                var scripts = mState?.Scripts;
                if (scripts != null)
                {
                    foreach (var seg in scripts.MemorySegments)
                    {
                        if (seg.ShouldUpdate(now))
                            PushPendingMemoryUpdate(seg);
                    }
                    foreach (var t in scripts.MemoryTimers)
                    {
                        if (t.ShouldUpdate(now))
                            PushPendingMemoryUpdate(t);
                    }
                }

                PackageManager.Game game = null;
                var packForGame = mState?.PackageInstance?.GamePackage;
                if (packForGame != null)
                {
                    PackageManager.Game gameInstance = PackageManager.Instance.FindGame(packForGame.Game);
                    if (gameInstance != PackageManager.Instance.DefaultGame)
                        game = gameInstance;
                }

                var providerInstance = ActiveProvider;

                if (providerInstance != null && !providerInstance.IsConnected)
                {
                    Dispatch.BeginInvoke(() => StopAutoTracking());
                    Error = true;
                    return;
                }

                mActiveUpdateTask = Task.Run(() =>
                {
                    bool bError = false;
                    try
                    {
                        int countAtStart = GetPendingMemoryUpdateCount();
                        Stopwatch sw = new Stopwatch();
                        sw.Start();

                        int count = 0;
                        while (count < countAtStart && sw.ElapsedMilliseconds < 30)
                        {
                            IUpdateWithConnector update = PopPendingMemoryUpdate();
                            if (update != null)
                            {
                                if (update.UpdateWithConnector(providerInstance, game) != MemoryUpdateResult.Success)
                                    bError = true;
                                ++count;
                            }
                            else
                                break;
                        }
                    }
                    finally
                    {
                        Dispatch.BeginInvoke(() =>
                        {
                            Error = bError;
                            mActiveUpdateTask = null;
                        });
                    }
                });
            }
        }

        // ---------- Memory polling queue ----------------------------------
        // Phase 7.13: segments + timers are owned by the per-state
        // ScriptManager. The polling loop above pulls from
        // mState.Scripts.MemorySegments / MemoryTimers each tick and
        // queues entries that are due for an update here.

        readonly Queue<IUpdateWithConnector> mPendingMemoryUpdateTasks = new Queue<IUpdateWithConnector>();

        void PushPendingMemoryUpdate(IUpdateWithConnector update)
        {
            lock (mPendingMemoryUpdateTasks)
            {
                if (!mPendingMemoryUpdateTasks.Contains(update))
                    mPendingMemoryUpdateTasks.Enqueue(update);
            }
        }

        int GetPendingMemoryUpdateCount()
        {
            lock (mPendingMemoryUpdateTasks)
                return mPendingMemoryUpdateTasks.Count;
        }

        IUpdateWithConnector PopPendingMemoryUpdate()
        {
            lock (mPendingMemoryUpdateTasks)
            {
                try
                {
                    if (mPendingMemoryUpdateTasks.Count > 0)
                        return mPendingMemoryUpdateTasks.Dequeue();
                    else
                        return null;
                }
                catch
                {
                    return null;
                }
            }
        }

        void Clear()
        {
            WaitForPendingMemoryUpdate();
            lock (this)
            {
                mPendingMemoryUpdateTasks.Clear();
                // Segments / timers themselves are owned by the per-state
                // ScriptManager and disposed via its Reset path; we just
                // drop the in-flight queue here.
            }
        }

        public override void Dispose()
        {
            if (mState != null)
                OnDetachedFromState(mState);
            base.Dispose();
        }
    }
}
