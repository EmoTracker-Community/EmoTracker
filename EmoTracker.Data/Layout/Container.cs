using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4: <see cref="Container"/> owns a per-state collection of child
    /// <see cref="LayoutItem"/> instances. The collection itself is a private
    /// field — owned subtree, not a cross-reference — and <see cref="Fork"/>
    /// walks it explicitly forking each child onto the new instance. Inherited
    /// subclasses (<c>ScrollPanel</c>, <c>ViewBox</c>, <c>GroupBox</c>) reuse
    /// this fork pattern via <c>InitializeAsForkOf</c>'s OnForked dispatch.
    /// </summary>
    [JsonTypeTags("container", "grid")]
    public partial class Container : LayoutItem
    {
        // Owned subtree: live LayoutItem children per state. Not in MutableData
        // (children are owning relationships, not references); forked
        // element-by-element on Fork.
        protected ObservableCollection<LayoutItem> mItems = new ObservableCollection<LayoutItem>();

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

        // -------- Fork ------------------------------------------------------

        /// <summary>
        /// Coordinated fork: allocates a fresh container of the same concrete
        /// type via <see cref="System.Activator"/>, runs the inherited
        /// <see cref="ModelTypeBase.InitializeAsForkOf"/>, and walks
        /// <see cref="mItems"/> forking each child onto the new instance.
        /// Subclasses (ScrollPanel, ViewBox, GroupBox) inherit this fork —
        /// they just need <see cref="OnForked"/> overrides if they hold extra
        /// per-state state. Currently none do beyond what <see cref="Container"/>
        /// itself owns.
        /// </summary>
        public override ModelTypeBase Fork()
        {
            var copy = (Container)System.Activator.CreateInstance(this.GetType());
            copy.InitializeAsForkOf(this);
            foreach (var child in this.mItems)
            {
                var forked = (LayoutItem)child.Fork();
                copy.mItems.Add(forked);
            }
            return copy;
        }

        public override IEnumerable<LayoutItem> EnumerateChildren()
        {
            foreach (var c in mItems) yield return c;
        }
    }
}
