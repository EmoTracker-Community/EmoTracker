using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    /// <summary>
    /// Read-only view of the registered items in a pack — the immutable half of the
    /// item Catalog/State split introduced in Phase 3 of the TrackerSession refactor.
    ///
    /// Today the catalog is co-resident with <c>ItemDatabase</c> and exposes the same
    /// underlying item objects (which still carry both definition and mutable state).
    /// Conceptually, anything reachable through this view is shareable across sessions
    /// without copying; runtime mutable state is segregated into <c>ItemStateStore</c>
    /// and accessed via <c>TransactableObject.PropertyStore</c>.
    ///
    /// In a future phase, items will be split into <c>ItemDefinition</c> records
    /// (held here) and thin wrappers (constructed per-session). For now this type
    /// provides the API surface that downstream code can lean on so the eventual
    /// split is non-breaking.
    /// </summary>
    public class ItemCatalog
    {
        readonly IList<ITrackableItem> mItems;

        public ItemCatalog(IList<ITrackableItem> items)
        {
            mItems = items;
        }

        public IEnumerable<ITrackableItem> Items => mItems;

        public int Count => mItems.Count;

        public ITrackableItem this[int idx] => mItems[idx];
    }
}
