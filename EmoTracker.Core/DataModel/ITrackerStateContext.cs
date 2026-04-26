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
    /// <see cref="Scripts"/> is the per-state script manager — a callback
    /// fired from a model in state A goes through state A's Lua
    /// interpreter rather than leaking into the active primary state's
    /// (the bug Phase 5's holder-aware GetScriptManager hook was
    /// scaffolded for).
    /// </para>
    ///
    /// <para>
    /// The transaction-processor surface stays Data-side
    /// (<c>TrackerState.Transactions</c>): it references
    /// <c>IUndoableTransactionProcessor</c> in <c>EmoTracker.Data</c>,
    /// and TransactableModelTypeBase casts <see cref="ModelTypeBase.OwnerState"/>
    /// to <c>TrackerState</c> directly to access it.
    /// </para>
    /// </summary>
    public interface ITrackerStateContext : IModelResolver
    {
        /// <summary>
        /// The per-state script manager. Models in this state route
        /// callback dispatch through here (via
        /// <see cref="ModelTypeBase.GetScriptManager"/>) so Lua callbacks
        /// fire on the right interpreter.
        /// </summary>
        IScriptManager Scripts { get; }
    }
}
