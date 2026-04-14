using Avalonia.Threading;
using EmoTracker.Core;
using EmoTracker.Data;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.ComponentModel;
using EmoTracker.Data.Session;

namespace EmoTracker.Extensions.NDI
{
    public class NDIExtension : ObservableObject, Extension
    {
        public string Name => "NewTek NDI®";
        public string UID  => "newtek_ndi_support";
        public int    Priority => -20000;

        bool mbActive = false;
        public bool Active
        {
            get => mbActive;
            set => SetProperty(ref mbActive, value);
        }

        public object StatusBarControl { get; set; }

        // -------------------------------------------------------------------------
        // Hidden-window-based background NDI broadcasting
        // -------------------------------------------------------------------------
        // When enabled, an off-screen HiddenBroadcastWindow is created as soon as
        // the loaded package exposes a BroadcastLayout.  The hidden window hosts
        // an NdiSendContainer that advertises the NDI source on the network and
        // renders frames on demand (when a receiver connects).
        //
        // The feature is gated on:
        //   1. ApplicationSettings.EnableBackgroundNdi — user opt-out switch.
        //   2. ApplicationModel.Instance.BroadcastLayout having valid content.
        //
        // When either becomes false the hidden window is torn down.  When the
        // setting is off, the visible BroadcastView retains its own
        // NdiSendContainer for the legacy "NDI only while broadcast view is open"
        // behaviour.  See HiddenBroadcastWindow for the cross-platform concerns
        // around its off-screen / opacity strategy.
        // -------------------------------------------------------------------------

        private HiddenBroadcastWindow _hiddenWindow;

        public NDIExtension()
        {
            StatusBarControl = new NDIStatusIndicator() { DataContext = this };
        }

        public void Start()
        {
            // Subscribe to the triggers that can flip the hidden-window condition:
            //   - package (re)load   → BroadcastLayout property change on ApplicationModel
            //   - user toggles setting → EnableBackgroundNdi property change on ApplicationSettings
            ApplicationModel.Instance.PropertyChanged += OnApplicationModelPropertyChanged;
            TrackerSession.Current.Global.PropertyChanged += OnApplicationSettingsPropertyChanged;

            ReconcileHiddenWindow();
        }

        public void Stop()
        {
            ApplicationModel.Instance.PropertyChanged -= OnApplicationModelPropertyChanged;
            TrackerSession.Current.Global.PropertyChanged -= OnApplicationSettingsPropertyChanged;

            DestroyHiddenWindow();
        }

        public void OnPackageUnloaded()
        {
            // Package is gone — nothing to broadcast.
            DestroyHiddenWindow();
        }

        public void OnPackageLoaded()
        {
            // The actual BroadcastLayout is assigned by AcquireLayouts *after*
            // this callback fires, so we don't reconcile here; the PropertyChanged
            // handler on ApplicationModel picks up the new layout.
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;

        // -------------------------------------------------------------------------
        // Change handlers
        // -------------------------------------------------------------------------

        private void OnApplicationModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationModel.BroadcastLayout))
                Dispatcher.UIThread.Post(ReconcileHiddenWindow);
        }

        private void OnApplicationSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationSettings.EnableBackgroundNdi))
                Dispatcher.UIThread.Post(ReconcileHiddenWindow);
        }

        // -------------------------------------------------------------------------
        // Hidden window lifecycle
        // -------------------------------------------------------------------------

        /// <summary>
        /// Brings the hidden window into the correct state for the current
        /// setting / layout combination — creating it if it should exist and
        /// doesn't, destroying it if it shouldn't and does.
        /// </summary>
        private void ReconcileHiddenWindow()
        {
            bool settingEnabled = TrackerSession.Current.Global.EnableBackgroundNdi;
            bool hasContent = HasBroadcastContent();
            bool shouldExist = settingEnabled && hasContent;

            Log.Debug(
                "[NDI] Reconcile hidden window: EnableBackgroundNdi={S}, HasBroadcastContent={C}, shouldExist={Should}, exists={Exists}",
                settingEnabled, hasContent, shouldExist, _hiddenWindow != null);

            if (shouldExist && _hiddenWindow == null)
                CreateHiddenWindow();
            else if (!shouldExist && _hiddenWindow != null)
                DestroyHiddenWindow();
        }

        private static bool HasBroadcastContent()
        {
            var layout = ApplicationModel.Instance.BroadcastLayout;
            return layout?.Root != null;
        }

        private void CreateHiddenWindow()
        {
            try
            {
                _hiddenWindow = new HiddenBroadcastWindow();
                _hiddenWindow.Show();
                Log.Information("[NDI] Hidden broadcast window created and shown.");
            }
            catch (Exception ex)
            {
                // Show() can fail in rare shutdown races; swallow so we don't
                // take down the host app.
                Log.Warning(ex, "[NDI] Failed to create hidden broadcast window: {Msg}", ex.Message);
                _hiddenWindow = null;
            }
        }

        private void DestroyHiddenWindow()
        {
            if (_hiddenWindow == null)
                return;

            try
            {
                _hiddenWindow.Close();
                Log.Information("[NDI] Hidden broadcast window closed.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[NDI] Error closing hidden broadcast window: {Msg}", ex.Message);
            }
            finally
            {
                _hiddenWindow = null;
            }
        }
    }
}
