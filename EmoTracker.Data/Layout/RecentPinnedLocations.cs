using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("recentpins", "recent_pins")]
    public partial class RecentPinnedLocations : ArrayPanel
    {
        // Per-state runtime view of LocationDatabase.PinnedLocations, capped to
        // NumItems. Held as a private field — derived from the owning state's
        // pinned-locations collection + NumItems.
        ObservableCollection<Location> mDisplayLocations = new ObservableCollection<Location>();
        INotifyCollectionChanged mSubscribedNcc;

        public RecentPinnedLocations()
        {
        }

        public override void Dispose()
        {
            UnsubscribePinned();
            base.Dispose();
        }

        // OwnerState is stamped at construction time, so we wire the
        // subscription wherever this element is finalised: at the end of
        // TryParseInternal for the initial pack-load, and in OnForked for
        // forked instances.
        void SubscribeToOwnerStatePinned()
        {
            UnsubscribePinned();
            var pinned = (this.OwnerState as Sessions.TrackerState)?.Locations.PinnedLocations as INotifyCollectionChanged;
            if (pinned != null)
            {
                mSubscribedNcc = pinned;
                pinned.CollectionChanged += Ncc_CollectionChanged;
            }
            RefreshDisplayItems();
        }

        void UnsubscribePinned()
        {
            if (mSubscribedNcc != null)
            {
                mSubscribedNcc.CollectionChanged -= Ncc_CollectionChanged;
                mSubscribedNcc = null;
            }
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

            var pinned = (this.OwnerState as Sessions.TrackerState)?.Locations.PinnedLocations;
            if (pinned == null)
                return;

            int idx = 0;
            foreach (Location location in pinned)
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
            // PopulateDefinitionData. OwnerState is stamped at construction
            // time, so the pinned-collection subscription can wire up here.
            base.TryParseInternal(data, package);
            SubscribeToOwnerStatePinned();
            return true;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            // Re-subscribe to the fork's PinnedLocations.
            SubscribeToOwnerStatePinned();
        }
    }
}
