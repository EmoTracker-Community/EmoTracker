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
    /// Phase 7.4: per-state auto-tracker runtime. One instance per
    /// <see cref="TrackerState"/>, created by
    /// <see cref="AutoTrackerExtension"/>'s factory hook on
    /// <c>OnStateRegistered</c>. Owns the state's selected provider,
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
    /// The class is internally <see cref="ObservableObject"/> so
    /// <see cref="AutoTrackerExtension"/>'s factory can subscribe to its
    /// PropertyChanged and forward bindings to the UI when the active
    /// window's active-state changes.
    /// </para>
    /// </summary>
    public class AutoTrackerInstance : ObservableObject, IStateScopedExtension, IMemoryWatchService, IDisposable
    {
        public string ExtensionUID => "emotracker_auto_tracking";

        readonly TrackerState mState;
        public TrackerState State => mState;

        public AutoTrackerInstance(TrackerState state)
        {
            mState = state ?? throw new ArgumentNullException(nameof(state));

            StartCommand = new DelegateCommand(StartAutoTracking, CanStartAutoTracking);
            StopCommand = new DelegateCommand(StopAutoTracking, CanStopAutoTracking);
            SetProviderCommand = new DelegateCommand(SetProvider);
            SetDeviceCommand = new DelegateCommand(SetDevice);
        }

        // ---------- IStateScopedExtension ---------------------------------

        public void OnAttachedToState(TrackerState state)
        {
            // Wire script hooks first so memory watches added during the
            // remainder of the package-load (e.g. by init.lua) land here.
            state.Scripts.SetGlobalObject("AutoTracker", this);
            state.Scripts.SetMemoryWatchService(this);

            // Hook PackageLoader's complete event filtered to this state
            // so the provider list refreshes when the state's pack reloads.
            // We can't reach the pre-existing pack-load (already complete by
            // attach time for the primary state); seed providers from the
            // current ActiveGamePackage if any.
            PackageLoader.OnPackageLoadComplete += OnAnyPackageLoadComplete;

            // Seed providers if a pack is already loaded.
            // Phase 7.1 stamps OwnerPackageInstance reachability via the
            // walk; for simplicity we use Tracker.Instance singleton's
            // package which mirrors the active primary state's pack.
            if (Tracker.Instance.ActiveGamePackage != null)
                ActivePlatform = Tracker.Instance.ActiveGamePackage.Platform;

            // Boot the polling timer.
            BootTimer();
        }

        public void OnDetachedFromState(TrackerState state)
        {
            PackageLoader.OnPackageLoadComplete -= OnAnyPackageLoadComplete;
            StopAutoTracking();
            Clear();
            Error = false;
            DisposeTimer();
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;

        public object StatusBarControl => null; // surfaced by the factory

        void OnAnyPackageLoadComplete(object sender, EmoTracker.Data.Sessions.PackageLoader.PackageLoadEventArgs e)
        {
            // Phase 7.4: filter to OUR state. PackageLoader fires the event
            // for every state load; we only care about our own pack changes.
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

                    if (Tracker.Instance.ActiveGamePackage != null)
                    {
                        var providers = AutoTrackingProviderRegistry.Instance.GetProvidersForPack(Tracker.Instance.ActiveGamePackage);
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

            if (bWasActive)
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
                if (Tracker.Instance.ActiveGamePackage != null)
                {
                    PackageManager.Game gameInstance = PackageManager.Instance.FindGame(Tracker.Instance.ActiveGamePackage.Game);
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
            OnDetachedFromState(mState);
            base.Dispose();
        }
    }
}
