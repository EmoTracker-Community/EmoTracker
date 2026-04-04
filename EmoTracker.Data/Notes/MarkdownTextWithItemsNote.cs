using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Notes
{
    public class MarkdownTextWithItemsNote : MarkdownTextNote, IItemCollection
    {
        ObservableCollection<ITrackableItem> mItems = new ObservableCollection<ITrackableItem>();

        public string ItemCaptureLayout
        {
            get { return "tracker_capture_item"; }
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
