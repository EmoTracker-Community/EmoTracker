using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConnectorLib;
using EmoTracker.Core;
using EmoTracker.Core.Services;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Packages;
using EmoTracker.Extensions.AutoTracker;
using EmoTracker.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace EmoTracker.Extensions.BontaMultiworld
{
    public enum MultiWorldExtensionStatus
    {
        Unusable,
        Usable,
        Connected,
        Error
    }

    public class MultiWorldExtension : MultiWorldClientSession, Extension
    {
        #region -- Extension Metadata --

        public string Name { get { return "BontaWorld"; } }

        public string UID { get { return "lttp_multiworld_bonta"; } }

        public int Priority { get { return -99; } }

        object mStatusIndicator;

        public object StatusBarControl
        {
            get
            {
                if (mStatusIndicator == null)
                    mStatusIndicator = new MultiWorldExtensionView() { DataContext = this };

                return mStatusIndicator;
            }
        }

        #endregion


        MultiWorldExtensionStatus mStatus = MultiWorldExtensionStatus.Unusable;
        public MultiWorldExtensionStatus Status
        {
            get { return mStatus; }
            private set { SetProperty(ref mStatus, value); }
        }

        DelegateCommand mConnectCmd;
        public DelegateCommand ConnectCmd
        {
            get { return mConnectCmd; }
            private set { SetProperty(ref mConnectCmd, value); }
        }

        DelegateCommand mDisconnectCmd;
        public DelegateCommand DisconnectCmd
        {
            get { return mDisconnectCmd; }
            private set { SetProperty(ref mDisconnectCmd, value); }
        }

        DelegateCommand mJoinGameCmd;
        public DelegateCommand JoinGameCmd
        {
            get { return mJoinGameCmd; }
            private set { SetProperty(ref mJoinGameCmd, value); }
        }

        DelegateCommand mClearMessageLogCmd;
        public DelegateCommand ClearMessageLogCmd
        {
            get { return mClearMessageLogCmd; }
            private set { SetProperty(ref mClearMessageLogCmd, value); }
        }

        DelegateCommand mForfeitCmd;
        public DelegateCommand ForfeitCmd
        {
            get { return mForfeitCmd; }
            private set { SetProperty(ref mForfeitCmd, value); }
        }

        DelegateCommand mPopOutCmd;
        public DelegateCommand PopOutCmd
        {
            get { return mPopOutCmd; }
            private set { SetProperty(ref mPopOutCmd, value); }
        }

        public MultiWorldExtension()
        {
            ConnectCmd = new DelegateCommand(ConnectHandler, CanConnect);
            DisconnectCmd = new DelegateCommand(Disconnect);
            ForfeitCmd = new DelegateCommand(ForfeitHandler);
            JoinGameCmd = new DelegateCommand(Authenticate, CanAuthenticate);
            ClearMessageLogCmd = new DelegateCommand(ClearMessageLog);
            PopOutCmd = new DelegateCommand(PopOutLogWindow, CanPopOutLogWindow);
        }

        protected void RefreshCommandAvailability()
        {
            Dispatch.BeginInvoke(() =>
            {
                ConnectCmd?.RaiseCanExecuteChanged();
                DisconnectCmd?.RaiseCanExecuteChanged();
                JoinGameCmd?.RaiseCanExecuteChanged();
                ForfeitCmd?.RaiseCanExecuteChanged();
                PopOutCmd?.RaiseCanExecuteChanged();
            });
        }

        protected override void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            RefreshCommandAvailability();
            base.NotifyPropertyChanged(propertyName);
        }

        private void Autotracker_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RefreshCommandAvailability();
        }

        private void ConnectHandler(object obj)
        {
            Connect();
        }

        private void ForfeitHandler(object obj)
        {
            Say("!forfeit");
        }

        MultiWorldLogWindow mLogWindow;
        public MultiWorldLogWindow LogWindow
        {
            get { return mLogWindow; }
            set
            {
                if (SetProperty(ref mLogWindow, value) && mLogWindow != null)
                {
                    mLogWindow.Closed += LogWindow_Closed;
                }
            }
        }

        private void LogWindow_Closed(object sender, EventArgs e)
        {
            MultiWorldLogWindow typedSender = sender as MultiWorldLogWindow;
            if (typedSender != null && typedSender == LogWindow)
                LogWindow = null;
        }

        private bool CanPopOutLogWindow(object obj)
        {
            return LogWindow == null;
        }

        private void PopOutLogWindow(object obj)
        {
            LogWindow = new MultiWorldLogWindow() { DataContext = this };
            LogWindow.Show();
        }

        public void Start()
        {
            AutoTrackerExtension autotracker = ExtensionManager.Instance.FindExtension<AutoTrackerExtension>();
            if (autotracker != null)
                autotracker.PropertyChanged += Autotracker_PropertyChanged;
        }

        public void Stop()
        {
        }

        public void OnPackageLoaded()
        {
        }

        public void OnPackageUnloaded()
        {
            Disconnect();
        }

        public JToken SerializeToJson()
        {
            return null;
        }

        public bool DeserializeFromJson(JToken token)
        {
            return true;
        }
    }
}
