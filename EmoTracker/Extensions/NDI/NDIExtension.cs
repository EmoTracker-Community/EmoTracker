using Avalonia.Threading;
using EmoTracker.Core;
using EmoTracker.Data;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

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
        // Hidden-window-based background NDI broadcasting (Windows only)
        // -------------------------------------------------------------------------
        // When enabled, an off-screen HiddenBroadcastWindow is created as soon as
        // the loaded package exposes a BroadcastLayout.  The hidden window hosts
        // an NdiSendContainer that advertises the NDI source on the network and
        // renders frames on demand (when a receiver connects).
        //
        // The feature is gated on:
        //   1. Running on Windows — other platforms lack verified hidden-window
        //      behaviour for Avalonia's compositor snapshot path.
        //   2. ApplicationSettings.EnableBackgroundNdi — user opt-out switch.
        //   3. ApplicationModel.Instance.BroadcastLayout having valid content.
        //
        // When any of those conditions become false the hidden window is torn
        // down.  The visible BroadcastView retains its own NdiSendContainer for
        // the non-background code path (non-Windows, or setting disabled).
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
            ApplicationSettings.Instance.PropertyChanged += OnApplicationSettingsPropertyChanged;

            ReconcileHiddenWindow();
        }

        public void Stop()
        {
            ApplicationModel.Instance.PropertyChanged -= OnApplicationModelPropertyChanged;
            ApplicationSettings.Instance.PropertyChanged -= OnApplicationSettingsPropertyChanged;

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
        /// platform / setting / layout combination — creating it if it should
        /// exist and doesn't, destroying it if it shouldn't and does.
        /// </summary>
        private void ReconcileHiddenWindow()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool settingEnabled = ApplicationSettings.Instance.EnableBackgroundNdi;
            bool hasContent = HasBroadcastContent();
            bool shouldExist = isWindows && settingEnabled && hasContent;

            Log.Debug(
                "[NDI] Reconcile hidden window: Windows={W}, EnableBackgroundNdi={S}, HasBroadcastContent={C}, shouldExist={Should}, exists={Exists}",
                isWindows, settingEnabled, hasContent, shouldExist, _hiddenWindow != null);

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
