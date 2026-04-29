using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Notes;
using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
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
    public sealed class NoteTakingExtension : ObservableObject, ITrackerExtension, INoteTaking
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

        // -- INoteTaking aggregation --------------------------------
        //
        // The status-bar surface re-uses the same NoteTakingIconPopup
        // template as per-Location surfaces, where the DataContext is
        // a NoteTakingSite (which implements INoteTaking). For the
        // status-bar surface the DataContext is this extension — so
        // we implement INoteTaking too, aggregating across every
        // location's NoteTakingSite. Without this, the popup's
        // <ItemsControl ItemsSource="{Binding Notes}"/> binding
        // failed at the extension because no Notes property existed.
        //
        // Aggregation flattens each per-site Notes collection into
        // a fresh enumerable on each access; PropertyChanged for
        // "Notes" fires whenever any per-location site notifies a
        // membership change, so the popup re-binds.

        /// <summary>
        /// All notes across every location in this state, flattened.
        /// </summary>
        public IEnumerable<Note> Notes
        {
            get
            {
                var state = mState;
                if (state == null) yield break;
                var locs = state.Locations?.AllLocations;
                if (locs == null) yield break;
                foreach (var loc in locs)
                {
                    var site = loc?.NoteTakingSite;
                    if (site == null) continue;
                    foreach (var note in site.Notes)
                        yield return note;
                }
            }
        }

        /// <summary>
        /// AddNote at the aggregate level is a no-op — there's no
        /// implicit target location to attach the note to. The
        /// per-location surface (where the DataContext IS a specific
        /// NoteTakingSite) is the right place to add. Returning
        /// false makes the ItemsControl's "Add note" button a
        /// quiet no-op when the popup is opened from the status bar.
        /// </summary>
        public bool AddNote(Note note) => false;

        /// <summary>
        /// Removes <paramref name="note"/> from whichever location's
        /// site contains it. Walks the locations once until a site
        /// claims ownership; cheap because deletes are infrequent
        /// and the location count is bounded.
        /// </summary>
        public bool RemoveNote(Note note)
        {
            if (note == null) return false;
            var state = mState;
            if (state == null) return false;
            var locs = state.Locations?.AllLocations;
            if (locs == null) return false;
            foreach (var loc in locs)
            {
                var site = loc?.NoteTakingSite;
                if (site != null && site.RemoveNote(note))
                    return true;
            }
            return false;
        }

        /// <summary>Clears every per-location site in this state.</summary>
        public void Clear()
        {
            var state = mState;
            if (state == null) return;
            var locs = state.Locations?.AllLocations;
            if (locs == null) return;
            foreach (var loc in locs)
                loc?.NoteTakingSite?.Clear();
        }

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
            NotifyPropertyChanged(nameof(Notes));
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
            // NoteTakingSite fires PropertyChanged for "Empty" /
            // "Any" on every collection-change. Re-emit "Empty" for
            // the existing status-icon binding and "Notes" for the
            // status-bar popup's aggregated ItemsControl binding.
            if (e.PropertyName == nameof(EmoTracker.Data.Notes.NoteTakingSite.Empty)
                || e.PropertyName == nameof(EmoTracker.Data.Notes.NoteTakingSite.Any))
            {
                NotifyPropertyChanged(nameof(Empty));
                NotifyPropertyChanged(nameof(Notes));
            }
        }
    }
}
