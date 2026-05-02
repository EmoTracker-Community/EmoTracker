using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("scroll")]
    public partial class ScrollPanel : Container
    {
        public enum ScrollBarVisibility
        {
            Disabled = 0,
            Auto = 1,
            Hidden = 2,
            Visible = 3
        }

        [KVOverridable]
        public partial ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }

        [KVOverridable]
        public partial ScrollBarVisibility VerticalScrollBarVisibility { get; set; }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            base.PopulateDefinitionData(data, package, definition);
            definition[nameof(HorizontalScrollBarVisibility) + "__def"] =
                data.GetEnumValue<ScrollBarVisibility>("horizontal_scrollbar_visibility", ScrollBarVisibility.Auto);
            definition[nameof(VerticalScrollBarVisibility) + "__def"] =
                data.GetEnumValue<ScrollBarVisibility>("vertical_scrollbar_visibility", ScrollBarVisibility.Auto);
        }
    }
}
