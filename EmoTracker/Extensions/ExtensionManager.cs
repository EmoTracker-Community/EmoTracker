using EmoTracker.Core;
using EmoTracker.Data.Sessions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Phase 7.4: <see cref="ExtensionManager"/> implements
    /// <see cref="IStateLifecycleObserver"/> so it can attach / detach
    /// per-state <see cref="IStateScopedExtension"/> instances as
    /// <see cref="TrackerState"/>s are registered with their owning
    /// <see cref="PackageInstance"/>.
    /// </summary>
    public class ExtensionManager : ObservableSingleton<ExtensionManager>, IStateLifecycleObserver
    {
        ObservableCollection<Extension> mExtensions = new ObservableCollection<Extension>();

        public IEnumerable<Extension> Extensions
        {
            get { return mExtensions; }
        }

        public ExtensionManager()
        {
            LoadExtensionModules();

            Type interfaceType = typeof(Extension);

            List<Type> types = new List<Type>();
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type t in a.GetTypes())
                    {
                        try
                        {
                            if (!t.IsAbstract && interfaceType.IsAssignableFrom(t) && !types.Contains(t))
                                types.Add(t);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (Type t in types)
            {
                try
                {
                    Extension instance = Activator.CreateInstance(t) as Extension;
                    if (instance != null)
                        mExtensions.Add(instance);
                }
                catch
                {
                }
            }

            mExtensions.Sort(ExtensionSortKeyFunc);

        }

        public void Start()
        {
            foreach (Extension ext in Extensions)
            {
                ext.Start();
            }
        }

        private object ExtensionSortKeyFunc(Extension arg)
        {
            return arg.Priority;
        } 

        public T FindExtension<T>() where T : class, Extension
        {
            foreach (Extension ext in Extensions)
            {
                if (ext.GetType() == typeof(T))
                    return ext as T;
            }

            return null;
        }

        public Extension FindExtensionByUID(string uid)
        {
            foreach (Extension ext in Extensions)
            {
                if (ext.UID.Equals(uid, StringComparison.OrdinalIgnoreCase))
                    return ext;
            }

            return null;
        }

        public static string GetExtensionPath(Extension instance)
        {
            return Path.Combine(Core.UserDirectory.Path, "extensions", instance.UID);
        }

        private void LoadExtensionModules()
        {
        }

        public void OnApplicationClosing()
        {
            foreach (Extension ext in Extensions)
            {
                ext.Stop();
            }
        }

        public void OnPackageUnloaded()
        {
            foreach (Extension ext in Extensions)
            {
                ext.OnPackageUnloaded();
            }
        }

        public void OnPackageLoaded()
        {
            foreach (Extension ext in Extensions)
            {
                ext.OnPackageLoaded();
            }
        }

        // -------- Phase 7.4: per-state extension lifecycle --------------------

        // Map TrackerState → list of IStateScopedExtension created for it.
        readonly Dictionary<TrackerState, List<IStateScopedExtension>> mStateExtensions
            = new Dictionary<TrackerState, List<IStateScopedExtension>>();

        /// <summary>
        /// Returns the per-state extension instance of type T attached to
        /// <paramref name="state"/>, or null if no such instance exists.
        /// </summary>
        public T GetStateScopedExtension<T>(TrackerState state) where T : class, IStateScopedExtension
        {
            if (state == null) return null;
            if (!mStateExtensions.TryGetValue(state, out var list)) return null;
            foreach (var ext in list)
            {
                if (ext is T typed) return typed;
            }
            return null;
        }

        /// <summary>
        /// Phase 7.4: returns the read-only collection of per-state
        /// extension instances bound to <paramref name="state"/>. Used by
        /// the status-bar surface to enumerate state-scoped indicators.
        /// </summary>
        public IReadOnlyList<IStateScopedExtension> GetStateScopedExtensions(TrackerState state)
        {
            if (state == null) return System.Array.Empty<IStateScopedExtension>();
            if (!mStateExtensions.TryGetValue(state, out var list))
                return System.Array.Empty<IStateScopedExtension>();
            return list;
        }

        /// <summary>
        /// IStateLifecycleObserver: bind every registered factory's
        /// per-state extension to <paramref name="state"/>. Called by
        /// <see cref="PackageInstance.CreateState"/> /
        /// <see cref="PackageInstance.AdoptAsPrimary"/> via
        /// <see cref="StateLifecycle.Observer"/>.
        /// </summary>
        public void OnStateRegistered(TrackerState state)
        {
            if (state == null) return;
            if (mStateExtensions.ContainsKey(state)) return;   // idempotent

            var list = new List<IStateScopedExtension>();
            mStateExtensions[state] = list;

            foreach (var ext in mExtensions)
            {
                if (ext is IStateScopedExtensionFactory factory)
                {
                    try
                    {
                        var instance = factory.CreateForState(state);
                        if (instance != null)
                        {
                            list.Add(instance);
                            instance.OnAttachedToState(state);
                        }
                    }
                    catch (Exception)
                    {
                        // Defensive: a faulty factory shouldn't tear down
                        // state registration for the rest of the extensions.
                    }
                }
            }
        }

        /// <summary>
        /// IStateLifecycleObserver: detach + dispose every per-state
        /// extension bound to <paramref name="state"/>. Called by
        /// <see cref="PackageInstance.RemoveState"/> /
        /// <see cref="PackageInstance.Dispose"/>.
        /// </summary>
        public void OnStateUnregistered(TrackerState state)
        {
            if (state == null) return;
            if (!mStateExtensions.TryGetValue(state, out var list)) return;

            foreach (var ext in list)
            {
                try
                {
                    ext.OnDetachedFromState(state);
                }
                catch (Exception)
                {
                    // Defensive: a faulty per-state extension's detach
                    // shouldn't block the rest of the cleanup.
                }
            }

            mStateExtensions.Remove(state);
        }
    }
}
