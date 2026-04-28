using EmoTracker.Core;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions.NDI
{
    /// <summary>
    /// Per-window NDI broadcast extension. One instance per
    /// <see cref="WindowContext"/>. Each window can broadcast its own
    /// active-tab content as a distinct NDI source — the per-window
    /// instance owns the broadcast lifecycle for its host window.
    ///
    /// <para>
    /// The actual broadcast surfaces (visible <see cref="UI.BroadcastView"/>
    /// and off-screen <see cref="HiddenBroadcastWindow"/>) live in
    /// MainWindow.cs's per-window broadcast section and look up the
    /// NDIExtension for their host window via
    /// <c>ExtensionManager.GetWindowExtensions(window)</c>.
    /// </para>
    /// </summary>
    public class NDIExtension : ObservableObject, IWindowExtension
    {
        public string Name => "NewTek NDI®";
        public string UID  => "newtek_ndi_support";
        public int    Priority => -20000;

        WindowContext mWindow;
        public WindowContext Window => mWindow;

        bool mbActive = false;
        public bool Active
        {
            get => mbActive;
            set
            {
                if (SetProperty(ref mbActive, value))
                {
                    // Active toggles whether StatusBarControl returns a
                    // control or null — fire PropertyChanged so the
                    // host window's status-bar binding re-fetches.
                    NotifyPropertyChanged(nameof(StatusBarControl));
                }
            }
        }

        // Return a control only when NDI is actively broadcasting. When
        // inactive, return null so the per-window status-bar ItemsControl
        // skips this slot entirely (otherwise the wrapper ContentControl
        // still consumes WrapPanel width even when the inner image is
        // collapsed). The owning BroadcastView / HiddenBroadcastWindow
        // flips Active when broadcast starts, which fires PropertyChanged
        // on this property and re-fetches the control.
        public object StatusBarControl
            => mbActive ? new NDIStatusIndicator { DataContext = this } : null;

        public NDIExtension() { }

        public void OnAttachedToWindow(WindowContext window)
        {
            mWindow = window;
        }

        public void OnDetachedFromWindow(WindowContext window)
        {
            // Reset state — clean slate if a new window picks up a recycled
            // instance via Activator.CreateInstance.
            Active = false;
            mWindow = null;
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
