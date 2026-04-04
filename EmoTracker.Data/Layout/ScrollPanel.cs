using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("scroll")]
    public class ScrollPanel : Container
    {
        public enum ScrollBarVisibility
        {
            Disabled = 0,
            Auto = 1,
            Hidden = 2,
            Visible = 3
        }

        ScrollBarVisibility mHorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        ScrollBarVisibility mVerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        public ScrollBarVisibility HorizontalScrollBarVisibility
        {
            get { return mHorizontalScrollBarVisibility; }
            set { SetProperty(ref mHorizontalScrollBarVisibility, value); }
        }

        public ScrollBarVisibility VerticalScrollBarVisibility
        {
            get { return mVerticalScrollBarVisibility; }
            set { SetProperty(ref mVerticalScrollBarVisibility, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            bool bResult = base.TryParseInternal(data, package);

            HorizontalScrollBarVisibility = data.GetEnumValue<ScrollBarVisibility>("horizontal_scrollbar_visibility", ScrollBarVisibility.Auto);
            VerticalScrollBarVisibility = data.GetEnumValue<ScrollBarVisibility>("vertical_scrollbar_visibility", ScrollBarVisibility.Auto);

            return bResult;
        }
    }
}
