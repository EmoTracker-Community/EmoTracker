namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 7.4: install a single observer to receive notifications when
    /// <see cref="TrackerState"/> instances are created on or removed from
    /// a <see cref="PackageInstance"/>. Used by
    /// <c>EmoTracker.Extensions.ExtensionManager</c> (in the EmoTracker
    /// app project) to register / unregister per-state extension
    /// instances (<c>IStateScopedExtension</c>) over the
    /// EmoTracker → EmoTracker.Data project boundary, since EmoTracker.Data
    /// can't reference EmoTracker directly.
    ///
    /// <para>
    /// The observer is installed once at app startup
    /// (<c>App.axaml.cs</c>) via <see cref="StateLifecycle.Observer"/>;
    /// EmoTracker.Data's <c>PackageInstance</c> consults the slot when
    /// states are created/adopted/removed.
    /// </para>
    /// </summary>
    public interface IStateLifecycleObserver
    {
        /// <summary>
        /// Called after <paramref name="state"/> has been added to its
        /// owning <c>PackageInstance.States</c> dictionary. Implementations
        /// typically allocate per-state extension instances and call
        /// their <c>OnAttachedToState</c> hook.
        /// </summary>
        void OnStateRegistered(TrackerState state);

        /// <summary>
        /// Called BEFORE <paramref name="state"/>.Dispose runs (so per-state
        /// extension instances can still query the state for any teardown
        /// they need to do).
        /// </summary>
        void OnStateUnregistered(TrackerState state);
    }

    public static class StateLifecycle
    {
        /// <summary>
        /// The currently installed observer, or null if none. Reads of this
        /// slot are not synchronised — callers (PackageInstance) read once
        /// per state-lifecycle event. The slot is install-once at app
        /// startup; reassignment at runtime is not supported.
        /// </summary>
        public static IStateLifecycleObserver Observer { get; set; }
    }
}
