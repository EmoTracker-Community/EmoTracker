using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("dock")]
    public partial class DockPanel : LayoutItem
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

        // -------- Fork ------------------------------------------------------

        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
            var copy = (DockPanel)System.Activator.CreateInstance(this.GetType());
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            foreach (var child in this.mChildren)
            {
                var forked = (LayoutItem)child.Fork(destOwnerState);
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
