using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("canvas")]
    public partial class CanvasPanel : LayoutItem
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

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mChildren.Clear();
            ParseLayoutItemList(data.GetValue<JArray>("content"), mChildren, package);

            return true;
        }

        // -------- Fork ------------------------------------------------------

        public override ModelTypeBase Fork()
        {
            var copy = (CanvasPanel)System.Activator.CreateInstance(this.GetType());
            copy.InitializeAsForkOf(this);
            foreach (var child in this.mChildren)
            {
                var forked = (LayoutItem)child.Fork();
                copy.mChildren.Add(forked);
            }
            return copy;
        }

        public override IEnumerable<LayoutItem> EnumerateChildren()
        {
            foreach (var c in mChildren) yield return c;
        }
    }
}
