using EmoTracker.Core;
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
    public class ArrayPanel : LayoutItem
    {
        ObservableCollection<LayoutItem> mChildren = new ObservableCollection<LayoutItem>();
        Orientation mOrientation = Orientation.Vertical;
        PanelStyle mStyle = PanelStyle.Stack;

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

        public Orientation Orientation
        {
            get { return mOrientation; }
            set { SetProperty(ref mOrientation, value); }
        }

        public PanelStyle Style
        {
            get { return mStyle; }
            set { SetProperty(ref mStyle, value); }
        }

        protected bool TryParsePanelConfiguration(JObject data, IGamePackage package)
        {
            string orientationVal = data.GetValue<string>("orientation");
            if (!string.IsNullOrEmpty(orientationVal) && string.Equals(orientationVal, "horizontal", StringComparison.OrdinalIgnoreCase))
            {
                Orientation = Orientation.Horizontal;
            }
            else
            {
                Orientation = Orientation.Vertical;
            }

            string styleVal = data.GetValue<string>("style");
            if (!string.IsNullOrEmpty(styleVal) && string.Equals(styleVal, "wrap", StringComparison.OrdinalIgnoreCase))
                Style = PanelStyle.Wrap;
            else
                Style = PanelStyle.Stack;

            return true;
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            TryParsePanelConfiguration(data, package);

            mChildren.Clear();
            ParseLayoutItemList(data.GetValue<JArray>("content"), mChildren, package);

            return true;
        }
    }
}
