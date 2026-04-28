using EmoTracker.Core;
using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Linq;

namespace EmoTracker.Extensions.NoteTaking
{
    /// <summary>
    /// Per-state NoteTaking extension. Hosts the bottom-bar indicator
    /// surface for the state's active locations with notes; the notes
    /// themselves live on <c>Location.NoteTakingSite</c> (per-location,
    /// per-state via the Phase 3 Location fork).
    ///
    /// <para>
    /// Exposes an aggregate <see cref="Empty"/> property the status-bar
    /// indicator binds to: true iff no location in this state has any
    /// notes. Drives the indicator's grey/cyan color: grey when empty,
    /// cyan when at least one location has notes.
    /// </para>
    /// </summary>
    public sealed class NoteTakingExtension : ObservableObject, ITrackerExtension
    {
        public string Name => "Note Taking";
        public string UID => "emotracker_note_taking";
        public int Priority => -300;

        TrackerState mState;
        public TrackerState State => mState;

        // Avalonia visuals are single-parent: each MainWindow that binds
        // the status bar needs its own control instance. Return fresh per
        // getter call. The DataContext is this per-state extension; the
        // indicator's `Empty`-bound foreground walks our state's locations
        // to compute the grey/cyan color.
        public object StatusBarControl => new NoteTakingStatusBarIndicator { DataContext = this };

        /// <summary>
        /// True iff no location in this state has any notes. Computed
        /// on-demand by walking the state's location catalog. Subscribers
        /// receive PropertyChanged via the per-location NoteTakingSite
        /// PropertyChanged forwarders set up in <see cref="OnAttachedToState"/>.
        /// </summary>
        public bool Empty
        {
            get
            {
                var state = mState;
                if (state == null) return true;
                var locs = state.Locations?.AllLocations;
                if (locs == null) return true;
                foreach (var loc in locs)
                {
                    var site = loc?.NoteTakingSite;
                    if (site != null && !site.Empty)
                        return false;
                }
                return true;
            }
        }

        public void OnAttachedToState(TrackerState state)
        {
            mState = state;
            // Hook PackageLoader's complete event (filtered to our state)
            // so the aggregate Empty refreshes after a pack reload — the
            // location set itself changes, and per-location subscriptions
            // need to be re-attached against the new locations.
            EmoTracker.Data.Sessions.PackageLoader.OnPackageLoadComplete += OnAnyPackageLoadComplete;
            HookLocations();
        }

        public void OnDetachedFromState(TrackerState state)
        {
            EmoTracker.Data.Sessions.PackageLoader.OnPackageLoadComplete -= OnAnyPackageLoadComplete;
            UnhookLocations();
            mState = null;
        }

        public ITrackerExtension Fork(TrackerState destState)
        {
            return new NoteTakingExtension();
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;

        // ---- Per-location subscription bookkeeping --------------------

        void OnAnyPackageLoadComplete(object sender, EmoTracker.Data.Sessions.PackageLoader.PackageLoadEventArgs e)
        {
            if (e == null || !ReferenceEquals(e.Target, mState)) return;
            UnhookLocations();
            HookLocations();
            NotifyPropertyChanged(nameof(Empty));
        }

        void HookLocations()
        {
            var state = mState;
            if (state == null) return;
            var locs = state.Locations?.AllLocations;
            if (locs == null) return;
            foreach (var loc in locs)
            {
                var site = loc?.NoteTakingSite;
                if (site is INotifyPropertyChanged notif)
                    notif.PropertyChanged += OnNoteTakingSitePropertyChanged;
            }
        }

        void UnhookLocations()
        {
            var state = mState;
            if (state == null) return;
            var locs = state.Locations?.AllLocations;
            if (locs == null) return;
            foreach (var loc in locs)
            {
                var site = loc?.NoteTakingSite;
                if (site is INotifyPropertyChanged notif)
                    notif.PropertyChanged -= OnNoteTakingSitePropertyChanged;
            }
        }

        void OnNoteTakingSitePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EmoTracker.Data.Notes.NoteTakingSite.Empty))
                NotifyPropertyChanged(nameof(Empty));
        }
    }
}
