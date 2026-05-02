using System;

namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Resolves <see cref="ModelTypeBase"/>-derived instances by their stable
    /// <see cref="ModelTypeBase.DefinitionId"/>. A resolver represents one state's
    /// view of the model graph: when the state-lifecycle phase introduces per-state
    /// graphs, each state owns its own resolver and the holder model returns it via
    /// <see cref="ModelTypeBase.GetModelResolver"/>.
    ///
    /// <para>
    /// In Phase 2.5 / early Phase 3 a single ambient resolver wraps the singleton
    /// <c>ItemDatabase</c> / <c>Tracker</c> / <c>LocationDatabase</c>; every model
    /// goes through the same resolver via the static <see cref="ModelResolver.Current"/>.
    /// </para>
    /// </summary>
    public interface IModelResolver
    {
        /// <summary>
        /// Returns the model with the given <paramref name="definitionId"/> in this
        /// resolver's graph, cast to <typeparamref name="T"/>. Returns <c>null</c>
        /// if no model with that DefinitionId is present in this graph, or if the
        /// resolved model is not assignable to <typeparamref name="T"/>.
        /// </summary>
        T Resolve<T>(Guid definitionId) where T : class;
    }
}
