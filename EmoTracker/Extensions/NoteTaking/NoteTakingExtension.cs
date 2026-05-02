using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Notes;
using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;
using System.ComponentModel;

namespace EmoTracker.Extensions.NoteTaking
{
    /// <summary>
    /// Per-state NoteTaking extension. Owns a single state-wide
    /// <see cref="NoteTakingSite"/> that's surfaced via the bottom-bar
    /// indicator: a "tracker-level" notebook independent of the
    /// per-Location notes that <c>Location.NoteTakingSite</c> hosts.
    ///
    /// <para>
    /// The status-bar popup binds directly to this site, so adding /
    /// editing / removing notes flows through standard
    /// <see cref="INoteTaking"/> dispatch — same surface as a
    /// per-Location popup, without coupling to any specific Location.
    /// </para>
    /// </summary>
    public sealed class NoteTakingExtension : ObservableObject, ITrackerExtension
    {
        public string Name => "Note Taking";
        public string UID => "emotracker_note_taking";
        public int Priority => -300;

        TrackerState mState;
        public TrackerState State => mState;

        /// <summary>
        /// The state-wide note-taking site this extension hosts. Owns
        /// its own collection of <see cref="Note"/> instances; the
        /// status-bar popup binds to it directly. Per-Location notes
        /// remain on <c>Location.NoteTakingSite</c> and are
        /// independent of this site.
        /// </summary>
        public NoteTakingSite Site { get; } = new NoteTakingSite();

        public NoteTakingExtension()
        {
            // Forward the site's collection-change notifications onto
            // the extension surface so external watchers (status-bar
            // indicator currently bound to <see cref="Empty"/>)
            // re-evaluate. Equivalent to forwarding the
            // PropertyChanged event 1:1 for the `Empty` / `Any`
            // properties — both fire on every site mutation.
            ((INotifyPropertyChanged)Site).PropertyChanged += OnSitePropertyChanged;
        }

        // Avalonia visuals are single-parent: each MainWindow that binds
        // the status bar needs its own control instance. Return fresh per
        // getter call. The DataContext is the per-state site (which
        // implements INoteTaking) so the popup's Notes / Empty bindings
        // resolve directly without a wrapper layer.
        public object StatusBarControl => new NoteTakingStatusBarIndicator { DataContext = Site };

        /// <summary>
        /// True iff this extension's site has no notes. Forwarded from
        /// <see cref="NoteTakingSite.Empty"/> for callers that bind to
        /// the extension itself rather than its site.
        /// </summary>
        public bool Empty => Site.Empty;

        public void OnAttachedToState(TrackerState state)
        {
            mState = state;
            // Stamp the site's OwnerState so MarkdownTextWithItemsNote
            // serialization (which resolves item refs through the
            // owning state's ItemDatabase) works without ambient
            // session lookups. Cleared on detach.
            Site.SetOwnerState(state);
        }

        public void OnDetachedFromState(TrackerState state)
        {
            Site.SetOwnerState(null);
            mState = null;
        }

        public ITrackerExtension Fork(TrackerState destState)
        {
            // A fresh extension with an empty site per fork — each tab
            // gets its own state-wide notebook. Pack-load and prior
            // saves restore the destination's notes via
            // DeserializeFromJson before the fork is presented.
            return new NoteTakingExtension();
        }

        public JToken SerializeToJson()
        {
            // Persist the site's notes as a JSON array. NoteTakingSite
            // already produces a JArray that round-trips through
            // PopulateWithJsonArray below; null when the site is
            // empty (matches NoteTakingSite.AsJsonArray's contract).
            return Site.AsJsonArray();
        }

        public bool DeserializeFromJson(JToken token)
        {
            if (token is JArray arr)
                return Site.PopulateWithJsonArray(arr);
            return true;
        }

        // ---- Plumbing ------------------------------------------------

        void OnSitePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // The site fires "Empty" / "Any" on every collection
            // change. Re-emit "Empty" on the extension so consumers
            // bound to the extension surface (older bindings that
            // pre-date the StatusBarControl DataContext switch) see
            // updates without binding through to the site.
            if (e.PropertyName == nameof(NoteTakingSite.Empty)
                || e.PropertyName == nameof(NoteTakingSite.Any))
            {
                NotifyPropertyChanged(nameof(Empty));
            }
        }
    }
}
