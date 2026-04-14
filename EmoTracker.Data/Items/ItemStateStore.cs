using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    /// <summary>
    /// Session-owned store of per-item mutable state. Each item registered with the
    /// session has an associated property dictionary, which <c>TransactableObject</c>
    /// reads and writes through the active transaction processor.
    ///
    /// Phase 3 of the TrackerSession refactor: this is the mutable half of the
    /// item Catalog/State split. Item objects themselves carry only their immutable
    /// configuration (definition); their runtime mutable state (Active, AcquiredCount,
    /// CurrentStage, badge text overrides, etc.) lives here, keyed by item identity.
    ///
    /// Forking a session deep-copies this store so the forked session sees an
    /// independent copy of every item's mutable state without recreating the item
    /// objects (preserving XAML bindings on the originals).
    /// </summary>
    public class ItemStateStore
    {
        readonly Dictionary<object, Dictionary<string, object>> mStateByOwner = new Dictionary<object, Dictionary<string, object>>();

        /// <summary>
        /// Returns the mutable property dictionary for the given owner, creating an
        /// empty one on first access. Owners are typically <c>ItemBase</c> instances.
        /// </summary>
        public Dictionary<string, object> StateFor(object owner)
        {
            if (!mStateByOwner.TryGetValue(owner, out var dict))
            {
                dict = new Dictionary<string, object>();
                mStateByOwner[owner] = dict;
            }
            return dict;
        }

        /// <summary>
        /// Drops the state for an owner being disposed/unregistered.
        /// </summary>
        public void RemoveState(object owner)
        {
            mStateByOwner.Remove(owner);
        }

        /// <summary>
        /// Clears all state. Called by <c>ItemDatabase.Reset()</c> when a pack reloads.
        /// </summary>
        public void Reset()
        {
            mStateByOwner.Clear();
        }

        /// <summary>
        /// Number of owners currently tracked (diagnostic).
        /// </summary>
        public int OwnerCount => mStateByOwner.Count;
    }
}
