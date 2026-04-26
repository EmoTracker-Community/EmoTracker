namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Phase 6: the "owning state" surface a <see cref="ModelTypeBase"/>
    /// uses to find its per-state collaborators. Every <c>TrackerState</c>
    /// implementation in <c>EmoTracker.Data</c> implements this interface;
    /// every model in a state holds a back-reference to its owning context
    /// via <see cref="ModelTypeBase.OwnerState"/>.
    ///
    /// <para>
    /// The interface inherits <see cref="IModelResolver"/> because every
    /// state IS a resolver — DefinitionId lookups in state A's graph go
    /// through state A's resolver, naturally chasing per-state instances
    /// without ambient-singleton fallback.
    /// </para>
    ///
    /// <para>
    /// Step 1 (this commit) defines the marker shape only. Subsequent
    /// Phase 6 steps add transaction-processor and script-manager
    /// surfaces here as the per-state migration progresses; for the
    /// data-model and Lua plumbing we already have, holders type their
    /// references to specialized interfaces in <c>EmoTracker.Data</c>
    /// (where the heavier types like <c>IUndoableTransactionProcessor</c>
    /// and <c>ScriptManager</c> live).
    /// </para>
    /// </summary>
    public interface ITrackerStateContext : IModelResolver
    {
    }
}
