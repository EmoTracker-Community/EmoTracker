using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("text")]
    public partial class TextBlock : LayoutItem
    {
        [KVOverridable]
        public partial string Text { get; set; }

        /// <summary>
        /// Font size for the text element, in points. A value of -1.0 (the default) means
        /// "not set" — the rendered control will inherit the font size from the visual tree.
        /// </summary>
        [KVOverridable]
        public partial double FontSize { get; set; }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            definition[nameof(Text) + "__def"] = data.GetValue<string>("text");
            definition[nameof(FontSize) + "__def"] = data.GetValue<double>("font_size", -1.0);
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            return true;
        }
    }
}
