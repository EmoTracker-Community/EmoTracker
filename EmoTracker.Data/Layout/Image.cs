using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("image")]
    public class Image : LayoutItem
    {
        ImageReference mContent;

        public ImageReference Content
        {
            get { return mContent; }
            set { SetProperty(ref mContent, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            Content = ImageReference.FromPackRelativePath(package, data.GetValue<string>("image"), data.GetValue<string>("image_filter"));
            return true;
        }
    }
}
