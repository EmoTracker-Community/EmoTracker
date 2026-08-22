using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Core.Services;
using EmoTracker.Data;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Scripting;
using EmoTracker.Data.Sessions;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

            // Watch the per-state ScriptManager for MemorySegments /
            // MemoryTimers mutations. Pack init.lua registers these
            // during pack-load (covered by OnAnyPackageLoadComplete
            // below), but packs can also register/unregister mid-
            // session — for example via a settings-driven branch in
            // init.lua, or via a pack-script callback that responds to
            // a config change. Active depends on whether ANY watches
            // exist, so we forward those mutation signals onto our own
            // PropertyChanged for Active / StatusBarControl so the
            // status-bar slot collapses + re-appears as registrations
            // come and go.
            state.Scripts.PropertyChanged += OnScriptsPropertyChanged;

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

            // Forks attach with segments already in place (carried via
            // TrackerState.Fork's AdoptForkedSegment loop) and providers
            // just seeded above. Push an Active / StatusBarControl tick
            // so any binding established before this call resolves with
            // the correct visibility from the start.
            NotifyPropertyChanged(nameof(Active));
            NotifyPropertyChanged(nameof(StatusBarControl));
        }

        public void OnDetachedFromState(TrackerState state)
        {
            PackageLoader.OnPackageLoadStarting -= OnAnyPackageLoadStarting;
            PackageLoader.OnPackageLoadComplete -= OnAnyPackageLoadComplete;
            if (state?.Scripts != null)
                state.Scripts.PropertyChanged -= OnScriptsPropertyChanged;
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
        //
        // Returns null when the extension isn't <see cref="Active"/> (no
        // applicable providers, OR no memory watches / timers registered
        // by the pack). The status-bar host's per-extension wrapper has
        // <c>IsVisible="{Binding StatusBarControl, Converter=…IsNotNull}"</c>
        // — null collapses the entire slot so the icon takes zero space,
        // including its Margin. <see cref="Active"/> changes raise
        // <c>PropertyChanged(nameof(StatusBarControl))</c> so the host
        // re-fetches and re-evaluates visibility.
        public object StatusBarControl
            => Active ? new AutoTrackerExtensionView { DataContext = this } : null;

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;

        void OnAnyPackageLoadStarting(object sender, EmoTracker.Data.Sessions.PackageLoader.PackageLoadEventArgs e)
        {
            // Filter to OUR state. PackageLoader fires the event for every
            // state load; we only react to our own.
            if (e == null) return;
            if (!ReferenceEquals(e.Target, mState)) return;

            // The state's Lua interpreter is about to be Reset() — close +
            // re-open. Fully stop autotracking before the reload: keeping
            // the connection alive across reload left the extension stuck
            // in an "active/running" state that didn't actually refresh
            // (the new pack's init.lua re-registers watches against the
            // new Lua state, but the throttle / dirty bookkeeping carried
            // from the old run prevents updates from landing until the
            // user manually stops and restarts).
            //
            // StopAutoTracking drains any in-flight poll, disconnects the
            // active provider, and fires the AutoTrackerStopped callback.
            // Clear() then drops the pending-update queue. The user will
            // re-Start once the pack finishes loading.
            StopAutoTracking();
            Clear();

            // Surface the now-stopped state to bindings; OnAnyPackageLoad-
            // Complete will re-emit these too once new segments / timers
            // are registered, but raise them here so the status bar
            // reflects "not running" during the reload window rather than
            // showing stale running state.
            NotifyPropertyChanged(nameof(Active));
            NotifyPropertyChanged(nameof(StatusBarControl));
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
            NotifyPropertyChanged(nameof(StatusBarControl));
        }

        // Forward MemorySegments / MemoryTimers mutations on the per-
        // state ScriptManager onto Active + StatusBarControl. Hook is
        // installed in OnAttachedToState; ScriptManager raises these
        // PropertyChanged events from AddMemoryWatch / AddMemoryTimer /
        // RemoveMemoryWatch / RemoveMemoryTimer / AdoptForkedSegment /
        // Reset.
        void OnScriptsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScriptManager.MemorySegments)
                || e.PropertyName == nameof(ScriptManager.MemoryTimers))
            {
                NotifyPropertyChanged(nameof(Active));
                NotifyPropertyChanged(nameof(StatusBarControl));
            }
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

        bool mbReconnecting = false;

        /// <summary>
        /// True while a lost provider connection is being transparently
        /// healed in the background. The tracker stays armed (polling
        /// resumes automatically once reconnected) rather than stopping, but
        /// the status icon renders a transient warning so the user sees the
        /// device is briefly unreachable.
        /// </summary>
        public bool Reconnecting
        {
            get { return mbReconnecting; }
            private set { SetProperty(ref mbReconnecting, value); }
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
                    // Provider count is one of the two inputs to Active;
                    // when the platform changes we may be flipping from
                    // "no providers" to "has providers" or vice versa.
                    NotifyPropertyChanged(nameof(Active));
                    NotifyPropertyChanged(nameof(StatusBarControl));
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
                if (mActiveProvider == null) return;

                Connected = connected;

                if (connected)
                {
                    // Connection healed (by BeginReconnect's retry loop or a
                    // user-triggered reconnect). Resume tracking transparently.
                    CompleteReconnect();
                }
                else
                {
                    // Socket-level disconnect. Don't stop tracking — attempt
                    // to heal the connection; warn while it recovers.
                    BeginReconnect();
                }
            });
        }

        // ---------- Automatic reconnect ----------------------------------
        // On a connection loss the tracker stays armed and a retry loop
        // transparently re-establishes the connection. While healing, the
        // device is in a transient warning state (Reconnecting == true) so
        // the icon reflects the temporary unreachability instead of lying
        // green or stopping outright. The underlying provider also has its
        // own reconnect scan timer; this is a secondary, faster retry.

        System.Timers.Timer mReconnectTimer;
        int mReconnectAttempts;

        void BeginReconnect()
        {
            if (mbReconnecting || mActiveProvider == null)
                return;

            Connected = false;

            // Respect the configured attempt budget. A budget of 0 means
            // auto-reconnect is disabled — settle straight into the normal
            // disconnected state without retrying.
            if (ApplicationSettings.Instance.AutoTrackerMaxReconnectAttempts == 0)
            {
                Reconnecting = false;
                mbReconnecting = false;
                return;
            }

            mbReconnecting = true;
            Reconnecting = true;
            Error = false;
            mReconnectAttempts = 0;

            // Mark every per-state segment dirty so the next post-reconnect
            // poll forces a fresh read rather than trusting stale buffers.
            MarkAllSegmentsDirty();

            if (mReconnectTimer == null)
            {
                mReconnectTimer = new System.Timers.Timer(2000);
                mReconnectTimer.Elapsed += OnReconnectTimerElapsed;
                mReconnectTimer.AutoReset = true;
            }
            mReconnectTimer.Stop();
            mReconnectTimer.Start();
        }

        async void OnReconnectTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                var provider = mActiveProvider;
                if (provider == null) { Dispatch.BeginInvoke(CompleteReconnect); return; }
                if (provider.IsConnected) { Dispatch.BeginInvoke(CompleteReconnect); return; }

                int maxAttempts = ApplicationSettings.Instance.AutoTrackerMaxReconnectAttempts;
                if (mReconnectAttempts >= maxAttempts)
                {
                    // Exhausted the attempt budget — give up and settle into
                    // the normal disconnected state (user can Stop / restart).
                    Dispatch.BeginInvoke(GiveUpReconnect);
                    return;
                }

                ++mReconnectAttempts;
                await provider.ConnectAsync();
                if (provider.IsConnected)
                    Dispatch.BeginInvoke(CompleteReconnect);
            }
            catch
            {
                // Keep retrying on the next tick.
            }
        }

        void CompleteReconnect()
        {
            if (mReconnectTimer != null)
            {
                mReconnectTimer.Stop();
                mReconnectTimer.Dispose();
                mReconnectTimer = null;
            }
            mbReconnecting = false;
            Reconnecting = false;
            Connected = mActiveProvider?.IsConnected ?? false;
            MarkAllSegmentsDirty();
        }

        void GiveUpReconnect()
        {
            if (mReconnectTimer != null)
            {
                mReconnectTimer.Stop();
                mReconnectTimer.Dispose();
                mReconnectTimer = null;
            }
            mbReconnecting = false;
            Reconnecting = false;
            Connected = false;
        }

        void MarkAllSegmentsDirty()
        {
            var scripts = mState?.Scripts;
            if (scripts == null) return;
            foreach (var seg in scripts.MemorySegments) seg.MarkDirty();
            foreach (var t in scripts.MemoryTimers) t.MarkDirty();
        }

        void CancelReconnect()
        {
            if (mReconnectTimer != null)
            {
                mReconnectTimer.Stop();
                mReconnectTimer.Dispose();
                mReconnectTimer = null;
            }
            mbReconnecting = false;
            Reconnecting = false;
        }

        // ---------- Raw Read API ------------------------------------------

        public byte ReadU8(ulong address, byte defaultVal = 0)
        {
            try
            {
                // NOTE: deliberately a live device read, not a cached
                // segment snapshot. Packs (e.g. SMZ3) use AutoTracker:ReadU8
                // for live reads to detect game mode and register/unregister
                // memory watches; returning a stale cached byte would change
                // that behavior. The SNI read is deadline-bounded, so a
                // stalled connection surfaces as a transient stall rather
                // than the unbounded UI-thread hang (issue #105).
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
                // Live device read (see ReadU8).
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

        // ------------------------------------------------------------------
        //  MCP / test-harness facing control helpers
        //  These surface the same driver logic the UI commands use, but await
        //  the async device refresh so a headless caller (the smoke runner
        //  via the MCP server) can drive auto-tracking deterministically.
        // ------------------------------------------------------------------

        /// <summary>
        /// Select a provider, refresh its device list, and pick the first
        /// available device (if none is chosen yet) as the default.
        /// Must be called from the UI thread (MCP tools marshal to it).
        /// </summary>
        public async Task SelectProviderAsync(IAutoTrackingProvider provider)
        {
            if (provider == null)
                return;

            SelectedProvider = provider;
            await provider.RefreshDevicesAsync();

            if (provider.DefaultDevice == null && provider.AvailableDevices.Count > 0)
                provider.DefaultDevice = provider.AvailableDevices[0];

            InvalidateCommandAvailability();
            NotifyPropertyChanged(nameof(SelectedProvider));
        }

        /// <summary>
        /// Select a specific device as the provider's default device.
        /// Must be called from the UI thread.
        /// </summary>
        public Task SelectDeviceAsync(IAutoTrackingDevice device)
        {
            if (device != null && SelectedProvider != null)
                SelectedProvider.DefaultDevice = device;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Set a provider option (e.g. SNI address_space / memory_mapping) by
        /// key. Must be called from the UI thread. Returns whether an option
        /// was found and set.
        /// </summary>
        public bool SetProviderOption(string key, object value)
        {
            if (SelectedProvider == null)
                return false;
            foreach (var opt in SelectedProvider.Options)
            {
                if (opt.Key?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
                {
                    opt.Value = value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Start auto-tracking on the currently selected provider/device.
        /// Must be called from the UI thread. Returns true when a provider
        /// became active.
        /// </summary>
        public async Task<bool> StartAutoTrackingAsync()
        {
            if (!CanStartAutoTracking())
                return false;

            CancelReconnect();
            MarkAllSegmentsDirty();

            try
            {
                await SelectedProvider.ConnectAsync();
                ActiveProvider = SelectedProvider;
                if (mState != null)
                    ((IScriptManager)mState.Scripts).InvokeStandardCallback(StandardCallback.AutoTrackerStarted);
                return ActiveProvider != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stop auto-tracking (drains in-flight updates and clears the active
        /// provider). Must be called from the UI thread.
        /// </summary>
        public Task StopAutoTrackingAsync()
        {
            StopAutoTracking();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Poll (on the caller's dispatcher) until a predicate over this
        /// extension returns true or the timeout elapses. Returns the last
        /// predicate result and whether it succeeded within the timeout.
        /// </summary>
        public async Task<(bool success, Exception error)> WaitUntilAsync(Func<AutoTrackerExtension, bool> predicate, int timeoutMs, int intervalMs = 100)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                Exception error = null;
                bool ok = false;
                try
                {
                    ok = await Dispatcher.UIThread.InvokeAsync(() => predicate(this));
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                if (ok)
                    return (true, error);
                if (error != null)
                    return (false, error);

                await Task.Delay(intervalMs);
            }
            return (false, null);
        }

        private bool CanStopAutoTracking(object obj = null) => ActiveProvider != null;

        private void StopAutoTracking(object obj = null)
        {
            WaitForPendingMemoryUpdate();
            CancelReconnect();
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
                    CancelReconnect();
                    MarkAllSegmentsDirty();

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
                    // Connection lost but the ConnectionStatusChanged event
                    // hasn't been processed yet. Kick off transparent
                    // reconnect on the UI thread instead of stopping
                    // tracking (the event handler drives the warning state).
                    Dispatch.BeginInvoke(() => BeginReconnect());
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
