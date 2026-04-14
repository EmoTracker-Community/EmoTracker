using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Items
{
    public class ItemList : ObservableCollection<ITrackableItem>
    {
    };

    public class ItemGrid : ObservableObject
    {
        ObservableCollection<ItemList> mRows = new ObservableCollection<ItemList>();

        public IEnumerable<ItemList> Rows
        {
            get { return mRows; }
        }

        private string mLegacyMargin;

        public string LegacyMargin
        {
            get { return mLegacyMargin; }
            set { SetProperty(ref mLegacyMargin, value); }
        }

        double mItemWidth = 32;
        public double ItemWidth
        {
            get { return mItemWidth; }
            set { SetProperty(ref mItemWidth, value); }
        }

        double mItemHeight = 32;
        public double ItemHeight
        {
            get { return mItemHeight; }
            set { SetProperty(ref mItemHeight, value); }
        }

        private string mItemMargin;

        public string ItemMargin
        {
            get { return mItemMargin; }
            set { SetProperty(ref mItemMargin, value); }
        }

        private double mBadgeFontSize = 12;
        public double BadgeFontSize
        {
            get { return mBadgeFontSize; }
            set { SetProperty(ref mBadgeFontSize, value); }
        }

        public void AddRow(ItemList row)
        {
            mRows.Add(row);
        }

        public void Clear()
        {
            mRows.Clear();
        }

        public void Load(JObject data)
        {
            ItemMargin = data.GetValue<string>("item_margin", "5");
            ItemWidth = mItemHeight = data.GetValue<double>("item_size", 32.0f);
            ItemWidth = data.GetValue<double>("item_width", ItemWidth);
            ItemHeight = data.GetValue<double>("item_height", ItemHeight);
            BadgeFontSize = data.GetValue<double>("badge_font_size", 12.0);
            LoadRows(data.GetValue<JArray>("rows"));
        }

        void LoadRows(JArray data)
        {
            foreach (JArray rowData in data)
            {
                ItemList row = new ItemList();
                foreach (string code in rowData)
                {
                    row.Add(TrackerSession.Current.Items.FindProvidingItemForCode(code));
                }

                AddRow(row);
            }
        }
    }
}
