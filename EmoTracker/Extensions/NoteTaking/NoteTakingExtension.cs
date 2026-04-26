using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions.NoteTaking
{
    /// <summary>
    /// Phase 7.4: NoteTaking is now per-state. The extension itself is a
    /// minimal factory; per-state instances render the bottom-bar
    /// indicator. Notes themselves live on <c>Location.NoteTakingSite</c>
    /// (per-location, per-state via the Phase 3 Location fork) — the
    /// vestigial app-level <c>NoteTakingSite</c> previously held by this
    /// extension was unused by any other code path and has been removed.
    /// </summary>
    public class NoteTakingExtension : IStateScopedExtensionFactory
    {
        public string Name { get { return "Note Taking"; } }

        public string UID { get { return "emotracker_note_taking"; } }

        public int Priority { get { return -300; } }

        public void Start() { }

        public void Stop() { }

        public void OnPackageUnloaded() { }

        public void OnPackageLoaded() { }

        // App-wide status-bar slot is unused now; per-state instance
        // surfaces its own indicator instead.
        public object StatusBarControl => null;

        // App-wide serialise / deserialise have nothing to persist —
        // notes are per-Location now.
        public JToken SerializeToJson() => null;

        public bool DeserializeFromJson(JToken token) => true;

        public IStateScopedExtension CreateForState(TrackerState state)
        {
            return new NoteTakingInstance();
        }
    }

    /// <summary>
    /// Phase 7.4: per-state NoteTaking instance. Holds the per-window
    /// status-bar indicator surface; serialise / deserialise are no-ops
    /// (notes live on Location.NoteTakingSite, fork-managed by Phase 3).
    /// </summary>
    public sealed class NoteTakingInstance : IStateScopedExtension
    {
        public string ExtensionUID => "emotracker_note_taking";

        NoteTakingStatusBarIndicator mStatusBarControl;
        public object StatusBarControl
        {
            get
            {
                if (mStatusBarControl == null)
                    mStatusBarControl = new NoteTakingStatusBarIndicator();
                return mStatusBarControl;
            }
        }

        public void OnAttachedToState(TrackerState state) { }
        public void OnDetachedFromState(TrackerState state) { }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
