using EmoTracker.Core;
using EmoTracker.Data.Sessions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Owns the four extension scopes:
    /// <list type="bullet">
    ///   <item><b>Application</b> — singletons, one per <see cref="IApplicationExtension"/>
    ///         type, allocated at construction via <c>TypedObjectRegistry</c>.</item>
    ///   <item><b>Window</b> — one per (<see cref="WindowContext"/>,
    ///         <see cref="IWindowExtension"/> type) pair, allocated when a
    ///         window is registered.</item>
    ///   <item><b>Package</b> — one per (<see cref="PackageInstance"/>,
    ///         <see cref="IPackageExtension"/> type) pair, allocated when a
    ///         package instance is created.</item>
    ///   <item><b>Tracker</b> — one per (<see cref="TrackerState"/>,
    ///         <see cref="ITrackerExtension"/> type) pair, allocated when a
    ///         state is registered. Forks of states fork their tracker
    ///         extensions via <see cref="ITrackerExtension.Fork"/>.</item>
    /// </list>
    /// Status-bar UIs bind to <see cref="GetActiveExtensionsFor"/>, which
    /// returns the union of all four scopes' instances applicable to the
    /// given <see cref="WindowContext"/>'s active state — grouped by
    /// scope, sorted by <see cref="IExtension.Priority"/> within each
    /// group.
    /// </summary>
    public sealed class ExtensionManager : ObservableSingleton<ExtensionManager>, IStateLifecycleObserver
    {
        // ------------------------------------------------------------------
        //  Application scope
        // ------------------------------------------------------------------

        readonly List<IApplicationExtension> mApplicationExtensions = new List<IApplicationExtension>();
        public IReadOnlyList<IApplicationExtension> ApplicationExtensions => mApplicationExtensions;

        // ------------------------------------------------------------------
        //  Window scope
        // ------------------------------------------------------------------

        readonly List<Type> mWindowExtensionTypes = new List<Type>();
        readonly Dictionary<WindowContext, List<IWindowExtension>> mWindowExtensions
            = new Dictionary<WindowContext, List<IWindowExtension>>();

        public IReadOnlyList<IWindowExtension> GetWindowExtensions(WindowContext window)
        {
            if (window == null) return Array.Empty<IWindowExtension>();
            return mWindowExtensions.TryGetValue(window, out var list)
                ? (IReadOnlyList<IWindowExtension>)list
                : Array.Empty<IWindowExtension>();
        }

        // ------------------------------------------------------------------
        //  Package scope
        // ------------------------------------------------------------------

        readonly List<Type> mPackageExtensionTypes = new List<Type>();
        readonly Dictionary<PackageInstance, List<IPackageExtension>> mPackageExtensions
            = new Dictionary<PackageInstance, List<IPackageExtension>>();

        public IReadOnlyList<IPackageExtension> GetPackageExtensions(PackageInstance package)
        {
            if (package == null) return Array.Empty<IPackageExtension>();
            return mPackageExtensions.TryGetValue(package, out var list)
                ? (IReadOnlyList<IPackageExtension>)list
                : Array.Empty<IPackageExtension>();
        }

        // ------------------------------------------------------------------
        //  Tracker scope
        // ------------------------------------------------------------------

        readonly List<Type> mTrackerExtensionTypes = new List<Type>();
        readonly Dictionary<TrackerState, List<ITrackerExtension>> mTrackerExtensions
            = new Dictionary<TrackerState, List<ITrackerExtension>>();

        public IReadOnlyList<ITrackerExtension> GetTrackerExtensions(TrackerState state)
        {
            if (state == null) return Array.Empty<ITrackerExtension>();
            return mTrackerExtensions.TryGetValue(state, out var list)
                ? (IReadOnlyList<ITrackerExtension>)list
                : Array.Empty<ITrackerExtension>();
        }

        /// <summary>
        /// Look up the tracker-extension instance of type T attached to
        /// <paramref name="state"/>, or null if no such instance exists.
        /// </summary>
        public T GetTrackerExtension<T>(TrackerState state) where T : class, ITrackerExtension
        {
            if (state == null) return null;
            if (!mTrackerExtensions.TryGetValue(state, out var list)) return null;
            foreach (var ext in list)
                if (ext is T typed) return typed;
            return null;
        }

        // ------------------------------------------------------------------
        //  Terminal scope
        // ------------------------------------------------------------------

        readonly List<Type> mTerminalExtensionTypes = new List<Type>();
        readonly Dictionary<TrackerState, List<ITerminalExtension>> mTerminalExtensions
            = new Dictionary<TrackerState, List<ITerminalExtension>>();

        public IReadOnlyList<ITerminalExtension> GetTerminalExtensions(TrackerState state)
        {
            if (state == null) return Array.Empty<ITerminalExtension>();
            return mTerminalExtensions.TryGetValue(state, out var list)
                ? (IReadOnlyList<ITerminalExtension>)list
                : Array.Empty<ITerminalExtension>();
        }

        // ------------------------------------------------------------------
        //  Application-extension lookups (typed + by-uid)
        // ------------------------------------------------------------------

        public T FindApplicationExtension<T>() where T : class, IApplicationExtension
        {
            foreach (var ext in mApplicationExtensions)
                if (ext is T typed) return typed;
            return null;
        }

        public IApplicationExtension FindApplicationExtensionByUID(string uid)
        {
            foreach (var ext in mApplicationExtensions)
                if (ext.UID.Equals(uid, StringComparison.OrdinalIgnoreCase))
                    return ext;
            return null;
        }

        // ------------------------------------------------------------------
        //  Aggregated active-extensions for status bar
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the extensions that should be surfaced for the given
        /// <paramref name="window"/>'s currently-active tab. Order:
        /// app extensions → window extensions → package extensions →
        /// tracker extensions, with each group sorted ascending by
        /// <see cref="IExtension.Priority"/>.
        /// </summary>
        public IReadOnlyList<IExtension> GetActiveExtensionsFor(WindowContext window)
        {
            var result = new List<IExtension>();

            // App scope: shown in every window.
            result.AddRange(mApplicationExtensions
                .OrderBy(e => e.Priority).Cast<IExtension>());

            // Window scope: this window's instances.
            if (window != null && mWindowExtensions.TryGetValue(window, out var winExts))
                result.AddRange(winExts.OrderBy(e => e.Priority).Cast<IExtension>());

            // Package + tracker scopes: keyed off the active tab's state.
            var state = window?.ActiveState;
            if (state != null)
            {
                var pkg = state.PackageInstance;
                if (pkg != null && mPackageExtensions.TryGetValue(pkg, out var pkgExts))
                    result.AddRange(pkgExts.OrderBy(e => e.Priority).Cast<IExtension>());

                if (mTrackerExtensions.TryGetValue(state, out var trkExts))
                    result.AddRange(trkExts.OrderBy(e => e.Priority).Cast<IExtension>());
            }

            return result;
        }

        // ==================================================================
        //  Discovery + construction
        // ==================================================================

        public ExtensionManager()
        {
            // Application: singleton-instance discovery.
            foreach (var inst in TypedObjectRegistry<IApplicationExtension>.SupportRegistry)
            {
                try
                {
                    mApplicationExtensions.Add(inst);
                }
                catch
                {
                }
            }
            mApplicationExtensions.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            // Window / Package / Tracker: type-only discovery (instances
            // allocated lazily as the corresponding scope appears).
            foreach (var t in TypeRegistry<IWindowExtension>.SupportRegistry)
                mWindowExtensionTypes.Add(t);
            foreach (var t in TypeRegistry<IPackageExtension>.SupportRegistry)
                mPackageExtensionTypes.Add(t);
            foreach (var t in TypeRegistry<ITrackerExtension>.SupportRegistry)
                mTrackerExtensionTypes.Add(t);
            foreach (var t in TypeRegistry<ITerminalExtension>.SupportRegistry)
                mTerminalExtensionTypes.Add(t);
        }

        // ==================================================================
        //  Application-scope lifecycle
        // ==================================================================

        public void Start(IApplicationContext app)
        {
            foreach (var ext in mApplicationExtensions)
            {
                try { ext.Start(app); } catch { }
            }
        }

        public void OnApplicationClosing()
        {
            foreach (var ext in mApplicationExtensions)
            {
                try { ext.Stop(); } catch { }
            }
        }

        // ==================================================================
        //  Window-scope lifecycle (called directly by ApplicationModel)
        // ==================================================================

        public void OnWindowRegistered(WindowContext window)
        {
            if (window == null) return;
            if (mWindowExtensions.ContainsKey(window)) return;

            var list = new List<IWindowExtension>();
            mWindowExtensions[window] = list;

            foreach (var t in mWindowExtensionTypes)
            {
                try
                {
                    var inst = Activator.CreateInstance(t) as IWindowExtension;
                    if (inst != null)
                    {
                        list.Add(inst);
                        inst.OnAttachedToWindow(window);
                    }
                }
                catch
                {
                }
            }
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public void OnWindowUnregistered(WindowContext window)
        {
            if (window == null) return;
            if (!mWindowExtensions.TryGetValue(window, out var list)) return;

            foreach (var ext in list)
            {
                try { ext.OnDetachedFromWindow(window); } catch { }
            }
            mWindowExtensions.Remove(window);
        }

        // ==================================================================
        //  IStateLifecycleObserver — package + state lifecycle
        // ==================================================================

        public void OnPackageInstanceCreated(PackageInstance package)
        {
            if (package == null) return;
            if (mPackageExtensions.ContainsKey(package)) return;

            var list = new List<IPackageExtension>();
            mPackageExtensions[package] = list;

            foreach (var t in mPackageExtensionTypes)
            {
                try
                {
                    var inst = Activator.CreateInstance(t) as IPackageExtension;
                    if (inst != null)
                    {
                        list.Add(inst);
                        inst.OnAttachedToPackage(package);
                    }
                }
                catch
                {
                }
            }
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public void OnPackageInstanceDisposed(PackageInstance package)
        {
            if (package == null) return;
            if (!mPackageExtensions.TryGetValue(package, out var list)) return;

            foreach (var ext in list)
            {
                try { ext.OnDetachedFromPackage(package); } catch { }
            }
            mPackageExtensions.Remove(package);
        }

        public void OnStateRegistered(TrackerState state)
        {
            if (state == null) return;
            AllocateTrackerExtensionsIfMissing(state);
            AllocateTerminalExtensionsIfMissing(state);
        }

        public void OnStateUnregistered(TrackerState state)
        {
            if (state == null) return;
            if (mTrackerExtensions.TryGetValue(state, out var trkList))
            {
                foreach (var ext in trkList)
                    try { ext.OnDetachedFromState(state); } catch { }
                mTrackerExtensions.Remove(state);
            }
            if (mTerminalExtensions.TryGetValue(state, out var termList))
            {
                foreach (var ext in termList)
                    try { ext.OnDetachedFromState(state); } catch { }
                mTerminalExtensions.Remove(state);
            }
        }

        public void OnStateForked(TrackerState source, TrackerState dest)
        {
            if (source == null || dest == null) return;
            ForkTrackerExtensions(source, dest);
            ForkTerminalExtensions(source, dest);
        }

        // ---- Tracker scope: allocate / fork helpers -----------------------

        void AllocateTrackerExtensionsIfMissing(TrackerState state)
        {
            // If the state was forked, its tracker-extensions were already
            // populated by OnStateForked. Idempotent: skip allocation.
            if (mTrackerExtensions.ContainsKey(state)) return;

            var list = new List<ITrackerExtension>();
            mTrackerExtensions[state] = list;

            foreach (var t in mTrackerExtensionTypes)
            {
                try
                {
                    var inst = Activator.CreateInstance(t) as ITrackerExtension;
                    if (inst != null)
                    {
                        list.Add(inst);
                        inst.OnAttachedToState(state);
                    }
                }
                catch
                {
                }
            }
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        void ForkTrackerExtensions(TrackerState source, TrackerState dest)
        {
            if (mTrackerExtensions.ContainsKey(dest)) return;
            if (!mTrackerExtensions.TryGetValue(source, out var srcList)) return;

            var destList = new List<ITrackerExtension>();
            mTrackerExtensions[dest] = destList;

            foreach (var srcExt in srcList)
            {
                try
                {
                    var forked = srcExt.Fork(dest);
                    if (forked != null)
                    {
                        destList.Add(forked);
                        forked.OnAttachedToState(dest);
                    }
                }
                catch
                {
                }
            }
            destList.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        // ---- Terminal scope: allocate / fork helpers ----------------------

        void AllocateTerminalExtensionsIfMissing(TrackerState state)
        {
            if (mTerminalExtensions.ContainsKey(state)) return;

            var list = new List<ITerminalExtension>();
            mTerminalExtensions[state] = list;

            foreach (var t in mTerminalExtensionTypes)
            {
                try
                {
                    var inst = Activator.CreateInstance(t) as ITerminalExtension;
                    if (inst != null)
                    {
                        list.Add(inst);
                        inst.OnAttachedToState(state);
                    }
                }
                catch
                {
                }
            }
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        void ForkTerminalExtensions(TrackerState source, TrackerState dest)
        {
            if (mTerminalExtensions.ContainsKey(dest)) return;
            if (!mTerminalExtensions.TryGetValue(source, out var srcList)) return;

            var destList = new List<ITerminalExtension>();
            mTerminalExtensions[dest] = destList;

            foreach (var srcExt in srcList)
            {
                try
                {
                    var forked = srcExt.Fork(dest);
                    if (forked != null)
                    {
                        destList.Add(forked);
                        forked.OnAttachedToState(dest);
                    }
                }
                catch
                {
                }
            }
            destList.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        // ==================================================================
        //  Helpers
        // ==================================================================

        public static string GetExtensionPath(IExtension instance)
        {
            return Path.Combine(Core.UserDirectory.Path, "extensions", instance.UID);
        }
    }
}
