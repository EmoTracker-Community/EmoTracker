using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("itemgrid")]
    public class ItemGrid : LayoutItem
    {
        private static Version mMarginFixVersion = new Version("1.0");

        Data.Items.ItemGrid mItemGrid = new Data.Items.ItemGrid();

        public Data.Items.ItemGrid Data
        {
            get { return mItemGrid; }
            set { SetProperty(ref mItemGrid, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mItemGrid.Clear();  
            mItemGrid.Load(data);

            //  Process legacy margin settings for old packages
            if (package.LayoutEngineVersion == null || package.LayoutEngineVersion < mMarginFixVersion)
                mItemGrid.LegacyMargin = data.GetValue<string>("margin", "5");

            return true;
        }
    }
}
