namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Identifies a "standard" Lua callback name dispatched through
    /// <see cref="IScriptManager.InvokeStandardCallback"/>. Each value maps
    /// to a specific Lua global function name (e.g. <c>AccessibilityUpdated</c>
    /// → <c>tracker_on_accessibility_updated</c>) at the implementation
    /// boundary; callers don't need to know the underlying function names.
    ///
    /// <para>
    /// Defined in <c>EmoTracker.Core</c> so <see cref="ModelTypeBase.GetScriptManager"/>
    /// can return a typed <see cref="IScriptManager"/> without Core taking a
    /// dependency on the concrete <c>ScriptManager</c> implementation in
    /// <c>EmoTracker.Data</c>.
    /// </para>
    /// </summary>
    public enum StandardCallback
    {
        AccessibilityUpdating,
        AccessibilityUpdated,
        StartLoadingSaveFile,
        FinishLoadingSaveFile,
        PackReady,
        AutoTrackerStarted,
        AutoTrackerStopped,
        LocationUpdating,
        LocationUpdated,
    }

    /// <summary>
    /// Minimal interface for the holder-aware script-callback dispatch
    /// surface. Defined in <c>EmoTracker.Core</c> so model types can
    /// reference it through <see cref="ModelTypeBase.GetScriptManager"/>;
    /// the concrete implementation in <c>EmoTracker.Data</c>'s
    /// <c>ScriptManager</c> implements this interface.
    ///
    /// <para>
    /// Phase 5 plumbs the indirection (<see cref="ModelTypeBase.GetScriptManager"/>
    /// + <see cref="ScriptManagerHost.Current"/>) so per-state model graphs in
    /// Phase 6 can override <see cref="ModelTypeBase.GetScriptManager"/> on a
    /// holder-by-holder basis without touching call sites. Until Phase 6
    /// lands, every holder's <see cref="ModelTypeBase.GetScriptManager"/>
    /// returns <see cref="ScriptManagerHost.Current"/> — the same singleton
    /// the legacy <c>ScriptManager.Instance</c> shim points at.
    /// </para>
    /// </summary>
    public interface IScriptManager
    {
        /// <summary>
        /// Invokes the Lua function corresponding to <paramref name="callback"/>
        /// (if defined by the loaded pack), passing through <paramref name="args"/>.
        /// Failures are caught and reported through the implementation's
        /// error-output channel; callers do not see exceptions.
        /// </summary>
        void InvokeStandardCallback(StandardCallback callback, params object[] args);
    }
}
