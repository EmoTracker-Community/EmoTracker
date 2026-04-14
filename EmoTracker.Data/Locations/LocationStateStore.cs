using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    /// <summary>
    /// Session-owned store of per-Location and per-Section mutable state.
    /// Phase 4 of the TrackerSession refactor: same shape as <c>ItemStateStore</c>,
    /// but for the location tree. <c>LocationVisualProperties</c> overrides
    /// <c>TransactableObject.PropertyStore</c> to source its dictionary from this
    /// store, so a session's mutable location/section state (Pinned, CapturedItem,
    /// AvailableChestCount, HostedItem, etc.) lives behind a single per-session
    /// data structure that future <c>Fork()</c> can deep-clone without recreating
    /// any Location or Section objects (preserving XAML bindings on the originals).
    /// </summary>
    public class LocationStateStore
    {
        readonly Dictionary<object, Dictionary<string, object>> mStateByOwner = new Dictionary<object, Dictionary<string, object>>();

        /// <summary>
        /// Returns the mutable property dictionary for the given owner, creating an
        /// empty one on first access. Owners are typically <c>Location</c> or
        /// <c>Section</c> instances.
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
        /// Drops state for an owner being disposed/unregistered.
        /// </summary>
        public void RemoveState(object owner)
        {
            mStateByOwner.Remove(owner);
        }

        /// <summary>
        /// Clears all state. Called when a pack reloads and the location tree is
        /// rebuilt.
        /// </summary>
        public void Reset()
        {
            mStateByOwner.Clear();
        }

        /// <summary>Diagnostic: number of owners currently tracked.</summary>
        public int OwnerCount => mStateByOwner.Count;

        /// <summary>
        /// Deep-clones the store for fork isolation (Phase 7). See notes in
        /// <c>ItemStateStore.Clone</c> — same semantics: aliased Location /
        /// Section keys, shallow-copied inner property dicts.
        /// </summary>
        public LocationStateStore Clone()
        {
            var clone = new LocationStateStore();
            foreach (var kv in mStateByOwner)
            {
                clone.mStateByOwner[kv.Key] = new Dictionary<string, object>(kv.Value);
            }
            return clone;
        }
    }
}
