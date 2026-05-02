namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Install a single observer to receive notifications about
    /// <see cref="PackageInstance"/> + <see cref="TrackerState"/>
    /// lifecycle events. Used by <c>EmoTracker.Extensions.ExtensionManager</c>
    /// (in the EmoTracker app project) to register / unregister scoped
    /// extension instances over the EmoTracker → EmoTracker.Data project
    /// boundary, since EmoTracker.Data can't reference EmoTracker
    /// directly.
    ///
    /// <para>
    /// The observer is installed once at app startup
    /// (<c>App.axaml.cs</c>) via <see cref="StateLifecycle.Observer"/>.
    /// EmoTracker.Data's <see cref="PackageInstance"/> +
    /// <see cref="TrackerState"/> consult the slot at lifecycle
    /// transitions. New extension scopes that are not relevant to a
    /// particular observer can be left to the no-op default
    /// implementations.
    /// </para>
    /// </summary>
    public interface IStateLifecycleObserver
    {
        /// <summary>
        /// Called after <paramref name="state"/> has been added to its
        /// owning <see cref="PackageInstance.States"/> dictionary.
        /// Implementations typically allocate per-state
        /// (<c>ITrackerExtension</c>) instances and call their
        /// <c>OnAttachedToState</c> hook.
        /// </summary>
        void OnStateRegistered(TrackerState state);

        /// <summary>
        /// Called BEFORE <paramref name="state"/>.Dispose runs (so
        /// per-state extension instances can still query the state for
        /// any teardown they need to do).
        /// </summary>
        void OnStateUnregistered(TrackerState state);

        /// <summary>
        /// Called from <see cref="TrackerState.Fork"/> after the fork's
        /// catalogs are wired but before it is registered with a
        /// <see cref="PackageInstance"/>. Lets observers fork per-state
        /// data (e.g. per-state extension instances) from
        /// <paramref name="source"/> to <paramref name="dest"/> so the
        /// fork inherits whatever the source held. Subsequent
        /// <see cref="OnStateRegistered"/> for the same dest must be a
        /// no-op for any data already present from the fork.
        /// </summary>
        void OnStateForked(TrackerState source, TrackerState dest)
        {
        }

        /// <summary>
        /// Called from the <see cref="PackageInstance"/> constructor
        /// after the instance is fully wired but before any states have
        /// been created on it. Implementations typically allocate
        /// per-package (<c>IPackageExtension</c>) instances and call
        /// their <c>OnAttachedToPackage</c> hook.
        /// </summary>
        void OnPackageInstanceCreated(PackageInstance package)
        {
        }

        /// <summary>
        /// Called from <see cref="PackageInstance.Dispose"/> BEFORE the
        /// instance's states are torn down, so per-package extension
        /// cleanup can still query the instance for any teardown it
        /// needs to do.
        /// </summary>
        void OnPackageInstanceDisposed(PackageInstance package)
        {
        }
    }

    public static class StateLifecycle
    {
        /// <summary>
        /// The currently installed observer, or null if none. Reads of
        /// this slot are not synchronised — callers read once per
        /// lifecycle event. The slot is install-once at app startup;
        /// reassignment at runtime is not supported.
        /// </summary>
        public static IStateLifecycleObserver Observer { get; set; }
    }
}
