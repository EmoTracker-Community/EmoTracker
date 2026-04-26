namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 6 step 11: in-process active <see cref="TrackerState"/>
    /// installed by <c>ApplicationModel</c> after pack-load. Used as a
    /// fallback by EmoTracker.Data code that has no
    /// <see cref="EmoTracker.Core.DataModel.ModelTypeBase.OwnerState"/>
    /// available (e.g. legacy callsites in Tracker / LocationDatabase
    /// pack-load paths) but needs to reach the active state's catalogs.
    ///
    /// <para>
    /// This is the EmoTracker.Data analog of <c>ApplicationModel.PrimaryState</c>
    /// — same value, different access point. The duplication exists
    /// because EmoTracker.Data cannot reference the EmoTracker assembly
    /// (where <c>ApplicationModel</c> lives), and the per-catalog
    /// static <c>Current</c> shims were retired in step 11. Tests that
    /// construct standalone catalogs leave this null; production code
    /// installs it via <c>ApplicationModel.RebindActivePackageInstanceFromSingletons</c>.
    /// </para>
    ///
    /// <para>
    /// Pattern at usage sites:
    /// <code>
    /// // Holder available (preferred):
    /// var maps = (this.OwnerState as TrackerState)?.Maps;
    /// // No holder (fallback):
    /// var maps = SessionContext.ActiveState?.Maps;
    /// </code>
    /// </para>
    /// </summary>
    public static class SessionContext
    {
        /// <summary>
        /// The currently-active <see cref="TrackerState"/> as registered by
        /// the application orchestrator. Null before any pack has loaded
        /// (or in test scenarios that don't go through the production
        /// pack-load path); production code defends with <c>?.</c>.
        /// </summary>
        public static TrackerState ActiveState { get; set; }
    }
}
