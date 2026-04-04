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

        public string Text
        {
            get { return mText; }
            set { SetProperty(ref mText, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            Text = data.GetValue<string>("text");
            return true;
        }
    }
}
