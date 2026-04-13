using EmoTracker.Core;
using EmoTracker.Core.Services;
using EmoTracker.Data;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Scripting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.AutoTracker
{
    public class AutoTrackerExtension : ObservableObject, Extension, IMemoryWatchService
    {
        #region -- Extension --

        public string Name { get { return "Auto Tracking"; } }

        public string UID { get { return "emotracker_auto_tracking"; } }

        public int Priority { get { return -100; } }

        public object StatusBarControl
        {
            get
            {
                return new AutoTrackerExtensionView { DataContext = this };
            }
        }

        public void OnPackageUnloaded()
        {
            StopAutoTracking();
            Clear();
            Error = false;
        }

        public void OnPackageLoaded()
        {
            if (Tracker.Instance.ActiveGamePackage != null)
                ActivePlatform = Tracker.Instance.ActiveGamePackage.Platform;

            NotifyPropertyChanged("Active");
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

        #endregion

        #region -- Provider Management --

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
                        {
                            mApplicableProviders.Add(provider);
                        }
                    }
                }
            }
        }

        ObservableCollection<IAutoTrackingProvider> mApplicableProviders = new ObservableCollection<IAutoTrackingProvider>();
        public IEnumerable<IAutoTrackingProvider> ApplicableProviders
        {
            get { return mApplicableProviders; }
        }

        IAutoTrackingProvider mSelectedProvider;
        public IAutoTrackingProvider SelectedProvider
        {
            get { return mSelectedProvider; }
            private set
            {
                if (SetProperty(ref mSelectedProvider, value))
                {
                    InvalidateCommandAvailability();
                }
            }
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

        private void ActiveProvider_ConnectionStatusChanged(object sender, bool connected)
        {
            if (mActiveProvider != null)
            {
                Connected = connected;
            }
        }

        #endregion

        #region -- Raw Read API --

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
                ScriptManager.Instance.OutputError("Error occurred during raw byte read via AutoTracker");
                ScriptManager.Instance.OutputException(e);
            }

            return defaultVal;
        }

        public sbyte Read8(ulong address, sbyte defaultVal = 0)
        {
            return unchecked((sbyte)ReadU8(address, unchecked((byte)defaultVal)));
        }

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
                ScriptManager.Instance.OutputError("Error occurred during raw word read via AutoTracker");
                ScriptManager.Instance.OutputException(e);
            }

            return defaultVal;
        }

        public short Read16(ulong address, short defaultVal = 0)
        {
            return unchecked((short)ReadU16(address, unchecked((ushort)defaultVal)));
        }

        #endregion

        #region -- Commands --

        DelegateCommand mStartCommand;
        DelegateCommand mStopCommand;
        DelegateCommand mSetProviderCommand;
        DelegateCommand mSetDeviceCommand;

        public DelegateCommand StartCommand
        {
            get { return mStartCommand; }
            set { SetProperty(ref mStartCommand, value); }
        }

        public DelegateCommand StopCommand
        {
            get { return mStopCommand; }
            set { SetProperty(ref mStopCommand, value); }
        }

        public DelegateCommand SetProviderCommand
        {
            get { return mSetProviderCommand; }
            set { SetProperty(ref mSetProviderCommand, value); }
        }

        public DelegateCommand SetDeviceCommand
        {
            get { return mSetDeviceCommand; }
            set { SetProperty(ref mSetDeviceCommand, value); }
        }

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

                // Auto-select first device if none selected
                if (provider.DefaultDevice == null && provider.AvailableDevices.Count > 0)
                {
                    provider.DefaultDevice = provider.AvailableDevices[0];
                }

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

        private bool CanStopAutoTracking(object obj = null)
        {
            return ActiveProvider != null;
        }

        private void StopAutoTracking(object obj = null)
        {
            WaitForPendingMemoryUpdate();

            bool bWasActive = ActiveProvider != null;
            ActiveProvider = null;

            if (bWasActive)
                ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.AutoTrackerStopped);
        }

        private bool CanStartAutoTracking(object obj = null)
        {
            return ActiveProvider == null && SelectedProvider != null && SelectedProvider.DefaultDevice != null;
        }

        private async void StartAutoTracking(object obj = null)
        {
            if (CanStartAutoTracking(obj))
            {
                if (SelectedProvider != null)
                {
                    //  Force mark all memory updates as dirty to ensure they update
                    foreach (IUpdateWithConnector update in mActiveMemoryUpdates)
                    {
                        update.MarkDirty();
                    }

                    try
                    {
                        await SelectedProvider.ConnectAsync();
                        ActiveProvider = SelectedProvider;

                        ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.AutoTrackerStarted);
                    }
                    catch
                    {
                    }
                }
            }
        }

        #endregion

        public AutoTrackerExtension()
        {
            StartCommand = new DelegateCommand(StartAutoTracking, CanStartAutoTracking);
            StopCommand = new DelegateCommand(StopAutoTracking, CanStopAutoTracking);
            SetProviderCommand = new DelegateCommand(SetProvider);
            SetDeviceCommand = new DelegateCommand(SetDevice);
        }

        System.Timers.Timer mUpdateTimer;

        public void Start()
        {
            ScriptManager.Instance.SetGlobalObject("AutoTracker", this);
            ScriptManager.Instance.SetMemoryWatchService(this);

            mUpdateTimer = new System.Timers.Timer(30);
            mUpdateTimer.Elapsed += (s, e) => UpdateMemoryHooks(s, e);
            mUpdateTimer.AutoReset = true;
            mUpdateTimer.Start();
        }

        Task mActiveUpdateTask = null;

        private bool HasPendingMemoryUpdate()
        {
            return mActiveUpdateTask != null;
        }

        public void WaitForPendingMemoryUpdate()
        {
            if (mActiveUpdateTask != null)
            {
                mActiveUpdateTask.Wait(1000);
            }

            mActiveUpdateTask = null;
        }

        private void UpdateMemoryHooks(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            if (HasPendingMemoryUpdate())
                return;

            if (ActiveProvider != null && Connected)
            {
                foreach (IUpdateWithConnector update in mActiveMemoryUpdates)
                {
                    if (update.ShouldUpdate(now))
                    {
                        PushPendingMemoryUpdate(update);
                    }
                }

                PackageManager.Game game = null;
                if (Tracker.Instance.ActiveGamePackage != null)
                {
                    PackageManager.Game gameInstance = PackageManager.Instance.FindGame(Tracker.Instance.ActiveGamePackage.Game);
                    if (gameInstance != PackageManager.Instance.DefaultGame)
                        game = gameInstance;
                }

                var providerInstance = ActiveProvider;

                //  Detect disconnection — stop autotracking automatically
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
                            {
                                break;
                            }
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

        public void Stop()
        {
            StopAutoTracking();

            if (mUpdateTimer != null)
            {
                mUpdateTimer.Stop();
                mUpdateTimer.Dispose();
                mUpdateTimer = null;
            }
        }

        public JToken SerializeToJson()
        {
            return null;
        }

        public bool DeserializeFromJson(JToken token)
        {
            return true;
        }

        List<IUpdateWithConnector> mActiveMemoryUpdates = new List<IUpdateWithConnector>();

        public IMemorySegment AddMemoryWatch(string name, ulong startAddress, ulong length, Func<IMemorySegment, bool> callback, Action<IMemorySegment> disposeCallback, int period)
        {
            lock (this)
            {
                MemorySegment segment = new MemorySegment(name, startAddress, length, callback, disposeCallback, period);
                mActiveMemoryUpdates.Add(segment);
                NotifyPropertyChanged("Active");

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
                NotifyPropertyChanged("Active");

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

        Queue<IUpdateWithConnector> mPendingMemoryUpdateTasks = new Queue<IUpdateWithConnector>();

        void PushPendingMemoryUpdate(IUpdateWithConnector update)
        {
            lock (mPendingMemoryUpdateTasks)
            {
                if (!mPendingMemoryUpdateTasks.Contains(update))
                {
                    mPendingMemoryUpdateTasks.Enqueue(update);
                }
            }
        }

        int GetPendingMemoryUpdateCount()
        {
            lock (mPendingMemoryUpdateTasks)
            {
                return mPendingMemoryUpdateTasks.Count;
            }
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
    }
}
