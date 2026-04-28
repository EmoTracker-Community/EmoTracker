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
    /// active provider, connection status, memory-watch list, and the
    /// 30 ms polling timer that drives reads.
    ///
    /// <para>
    /// Lua / pack scripts on this state reach the instance via
    /// <c>state.Scripts.SetGlobalObject("AutoTracker", this)</c> and
    /// <c>state.Scripts.SetMemoryWatchService(this)</c>, both wired in
    /// <see cref="OnAttachedToState"/>. Memory watches added by pack
    /// scripts on a fork land on the fork's instance — independent of
    /// the source's connection / watch list.
    /// </para>
    ///
    /// <para>
    /// <b>Fork support.</b> <see cref="Fork"/> currently allocates a
    /// fresh, disconnected instance bound to the destination state. The
    /// memory-watch list is rebuilt by re-running pack scripts on the
    /// fork's <see cref="ScriptManager"/>, which calls
    /// <see cref="AddMemoryWatch"/> on the fork. We do NOT carry the
    /// source's active-provider connection across to the fork — each
    /// state owns its connection, and starting a second connection
    /// against the same emulator from a fork is not what users expect
    /// when forking; if the user wants the fork's auto-tracker connected
    /// they re-connect explicitly.
    /// </para>
    /// </summary>
    public class AutoTrackerExtension : ObservableObject, ITrackerExtension, IMemoryWatchService, IDisposable
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

            // Wire script hooks first so memory watches added during the
            // remainder of the package-load (e.g. by init.lua) land here.
            state.Scripts.SetGlobalObject("AutoTracker", this);
            state.Scripts.SetMemoryWatchService(this);

            // Hook PackageLoader's events filtered to this state.
            //   * OnPackageLoadStarting clears the segment list BEFORE the
            //     new init.lua runs — without this, Reload re-attaches the
            //     state's Lua interpreter while the AutoTracker still
            //     holds segments whose callbacks reference the now-closed
            //     interpreter, and the next memory poll hangs in SafeCall
            //     trying to invoke a stale LuaFunction.
            //   * OnPackageLoadComplete refreshes the platform-driven
            //     provider list when the state's pack reloads.
            PackageLoader.OnPackageLoadStarting += OnAnyPackageLoadStarting;
            PackageLoader.OnPackageLoadComplete += OnAnyPackageLoadComplete;

            // Seed providers from THIS state's PackageInstance — by the
            // time OnAttachedToState fires, the state has been registered
            // with its PackageInstance (and for primary states forked
            // from a loaded definitional, the pack data is already
            // populated). Reading from ApplicationModel.ActiveGamePackage
            // race-conditioned with primary-state assignment; reading
            // from the state's own back-reference is deterministic.
            var pkg = state.PackageInstance?.GamePackage;
            if (pkg != null)
                ActivePlatform = pkg.Platform;

            // Replay memory-watch registrations recorded on the fork
            // source (typically the definitional state, where init.lua
            // ran). The source's recorded LuaFunction callbacks live in
            // its (closed-on-fork-time-but-not-yet-here) interpreter; we
            // remap each through this state's ForkCloner so the dest-side
            // function reference is registered with our (newly-attached)
            // memory service. Without this, the watches that init.lua
            // registered against a definitional state with no service
            // would be silently lost on every primary fork.
            ReplayMemoryWatchesFromForkSource(state);

            // Boot the polling timer.
            BootTimer();
        }

        void ReplayMemoryWatchesFromForkSource(TrackerState state)
        {
            var src = state.Scripts.ForkSource;
            var cloner = state.Scripts.ForkCloner;
            if (src == null || cloner == null) return;

            foreach (var reg in src.MemoryWatchRegistrations)
            {
                // CloneValue handles closures captured outside _G (memory
                // watch callbacks are typically inline closures held only
                // by the C# wrapper, never globally reachable). It returns
                // the cached clone when the function was already produced
                // by CloneAll; otherwise it produces a fresh clone with
                // upvalues remapped through the bridge identity map.
                var luaFunc = cloner.CloneValue(reg.LuaFunc) as NLua.LuaFunction;
                if (luaFunc == null) continue; // diagnostic warned in cloner
                state.Scripts.AddMemoryWatch(reg.Name, reg.StartAddress, reg.Length, luaFunc, reg.Period);
            }
        }

        public void OnDetachedFromState(TrackerState state)
        {
            PackageLoader.OnPackageLoadStarting -= OnAnyPackageLoadStarting;
            PackageLoader.OnPackageLoadComplete -= OnAnyPackageLoadComplete;
            StopAutoTracking();
            Clear();
            Error = false;
            DisposeTimer();
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
            get { return mApplicableProviders.Count > 0 && mActiveMemoryUpdates.Count > 0; }
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
                    mApplicableProviders.Clear();

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
            if (SelectedProvider != null && SelectedProvider.DefaultDevice == null && SelectedProvider.AvailableDevices.Count > 0)
                SelectedProvider.DefaultDevice = SelectedProvider.AvailableDevices[0];

            Dispatch.BeginInvoke(() =>
            {
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
            if (mActiveProvider != null)
                Connected = connected;
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
                    foreach (IUpdateWithConnector update in mActiveMemoryUpdates)
                        update.MarkDirty();

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
                foreach (IUpdateWithConnector update in mActiveMemoryUpdates)
                {
                    if (update.ShouldUpdate(now))
                        PushPendingMemoryUpdate(update);
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

        // ---------- Memory watch service ----------------------------------

        readonly List<IUpdateWithConnector> mActiveMemoryUpdates = new List<IUpdateWithConnector>();

        public IMemorySegment AddMemoryWatch(string name, ulong startAddress, ulong length, Func<IMemorySegment, bool> callback, Action<IMemorySegment> disposeCallback, int period)
        {
            lock (this)
            {
                MemorySegment segment = new MemorySegment(name, startAddress, length, callback, disposeCallback, period);
                mActiveMemoryUpdates.Add(segment);
                NotifyPropertyChanged(nameof(Active));
                return segment;
            }
        }

        public void RemoveMemoryWatch(IMemorySegment segmentBase)
        {
            lock (this)
            {
                MemorySegment segment = segmentBase as MemorySegment;
                if (segment != null && mActiveMemoryUpdates.Contains(segment))
                {
                    mActiveMemoryUpdates.Remove(segment);
                    segment.Dispose();
                }
            }
        }

        public MemoryTimer AddMemoryTimer(string name, Func<IAutoTrackingProvider, PackageManager.Game, bool> callback, int period)
        {
            lock (this)
            {
                MemoryTimer timer = new MemoryTimer(name, callback, period);
                mActiveMemoryUpdates.Add(timer);
                NotifyPropertyChanged(nameof(Active));
                return timer;
            }
        }

        public void RemoveMemoryTimer(MemoryTimer timer)
        {
            lock (this)
            {
                if (timer != null && mActiveMemoryUpdates.Contains(timer))
                {
                    mActiveMemoryUpdates.Remove(timer);
                    DisposeObject(timer);
                }
            }
        }

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
                DisposeCollection(mActiveMemoryUpdates);
                mActiveMemoryUpdates.Clear();
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
