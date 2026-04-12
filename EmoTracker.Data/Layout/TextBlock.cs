using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("text")]
    public class TextBlock : LayoutItem
    {
        string mText;
        double mFontSize = -1.0;

        public string Text
        {
            get { return mText; }
            set { SetProperty(ref mText, value); }
        }

        /// <summary>
        /// Font size for the text element, in points. A value of -1.0 (the default) means
        /// "not set" — the rendered control will inherit the font size from the visual tree.
        /// </summary>
        public double FontSize
        {
            get { return mFontSize; }
            set { SetProperty(ref mFontSize, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            Text = data.GetValue<string>("text");
            FontSize = data.GetValue<double>("font_size", -1.0);
            return true;
        }
    }
}
