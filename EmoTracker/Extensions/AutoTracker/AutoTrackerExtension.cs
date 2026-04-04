using ConnectorLib;
using EmoTracker.Core;
using EmoTracker.Core.Services;
using EmoTracker.Data;
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
                return new AutoTrackerExtensionView() { DataContext = this };
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
            get { return mApplicableConnectorTypes.Count > 0 && mActiveMemoryUpdates.Count > 0; }
        }

        bool mbError = false;
        public bool Error
        {
            get { return mbError; }
            private set { SetProperty(ref mbError, value); }
        }

        #endregion

        #region -- Connector Management --

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
                    mApplicableConnectorTypes.Clear();

                    ConnectorType connectorType;
                    if (ConnectorTypeForGamePlatform(mActivePlatform, out connectorType))
                    {
                        var availableConnectorInstanceTypes = ConnectorFactory.Available[(int)connectorType];
                        if (availableConnectorInstanceTypes != null && availableConnectorInstanceTypes.Length > 0)
                        {
                            foreach (var availableType in availableConnectorInstanceTypes)
                            {
                                if (availableType.Visibility != ConnectorFactory.Visibility.Production)
                                    continue;

                                mApplicableConnectorTypes.Add(new ConnectorTypeDesc()
                                {
                                    Name = availableType.Name,
                                    InstanceType = availableType.Type
                                });
                            }
                        }
                    }
                }
            }
        }

        public class ConnectorTypeDesc : ObservableObject
        {
            public string Name { get; set; }
            public Type InstanceType { get; set; }

            bool mbActive = false;
            public bool Active
            {
                get { return mbActive; }
                set { SetProperty(ref mbActive, value); }
            }
        }

        ObservableCollection<ConnectorTypeDesc> mApplicableConnectorTypes = new ObservableCollection<ConnectorTypeDesc>();
        public IEnumerable<ConnectorTypeDesc> ApplicableConnectorTypes
        {
            get { return mApplicableConnectorTypes; }
        }

        private bool ConnectorTypeForGamePlatform(GamePlatform platform, out ConnectorType connectorType)
        {
            switch (platform)
            {
                case GamePlatform.NES:
                    connectorType = ConnectorType.NESConnector;
                    return true;

                case GamePlatform.SNES:
                    connectorType = ConnectorType.SNESConnector;
                    return true;

                case GamePlatform.N64:
                    connectorType = ConnectorType.N64Connector;
                    return true;

                case GamePlatform.Gameboy:
                    connectorType = ConnectorType.GBConnector;
                    return true;

                case GamePlatform.GBA:
                    connectorType = ConnectorType.GBAConnector;
                    return true;

                case GamePlatform.Genesis:
                    connectorType = ConnectorType.GenesisConnector;
                    return true;
            }

            connectorType = ConnectorType.ExternalConnector;
            return false;
        }

        ConnectorTypeDesc mSelectedConnectorType;
        public ConnectorTypeDesc SelectedConnectorType
        {
            get { return mSelectedConnectorType; }
            private set
            {
                if (SetProperty(ref mSelectedConnectorType, value))
                {
                    ActiveConnector = null;
                    UpdateConnectorTypeDescActiveState(mSelectedConnectorType);
                    InvalidateCommandAvailability();

                    if (CanStartAutoTracking())
                        StartAutoTracking();
                }
            }
        }

        IAddressableConnector mActiveConnector;
        public IAddressableConnector ActiveConnector
        {
            get { return mActiveConnector; }
            private set
            {
                Connected = false;
                WaitForPendingMemoryUpdate();

                IAddressableConnector prev = mActiveConnector;
                if (SetProperty(ref mActiveConnector, value))
                {
                    IGameConnector prevGC = prev as IGameConnector;
                    if (prevGC != null)
                        prevGC.Dispose();

                    IGameConnector gc = mActiveConnector as IGameConnector;
                    if (gc != null)
                    {
                        Connected = gc.Connected;
                        gc.ConnectionStatusChanged += ActiveConnector_ConnectionStatusChanged; ;
                    }

                    InvalidateCommandAvailability();
                }
            }
        }

        private void ActiveConnector_ConnectionStatusChanged(object sender, (ConnectionStatus status, string) e)
        {
            IGameConnector gc = mActiveConnector as IGameConnector;
            if (gc != null)
            {
                Connected = gc.Connected || e.status == ConnectionStatus.Open;
            }
        }

        void UpdateConnectorTypeDescActiveState(ConnectorTypeDesc desc)
        {
            foreach (ConnectorTypeDesc entry in ApplicableConnectorTypes)
            {
                if (object.ReferenceEquals(entry, desc))
                    entry.Active = true;
                else
                    entry.Active = false;
            }
        }

        #endregion

        #region -- Raw Read API --

        public byte ReadU8(ulong address, byte defaultVal = 0)
        {
            try
            {
#if false
                PackageManager.Game game = null;
                if (Tracker.Instance.ActiveGamePackage != null)
                {
                    PackageManager.Game gameInstance = PackageManager.Instance.FindGame(Tracker.Instance.ActiveGamePackage.Game);
                    if (gameInstance != PackageManager.Instance.DefaultGame)
                        game = gameInstance;
                }

                if (game == null || !game.IsMemoryRangeAccessAllowed(address, address))
                    throw new InvalidOperationException("The requested memory address(es) are not allowed to be read");
#endif

                if (ActiveConnector != null && Connected)
                {
                    I8BitConnector as8Bit = ActiveConnector as I8BitConnector;
                    if (as8Bit != null)
                    {
                        byte val = defaultVal;
                        if (as8Bit.Read8(address, out val))
                            return val;
                    }
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
#if false
                PackageManager.Game game = null;
                if (Tracker.Instance.ActiveGamePackage != null)
                {
                    PackageManager.Game gameInstance = PackageManager.Instance.FindGame(Tracker.Instance.ActiveGamePackage.Game);
                    if (gameInstance != PackageManager.Instance.DefaultGame)
                        game = gameInstance;
                }

                if (game == null || !game.IsMemoryRangeAccessAllowed(address, address + 1))
                    throw new InvalidOperationException("The requested memory address(es) are not allowed to be read");
#endif

                if (ActiveConnector != null && Connected)
                {
                    I16BitConnector as16Bit = ActiveConnector as I16BitConnector;
                    if (as16Bit != null)
                    {
                        ushort val = defaultVal;
                        if (as16Bit.Read16(address, out val))
                            return val;
                    }
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
        DelegateCommand mSetConnectorTypeCommand;

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

        public DelegateCommand SetConnectorTypeCommand
        {
            get { return mSetConnectorTypeCommand; }
            set { SetProperty(ref mSetConnectorTypeCommand, value); }
        }

        void InvalidateCommandAvailability()
        {
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }

        private void SetConnectorType(object obj)
        {
            SelectedConnectorType = obj as ConnectorTypeDesc;
        }

        private bool CanStopAutoTracking(object obj = null)
        {
            return ActiveConnector != null;
        }

        private void StopAutoTracking(object obj = null)
        {
            WaitForPendingMemoryUpdate();

            bool bWasActive = ActiveConnector != null;
            ActiveConnector = null;

            if (bWasActive)
                ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.AutoTrackerStopped);
        }

        private bool CanStartAutoTracking(object obj = null)
        {
            return ActiveConnector == null && SelectedConnectorType != null;
        }

        private void StartAutoTracking(object obj = null)
        {
            if (CanStartAutoTracking(obj))
            {
                if (SelectedConnectorType != null)
                {
                    //  Force mark all memory updates as dirty to ensure they update
                    foreach (IUpdateWithConnector update in mActiveMemoryUpdates)
                    {
                        update.MarkDirty();
                    }

                    try
                    {
                        IAddressableConnector instance = Activator.CreateInstance(SelectedConnectorType.InstanceType) as IAddressableConnector;
                        ActiveConnector = instance;

                        ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.AutoTrackerStarted);
                    }
                    catch
                    {
                    }
                }
            }
        }

#endregion

        class ConnectorLibLogger // : ConnectorLib.Common.ILogger
        {
            public void Debug(string msg)
            {
                System.Diagnostics.Debug.Print(msg);
            }

            public void Error(string msg)
            {
                System.Diagnostics.Debug.Print(msg);
            }

            public void Exception(Exception e, string msg)
            {
                System.Diagnostics.Debug.Print(msg);
            }

            public void Info(string msg)
            {
                System.Diagnostics.Debug.Print(msg);
            }

            public void Message(string msg)
            {
                System.Diagnostics.Debug.Print(msg);
            }

            public void Warning(string msg)
            {
                System.Diagnostics.Debug.Print(msg);
            }
        }

        public AutoTrackerExtension()
        {
            //  Call this here to force an exception during load if we can't load the connectorlib DLL
            StopAutoTracking();

            // ConnectorLib.Common.Log.Logger = new ConnectorLibLogger();

            sd2snesConnector.Usb2SnesApplicationName = string.Format("EmoTracker {0}", ApplicationVersion.Current);

            StartCommand = new DelegateCommand(StartAutoTracking, CanStartAutoTracking);
            StopCommand = new DelegateCommand(StopAutoTracking, CanStopAutoTracking);
            SetConnectorTypeCommand = new DelegateCommand(SetConnectorType);
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

            if (ActiveConnector != null && Connected)
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

                var connectorInstance = ActiveConnector;

                IGameConnector gameConnector = connectorInstance as IGameConnector;

                //  ConnectorLib occasionally disconnects a Lua connector without properly updating
                //  the connection status via our callback. particularly when the script is shut down
                //  via the emulator without disconnecting. Detect that here.
                if (gameConnector != null && !gameConnector.Connected)
                {
                    //  Mark all segments dirty to force a re-read when/if we come back from the error
                    foreach (IUpdateWithConnector update in mActiveMemoryUpdates)
                    {
                        update.MarkDirty();
                    }

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
                                if (update.UpdateWithConnector(connectorInstance, game) != MemoryUpdateResult.Success)
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

        public MemoryTimer AddMemoryTimer(string name, Func<IAddressableConnector, PackageManager.Game, bool> callback, int period)
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
