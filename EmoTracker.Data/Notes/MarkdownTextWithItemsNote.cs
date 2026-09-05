using System.Collections.Generic;
using System.Collections.ObjectModel;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Sessions;

namespace EmoTracker.Data.Notes
{
    public class MarkdownTextWithItemsNote : MarkdownTextNote, IItemCollection
    {
        ObservableCollection<ITrackableItem> mItems = new ObservableCollection<ITrackableItem>();

        internal NoteTakingSite Site { get; set; }

        public string ItemCaptureLayout
        {
            get
            {
                if (Site?.Owner is Location loc)
                    return loc.ItemCaptureLayout;

                var state = Site?.OwnerState as TrackerState;
                var packDefault = state?.DefaultItemCaptureLayout;
                if (!string.IsNullOrWhiteSpace(packDefault)) return packDefault;

                return "tracker_capture_item";
            }
        }

        public IEnumerable<ITrackableItem> Items
        {
            get { return mItems; }
        }

        public bool AddItem(ITrackableItem item)
        {
            if (!mItems.Contains(item))
            {
                mItems.Add(item);
                return true;
            }

            return false;
        }

        public bool RemoveItem(ITrackableItem item)
        {
            if (mItems.Contains(item))
            {
                mItems.Remove(item);
                return true;
            }

            return false;
        }
    }
}
