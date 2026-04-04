using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("item")]
    public class Item : LayoutItem
    {
        Data.ITrackableItem mData;

        public Data.ITrackableItem Data
        {
            get { return mData; }
            set { SetProperty(ref mData, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mData = ItemDatabase.Instance.FindProvidingItemForCode(data.GetValue<string>("item"));
            return true;
        }
    }
}
