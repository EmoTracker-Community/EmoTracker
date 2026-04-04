using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("container", "grid")]
    public class Container : LayoutItem
    {
        ObservableCollection<LayoutItem> mItems = new ObservableCollection<LayoutItem>();

        public IEnumerable<LayoutItem> Items
        {
            get { return mItems; }
        }

        public override void Dispose()
        {
            DisposeCollection(mItems);
            mItems.Clear();

            base.Dispose();
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mItems.Clear();

            JArray contentAsArray = data.GetValue<JArray>("content");
            if (contentAsArray != null)
            {
                foreach (JObject dataObject in contentAsArray)
                {
                    LayoutItem item = CreateLayoutItem(dataObject, package);
                    if (item != null)
                        mItems.Add(item);
                }
            }


            JObject contentAsObject = data.GetValue<JObject>("content");
            if (contentAsObject != null)
            {
                LayoutItem item = CreateLayoutItem(contentAsObject, package);
                if (item != null)
                    mItems.Add(item);
            }
            
            return true;
        }
    }
}
