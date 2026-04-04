using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("dock")]
    public class DockPanel : LayoutItem
    {
        ObservableCollection<LayoutItem> mChildren = new ObservableCollection<LayoutItem>();

        public IEnumerable<LayoutItem> Children
        {
            get { return mChildren; }
        }

        public override void Dispose()
        {
            DisposeCollection(mChildren);
            mChildren.Clear();
            base.Dispose();
        }


        protected bool TryParsePanelConfiguration(JObject data)
        {
            return true;
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            TryParsePanelConfiguration(data);

            mChildren.Clear();
            ParseLayoutItemList(data.GetValue<JArray>("content"), mChildren, package);

            return true;
        }
    }
}
