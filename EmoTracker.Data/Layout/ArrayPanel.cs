using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Layout
{
    public enum Orientation
    {
        Vertical,
        Horizontal
    }

    public enum PanelStyle
    {
        Stack,
        Wrap
    };

    [JsonTypeTags("array")]
    public partial class ArrayPanel : LayoutItem
    {
        // Owned subtree of children — same pattern as Container.
        protected ObservableCollection<LayoutItem> mChildren = new ObservableCollection<LayoutItem>();

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

        [KVOverridable]
        public partial Orientation Orientation { get; set; }

        [KVOverridable]
        public partial PanelStyle Style { get; set; }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            string orientationVal = data.GetValue<string>("orientation");
            var orientation = (!string.IsNullOrEmpty(orientationVal) && string.Equals(orientationVal, "horizontal", StringComparison.OrdinalIgnoreCase))
                ? Orientation.Horizontal
                : Orientation.Vertical;
            definition[nameof(Orientation) + "__def"] = orientation;

            string styleVal = data.GetValue<string>("style");
            var style = (!string.IsNullOrEmpty(styleVal) && string.Equals(styleVal, "wrap", StringComparison.OrdinalIgnoreCase))
                ? PanelStyle.Wrap
                : PanelStyle.Stack;
            definition[nameof(Style) + "__def"] = style;
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mChildren.Clear();
            ParseLayoutItemList(data.GetValue<JArray>("content"), mChildren, package);

            return true;
        }

        // -------- Fork ------------------------------------------------------

        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
            var copy = (ArrayPanel)System.Activator.CreateInstance(this.GetType());
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            foreach (var child in this.mChildren)
            {
                var forked = (LayoutItem)child.Fork(destOwnerState);
                copy.mChildren.Add(forked);
            }
            return copy;
        }

        public override System.Collections.Generic.IEnumerable<LayoutItem> EnumerateChildren()
        {
            foreach (var c in mChildren) yield return c;
        }
    }
}
