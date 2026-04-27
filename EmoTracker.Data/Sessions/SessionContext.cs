namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 6 step 11 / Phase 7.1.f: in-process active <see cref="TrackerState"/>
    /// installed by <c>ApplicationModel</c> after pack-load. Used as a
    /// fallback by EmoTracker.Data code that has no
    /// <see cref="EmoTracker.Core.DataModel.ModelTypeBase.OwnerState"/>
    /// available (e.g. parse-time callsites in Tracker / LocationDatabase
    /// pack-load paths) but needs to reach the active state's catalogs.
    ///
    /// <para>
    /// <b>Phase 7.1.f decision:</b> the plan's call to retire this slot
    /// outright was reconsidered and the slot is kept by design. Pack-load
    /// is inherently a single-state operation (the loader populates one
    /// target state's catalogs), and parse-time models don't yet have
    /// OwnerState wired — so an ambient "state being loaded into" slot is
    /// structurally needed. Phase 7 documents <see cref="ActiveState"/> as
    /// the home for that role; <c>WindowContext.ActiveState</c> serves
    /// the per-window UI selection. The two are kept in sync by
    /// <c>ApplicationModel.OnActiveStateSwitched</c>. Holder-aware
    /// lookups (<c>this.OwnerState as TrackerState</c>) remain the
    /// preferred call pattern wherever a model holder is available.
    /// </para>
    ///
    /// <para>
    /// This is the EmoTracker.Data analog of <c>ApplicationModel.PrimaryState</c>
    /// — same value, different access point. The duplication exists
    /// because EmoTracker.Data cannot reference the EmoTracker assembly
    /// (where <c>ApplicationModel</c> lives). Tests that construct
    /// standalone catalogs leave this null; production code installs it
    /// via <c>ApplicationModel.RebindActivePackageInstanceFromSingletons</c>.
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
