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

    /// <summary>
    /// Static host for the active <see cref="IScriptManager"/>. Mirrors
    /// <see cref="ModelResolver"/>'s install-once shape: app startup
    /// registers the concrete script manager via <see cref="Current"/>'s
    /// setter; pre-Phase-6 code reads through the property to dispatch
    /// callbacks. Phase 6 swaps the holder-aware
    /// <see cref="ModelTypeBase.GetScriptManager"/> override in for
    /// per-state routing without retiring this static.
    /// </summary>
    public static class ScriptManagerHost
    {
        public static IScriptManager Current { get; set; }
    }

    /// <summary>
    /// No-op <see cref="IScriptManager"/> used as the fallback when
    /// <see cref="ScriptManagerHost.Current"/> is null. Returned by
    /// <see cref="ModelTypeBase.GetScriptManager"/> in test scenarios
    /// where app startup hasn't registered the real script manager —
    /// callsites that fire standard callbacks via
    /// <c>holder.GetScriptManager().InvokeStandardCallback(...)</c> stay
    /// safe rather than NRE'ing. The pre-Phase-5 lazy-create behavior
    /// of <c>ScriptManager.Instance</c> served the same role; this is
    /// the equivalent for the holder-aware path.
    /// </summary>
    public sealed class NullScriptManager : IScriptManager
    {
        public static readonly NullScriptManager Instance = new NullScriptManager();
        private NullScriptManager() { }
        public void InvokeStandardCallback(StandardCallback callback, params object[] args)
        {
            // Intentionally no-op.
        }
    }
}
