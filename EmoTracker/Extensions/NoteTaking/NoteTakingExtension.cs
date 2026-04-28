using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions.NoteTaking
{
    /// <summary>
    /// Per-state NoteTaking extension. Hosts the bottom-bar indicator
    /// surface for the state's active locations with notes; the notes
    /// themselves live on <c>Location.NoteTakingSite</c> (per-location,
    /// per-state via the Phase 3 Location fork).
    /// </summary>
    public sealed class NoteTakingExtension : ITrackerExtension
    {
        public string Name => "Note Taking";
        public string UID => "emotracker_note_taking";
        public int Priority => -300;

        // Avalonia visuals are single-parent: each MainWindow that binds
        // the status bar needs its own control instance. Return fresh per
        // getter call.
        public object StatusBarControl => new NoteTakingStatusBarIndicator();

        public void OnAttachedToState(TrackerState state) { }
        public void OnDetachedFromState(TrackerState state) { }

        public ITrackerExtension Fork(TrackerState destState)
        {
            // Stateless aside from the indicator surface — fresh instance.
            return new NoteTakingExtension();
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
