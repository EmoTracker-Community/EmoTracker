using EmoTracker.Core;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions.NDI
{
    /// <summary>
    /// NDI extension. Exposes the user-facing toggle / status indicator;
    /// the actual broadcast lifecycle (visible <see cref="UI.BroadcastView"/>
    /// and off-screen <see cref="HiddenBroadcastWindow"/>) is owned
    /// per-MainWindow so each app window can broadcast its own active-tab
    /// content as a distinct NDI source. See <c>MainWindow.cs</c>'s
    /// per-window broadcast section.
    /// </summary>
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

        // Avalonia visuals are single-parent: a control instance can only
        // be hosted by one ContentPresenter at a time. The MainWindow tab
        // strip's tear-off path creates a second window that also binds to
        // this property, and re-parenting the same instance throws. Return
        // a fresh control on every getter call so each window gets its own
        // visual; the DataContext binds back to the singleton extension so
        // they all reflect the same state.
        public object StatusBarControl => new NDIStatusIndicator() { DataContext = this };

        public NDIExtension() { }

        public void Start() { }
        public void Stop() { }

        public void OnPackageUnloaded() { }
        public void OnPackageLoaded() { }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
