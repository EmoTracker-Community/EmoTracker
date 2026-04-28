using EmoTracker.Core;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions.VariantSwitcher
{
    /// <summary>
    /// Per-window variant switcher: lets the user swap variants on the
    /// active tab's pack within this window. One instance per
    /// <see cref="WindowContext"/>; the control reads
    /// <c>WindowContext.ActivePackageInstance</c> to discover the active
    /// pack's variants and operates on the active tab's state.
    /// </summary>
    public class VariantSwitcherExtension : ObservableObject, IWindowExtension
    {
        public string Name => "Variant Switcher";
        public string UID => "emotracker_variant_switcher";
        public int Priority => -200;

        WindowContext mWindow;
        public WindowContext Window => mWindow;

        // Avalonia visuals are single-parent: each MainWindow that binds
        // the status bar needs its own control instance. Return fresh per
        // getter call — the DataContext binds back to this per-window
        // extension instance so the control resolves the active tab via
        // mWindow when invoked.
        public object StatusBarControl => new VariantSwitcherControl() { DataContext = this };

        public VariantSwitcherExtension() { }

        public void OnAttachedToWindow(WindowContext window)
        {
            mWindow = window;
        }

        public void OnDetachedFromWindow(WindowContext window)
        {
            mWindow = null;
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
