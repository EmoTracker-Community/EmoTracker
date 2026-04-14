using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("layout")]
    public class LayoutReference : LayoutItem
    {
        string mKey;
        Layout mLayout;

        public LayoutReference()
        {
            //  TODO: Track changes to our referenced layout key and re-acquire referenced layout as needed
        }

        public Layout Layout
        {
            get { return mLayout; }
            set { SetProperty(ref mLayout, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mKey = data.GetValue<string>("key");
            Layout = TrackerSession.Current.Layouts.FindLayout(mKey);
            return true;
        }
    }
}
