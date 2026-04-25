using EmoTracker.Core.DataModel;
using EmoTracker.Data.Locations;
using System;

namespace EmoTracker.Data.Core.DataModel
{
    /// <summary>
    /// Bridge between the data-model-v2 reference framework and the legacy
    /// singleton-driven model graph. Walks <c>ItemDatabase.Instance.Items</c>
    /// and (Phase 3) <c>LocationDatabase.Instance.AllLocations</c> + each
    /// location's owned Sections, looking for a <see cref="ModelTypeBase"/>-
    /// derived instance whose <see cref="ModelTypeBase.DefinitionId"/> matches
    /// the requested Guid.
    ///
    /// <para>
    /// Linear scan is acceptable while the framework is singleton-backed (one
    /// graph, modest size — even ALttPR's catalogue is well under a thousand
    /// items + locations + sections). The state-lifecycle phase will introduce
    /// per-state resolvers that pre-index by DefinitionId for O(1) lookup.
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

            // Items.
            foreach (var item in ItemDatabase.Instance.Items)
            {
                if (item is ModelTypeBase mb && mb.DefinitionId == definitionId)
                    return item as T;
            }

            // Locations and their owned Sections (Phase 3: both are now
            // ModelTypeBase-derived, so they participate in identity-based
            // resolution).
            foreach (var loc in LocationDatabase.Instance.AllLocations)
            {
                if (loc.DefinitionId == definitionId)
                    return loc as T;

                foreach (var section in loc.Sections)
                {
                    if (section.DefinitionId == definitionId)
                        return section as T;
                }
            }

            return null;
        }
    }
}
