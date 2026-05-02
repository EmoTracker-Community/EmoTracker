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

        // Sequential per-process index assigned at construction. Used for
        // human-readable per-window labels (NDI source naming, broadcast
        // window titles, etc.) where reading "Broadcast 2" is clearer than
        // a Guid suffix. Stable for the lifetime of the WindowContext;
        // never recycled if a window is closed. First context gets 1.
        static int sNextSeq = 0;
        readonly int mSeq = System.Threading.Interlocked.Increment(ref sNextSeq);
        public int Sequence => mSeq;

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
            set
            {
                if (SetProperty(ref mActiveState, value))
                {
                    // Phase 7 XAML migration: derived properties refresh.
                    NotifyPropertyChanged(nameof(ActivePackageInstance));
                    NotifyPropertyChanged(nameof(ActiveGamePackage));
                    NotifyPropertyChanged(nameof(WindowTitle));
                    NotifyPropertyChanged(nameof(HasActiveState));
                    NotifyPropertyChanged(nameof(BroadcastLayout));
                    // Status-bar extension list reaches into ActiveState's
                    // pack + tracker scopes, so it must re-fire too.
                    NotifyPropertyChanged(nameof(ActiveExtensions));
                }
            }
        }

        /// <summary>
        /// Per-window broadcast layout: the active tab's
        /// <c>tracker_broadcast</c> layout, or null when no pack is loaded.
        /// Each window's <see cref="UI.BroadcastView"/> and
        /// <see cref="Extensions.NDI.HiddenBroadcastWindow"/> bind to this
        /// instead of the app-wide <c>ApplicationModel.BroadcastLayout</c>
        /// so each window has its own broadcast feed that follows its
        /// own active tab.
        ///
        /// <para>
        /// Recomputed on every read (cheap dictionary lookup). Fires
        /// PropertyChanged when <see cref="ActiveState"/> changes or when
        /// <see cref="RefreshBroadcastLayout"/> is called explicitly (e.g.
        /// after a pack reload repopulates <see cref="TrackerState.Layouts"/>).
        /// </para>
        /// </summary>
        public EmoTracker.Data.Layout.Layout BroadcastLayout
            => mActiveState?.Layouts?.FindLayout("tracker_broadcast");

        /// <summary>
        /// Re-fires PropertyChanged for <see cref="BroadcastLayout"/>.
        /// Call this after a pack reload so that the
        /// <see cref="Extensions.NDI.HiddenBroadcastWindow"/> and any other
        /// subscribers pick up the new layout instance.
        /// </summary>
        public void RefreshBroadcastLayout()
            => NotifyPropertyChanged(nameof(BroadcastLayout));

        /// <summary>
        /// The aggregated extensions surfaced in this window's status
        /// bar: app-wide + this window's window-extensions + the active
        /// tab's package-extensions + the active tab's tracker-extensions,
        /// each group ordered by priority. Bound by the status-bar
        /// ItemsControl in MainWindow.axaml; refreshes when
        /// <see cref="ActiveState"/> changes.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<EmoTracker.Extensions.IExtension> ActiveExtensions
            => EmoTracker.Extensions.ExtensionManager.Instance.GetActiveExtensionsFor(this);

        /// <summary>
        /// Phase 7 XAML migration: the <see cref="PackageInstance"/>
        /// owning this window's active state, or null. Surfaces enough
        /// pack info for per-window bindings (placeholder visibility,
        /// title) without forcing each consumer to walk
        /// <c>ApplicationModel.PackageInstances</c>.
        /// </summary>
        public PackageInstance ActivePackageInstance
        {
            get
            {
                var state = mActiveState;
                if (state == null) return null;
                foreach (var pi in EmoTracker.ApplicationModel.Instance.PackageInstances)
                {
                    if (pi.States.ContainsKey(state.Id))
                        return pi;
                }
                return null;
            }
        }

        /// <summary>
        /// Phase 7 XAML migration: the active pack's IGamePackage, or null.
        /// Used by the "no package loaded" placeholder in <c>MainWindow</c>.
        /// Pack metadata lives on the active state's
        /// <see cref="TrackerState.PackageInstance"/> — read it through there.
        /// </summary>
        public EmoTracker.Data.IGamePackage ActiveGamePackage
            => mActiveState?.PackageInstance?.GamePackage;

        /// <summary>True iff <see cref="ActiveState"/> is non-null.</summary>
        public bool HasActiveState => mActiveState != null;

        /// <summary>
        /// Phase 7 XAML migration: the window title — pack name + variant
        /// for this window's active state, falling back to the app title.
        /// </summary>
        public string WindowTitle
        {
            get
            {
                var pi = mActiveState?.PackageInstance;
                var pkg = pi?.GamePackage;
                if (pkg == null)
                    return string.Format("EmoTracker {0}", EmoTracker.Core.ApplicationVersion.Current);

                var variant = pi.ActiveVariant;
                if (variant != null)
                    return string.Format("EmoTracker {0}  ::  {1} | {2}",
                        EmoTracker.Core.ApplicationVersion.Current,
                        pkg.DisplayName, variant.DisplayName);
                return string.Format("EmoTracker {0}  ::  {1}",
                    EmoTracker.Core.ApplicationVersion.Current,
                    pkg.DisplayName);
            }
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
        /// Replaces the currently <see cref="ActiveState"/> entry in
        /// <see cref="OpenStates"/> with <paramref name="newState"/>,
        /// preserving the tab's index in the strip. Returns the old
        /// state so the caller can dispose / reassign it; returns null
        /// if there was no active state to replace (in which case
        /// <paramref name="newState"/> is added as a fresh tab and made
        /// active, matching <see cref="AddState"/> semantics).
        ///
        /// <para>
        /// Used by the "open in current tab" UX: clicking a pack from
        /// the installed-packs menu replaces the active tab's state
        /// with a freshly-forked primary, rather than appending a new
        /// tab. To explicitly create a new tab, callers go through
        /// <see cref="AddState"/> (e.g. <c>Ctrl+T</c> -&gt; new empty
        /// tab).
        /// </para>
        /// </summary>
        public TrackerState ReplaceActiveState(TrackerState newState)
        {
            if (newState == null) return null;

            int idx = mActiveState != null ? mOpenStates.IndexOf(mActiveState) : -1;
            if (idx < 0)
            {
                AddState(newState, makeActive: true);
                return null;
            }

            var oldState = mOpenStates[idx];
            if (ReferenceEquals(oldState, newState)) return null;

            // Replace at the same index so the tab strip's element ordering
            // is preserved. ObservableCollection's indexer setter fires a
            // Replace event which the ItemsControl handles correctly.
            mOpenStates[idx] = newState;
            ActiveState = newState;
            return oldState;
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
