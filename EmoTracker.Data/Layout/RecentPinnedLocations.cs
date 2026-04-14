using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("recentpins", "recent_pins")]
    public class RecentPinnedLocations : ArrayPanel
    {
        ObservableCollection<Location> mDisplayLocations = new ObservableCollection<Location>();
        int mNumItems = 0;
        bool mbDisplayCompact = true;

        public RecentPinnedLocations()
        {
            INotifyCollectionChanged ncc = TrackerSession.Current.Locations.PinnedLocations as INotifyCollectionChanged;
            if (ncc != null)
                ncc.CollectionChanged += Ncc_CollectionChanged;
        }

        public override void Dispose()
        {
            INotifyCollectionChanged ncc = TrackerSession.Current.Locations.PinnedLocations as INotifyCollectionChanged;
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
        
        public int NumItems
        {
            get { return mNumItems; }
            set { SetProperty(ref mNumItems, value); RefreshDisplayItems(); }
        }

        public bool CompactDisplay
        {
            get { return mbDisplayCompact; }
            set { SetProperty(ref mbDisplayCompact, value); }
        }

        private void RefreshDisplayItems()
        {
            mDisplayLocations.Clear();

            int idx = 0;
            foreach (Location location in TrackerSession.Current.Locations.PinnedLocations)
            {
                mDisplayLocations.Add(location);

                if (++idx >= NumItems && NumItems > 0)
                    break;
            }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            TryParsePanelConfiguration(data, package);
            CompactDisplay = data.GetValue<bool>("compact", true);
            NumItems = data.GetValue<int>("num_items", 0);
            return true;
        }
    }
}
