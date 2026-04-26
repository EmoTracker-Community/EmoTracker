using EmoTracker.Core;
using EmoTracker.Data.Sessions;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmoTracker
{
    /// <summary>
    /// Phase 7.6: per-window MVVM context. Exposes the set of
    /// <see cref="TrackerState"/>s open as tabs in the window plus the
    /// currently-active one. The window's tab strip + content area bind
    /// against <see cref="OpenStates"/> and <see cref="ActiveState"/>;
    /// switching <see cref="ActiveState"/> tears down the current
    /// LayoutControl bindings and rebuilds them against the newly-active
    /// state's catalogs (per plan §6.4 "expensive rebinds, every state
    /// pays observer cost" — accepted cost for the pointer-swap-free
    /// model).
    /// </summary>
    public class WindowContext : ObservableObject
    {
        readonly Guid mId = Guid.NewGuid();
        public Guid Id => mId;

        string mName;
        public string Name
        {
            get => mName;
            set => SetProperty(ref mName, value);
        }

        readonly ObservableCollection<TrackerState> mOpenStates = new ObservableCollection<TrackerState>();
        public ObservableCollection<TrackerState> OpenStates => mOpenStates;

        TrackerState mActiveState;
        /// <summary>
        /// The currently-active state in this window. Setter raises
        /// PropertyChanged so XAML bindings against
        /// <c>WindowContext.ActiveState.Items</c> /
        /// <c>WindowContext.ActiveState.Locations</c> rebuild.
        /// </summary>
        public TrackerState ActiveState
        {
            get => mActiveState;
            set => SetProperty(ref mActiveState, value);
        }

        // Optional back-ref to the host Window. Set by the window during
        // ctor / activation so multi-window helpers (tear-off, dock) can
        // resolve the visual surface from a WindowContext reference.
        public object OwnerWindow { get; internal set; }

        public WindowContext(string name = null)
        {
            mName = name;
        }

        /// <summary>
        /// Add <paramref name="state"/> to <see cref="OpenStates"/> and
        /// optionally make it the new active state.
        /// </summary>
        public void AddState(TrackerState state, bool makeActive = true)
        {
            if (state == null) return;
            if (!mOpenStates.Contains(state))
                mOpenStates.Add(state);
            if (makeActive)
                ActiveState = state;
        }

        /// <summary>
        /// Remove <paramref name="state"/> from <see cref="OpenStates"/>.
        /// If it was the active state, the next state in the collection
        /// (or null if none remain) becomes active.
        /// </summary>
        public void RemoveState(TrackerState state)
        {
            if (state == null) return;
            int idx = mOpenStates.IndexOf(state);
            if (idx < 0) return;
            mOpenStates.RemoveAt(idx);
            if (ReferenceEquals(ActiveState, state))
            {
                ActiveState = mOpenStates.Count > 0
                    ? mOpenStates[Math.Min(idx, mOpenStates.Count - 1)]
                    : null;
            }
        }

        /// <summary>
        /// Phase 7.9: tear-off helper. Asks the application to spawn a
        /// new <see cref="WindowContext"/> hosting just <paramref name="state"/>;
        /// removes the state from this context. Returns the new context
        /// (host window may not yet exist if 7.6 doesn't construct windows
        /// for additional WindowContexts — the typical usage is via the
        /// app-level helper which builds the window).
        /// </summary>
        public WindowContext OpenStateInNewWindow(TrackerState state)
        {
            if (state == null || !mOpenStates.Contains(state)) return null;
            return ApplicationModel.Instance.OpenStateInNewWindow(this, state);
        }
    }
}
