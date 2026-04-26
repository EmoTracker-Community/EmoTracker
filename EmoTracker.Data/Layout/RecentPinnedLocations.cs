using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

// Phase 6 step 11: this layout element subscribes to the singleton
// LocationDatabase's PinnedLocations at construction. Same caveat as
// LastClearedLocation: per-state migration is part of the multi-window
// UI follow-up.
#pragma warning disable CS0618

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("recentpins", "recent_pins")]
    public partial class RecentPinnedLocations : ArrayPanel
    {
        // Per-state runtime view of LocationDatabase.PinnedLocations, capped to
        // NumItems. Held as a private field — derived from the singleton +
        // NumItems, recomputed via OnNumItemsChanged / Ncc_CollectionChanged.
        ObservableCollection<Location> mDisplayLocations = new ObservableCollection<Location>();

        public RecentPinnedLocations()
        {
            INotifyCollectionChanged ncc = LocationDatabase.Instance.PinnedLocations as INotifyCollectionChanged;
            if (ncc != null)
                ncc.CollectionChanged += Ncc_CollectionChanged;
        }

        public override void Dispose()
        {
            INotifyCollectionChanged ncc = LocationDatabase.Instance.PinnedLocations as INotifyCollectionChanged;
            if (ncc != null)
                ncc.CollectionChanged -= Ncc_CollectionChanged;

            base.Dispose();
        }

        private void Ncc_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshDisplayItems();
        }

        public IEnumerable<Location> Locations
        {
            get { return mDisplayLocations; }
        }

        [KVOverridable]
        [OnChanged(nameof(RefreshDisplayItems))]
        public partial int NumItems { get; set; }

        [KVOverridable]
        public partial bool CompactDisplay { get; set; }

        protected void RefreshDisplayItems()
        {
            mDisplayLocations.Clear();

            int idx = 0;
            foreach (Location location in LocationDatabase.Instance.PinnedLocations)
            {
                mDisplayLocations.Add(location);

                if (++idx >= NumItems && NumItems > 0)
                    break;
            }
        }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            base.PopulateDefinitionData(data, package, definition);
            definition[nameof(CompactDisplay) + "__def"] = data.GetValue<bool>("compact", true);
            definition[nameof(NumItems) + "__def"] = data.GetValue<int>("num_items", 0);
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            // Both ArrayPanel-level (Orientation, Style) and the local NumItems /
            // CompactDisplay defaults have been seeded into ImmutableData by
            // PopulateDefinitionData. We still need to do the post-definition
            // refresh so mDisplayLocations starts populated.
            base.TryParseInternal(data, package);
            RefreshDisplayItems();
            return true;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            // Each fork gets its own mDisplayLocations subscribed to the
            // singleton's PinnedLocations (Phase 6 will route through the
            // per-state LocationDatabase). Repopulate from current state.
            RefreshDisplayItems();
        }
    }
}
