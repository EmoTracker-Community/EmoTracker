using EmoTracker.Core.DataModel;
using System;

namespace EmoTracker.Data.Core.DataModel
{
    /// <summary>
    /// Bridge between the data-model-v2 reference framework and the legacy
    /// singleton-driven model graph. Walks <c>ItemDatabase.Instance.Items</c>
    /// (and, in Phase 3 onward, the location graph) looking for a
    /// <see cref="ModelTypeBase"/>-derived instance whose
    /// <see cref="ModelTypeBase.DefinitionId"/> matches the requested Guid.
    ///
    /// <para>
    /// Linear scan is acceptable while the framework is singleton-backed (one
    /// graph, modest size — even ALttPR's catalogue is well under a thousand
    /// items). The state-lifecycle phase will introduce per-state resolvers
    /// that pre-index by DefinitionId for O(1) lookup.
    /// </para>
    /// <para>
    /// Installed at app startup via
    /// <c>ModelResolver.Current = new AmbientSingletonModelResolver();</c>.
    /// </para>
    /// </summary>
    public sealed class AmbientSingletonModelResolver : IModelResolver
    {
        public T Resolve<T>(Guid definitionId) where T : class
        {
            if (definitionId == Guid.Empty) return null;

            // ItemDatabase: the only model graph fully on ModelTypeBase as of
            // Phase 2.5. Walk it first.
            foreach (var item in ItemDatabase.Instance.Items)
            {
                if (item is ModelTypeBase mb && mb.DefinitionId == definitionId)
                    return item as T;
            }

            // Phase 3 onward: extend with LocationDatabase / Tracker walks once
            // those graphs become ModelTypeBase-derived. Today they aren't, so
            // a request for a Section / Location DefinitionId returns null
            // through this path. Holders in Phase 2.5 only reference items.

            return null;
        }
    }
}
