using EmoTracker.Core.DataModel;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Core.DataModel
{
    /// <summary>
    /// Phase 6: per-state O(1) <see cref="IModelResolver"/> backed by a
    /// <see cref="Dictionary{Guid, ModelTypeBase}"/> keyed by
    /// <see cref="ModelTypeBase.DefinitionId"/>. Each
    /// <c>EmoTracker.Data.Sessions.TrackerState</c> owns one of these and
    /// populates it as it builds up its model graph (items, locations,
    /// sections, maps, layouts, layout elements with UIDs, etc.).
    ///
    /// <para>
    /// Replaces the Phase 2.5 <see cref="AmbientSingletonModelResolver"/>'s
    /// linear-scan-the-singleton-catalogs shape: the ambient resolver works
    /// for one global graph but doesn't compose across states. The indexed
    /// resolver gives each state its own DefinitionId → instance map and
    /// answers <c>Resolve&lt;T&gt;(Guid)</c> in O(1), with the right
    /// instance for the right state.
    /// </para>
    ///
    /// <para>
    /// The resolver is mutable (Register / Unregister): incremental
    /// population during a coordinated fork is the typical case.
    /// Lookups use a snapshot of the dictionary at call time — no locking
    /// is performed here because the typical caller is the UI thread
    /// (the same thread that's performing the registration). Cross-thread
    /// scenarios (e.g. AutoTracker reads) should marshal through the UI
    /// dispatcher before resolving.
    /// </para>
    /// </summary>
    public sealed class IndexedModelResolver : IModelResolver
    {
        readonly Dictionary<Guid, ModelTypeBase> mIndex = new Dictionary<Guid, ModelTypeBase>();

        /// <summary>
        /// Adds <paramref name="model"/> to the index keyed by its
        /// <see cref="ModelTypeBase.DefinitionId"/>. No-op if the model
        /// has <see cref="Guid.Empty"/> as its DefinitionId (which would
        /// indicate it hasn't been fully initialized yet).
        ///
        /// If a different model is already registered under the same
        /// DefinitionId, the new registration replaces the old. Callers
        /// who care about collisions should check <see cref="IsRegistered"/>
        /// first.
        /// </summary>
        public void Register(ModelTypeBase model)
        {
            if (model == null) return;
            var id = model.DefinitionId;
            if (id == Guid.Empty) return;
            mIndex[id] = model;
        }

        /// <summary>
        /// Removes the model with the given <paramref name="definitionId"/>
        /// from the index. Returns true if a model was removed.
        /// </summary>
        public bool Unregister(Guid definitionId)
        {
            return mIndex.Remove(definitionId);
        }

        /// <summary>
        /// Returns true iff a model with the given
        /// <paramref name="definitionId"/> is currently registered.
        /// </summary>
        public bool IsRegistered(Guid definitionId)
        {
            return mIndex.ContainsKey(definitionId);
        }

        /// <summary>
        /// Number of currently-registered models. Diagnostic only —
        /// production callers shouldn't iterate the resolver.
        /// </summary>
        public int Count => mIndex.Count;

        /// <inheritdoc />
        public T Resolve<T>(Guid definitionId) where T : class
        {
            if (mIndex.TryGetValue(definitionId, out var model))
                return model as T;
            return null;
        }

        /// <summary>
        /// Removes all registrations. Used when a state is torn down or
        /// when the resolver is being repopulated from scratch (e.g. on
        /// pack reload).
        /// </summary>
        public void Clear()
        {
            mIndex.Clear();
        }
    }
}
