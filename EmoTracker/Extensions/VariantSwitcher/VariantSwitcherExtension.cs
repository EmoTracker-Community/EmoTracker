using EmoTracker.Core;
using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions.VariantSwitcher
{
    /// <summary>
    /// Per-state variant switcher: lets the user swap variants on the
    /// active tab's pack. One instance per <see cref="TrackerState"/>;
    /// the control reads variants from the owning state's
    /// <see cref="TrackerState.PackageInstance"/> and operates on that
    /// state when activated.
    /// </summary>
    public class VariantSwitcherExtension : ObservableObject, ITrackerExtension
    {
        public string Name => "Variant Switcher";
        public string UID => "emotracker_variant_switcher";
        public int Priority => -200;

        TrackerState mState;
        public TrackerState State => mState;

        // Avalonia visuals are single-parent: each MainWindow that binds
        // the status bar needs its own control instance. Return fresh per
        // getter call — the DataContext binds back to this per-state
        // extension instance so the control resolves the active tab via
        // mState when invoked.
        public object StatusBarControl => new VariantSwitcherControl() { DataContext = this };

        public VariantSwitcherExtension() { }

        public void OnAttachedToState(TrackerState state)
        {
            mState = state;
        }

        public void OnDetachedFromState(TrackerState state)
        {
            mState = null;
        }

        public ITrackerExtension Fork(TrackerState destState)
        {
            // Stateless apart from the back-reference; allocate fresh.
            return new VariantSwitcherExtension();
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
