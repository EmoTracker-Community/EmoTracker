using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("group")]
    public class GroupBox : Container
    {
        string mHeader;

        public string Header
        {
            get { return mHeader; }
            set { SetProperty(ref mHeader, value); }
        }

        string mHeaderBackground;
        public string HeaderBackground
        {
            get { return mHeaderBackground; }
            set { SetProperty(ref mHeaderBackground, value); }
        }

        public LayoutItem mHeaderContent;
        public LayoutItem HeaderContent
        {
            get { return mHeaderContent; }
            set { SetProperty(ref mHeaderContent, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            if (base.TryParseInternal(data, package))
            {
                Header = data.GetValue<string>("header", null);
                HeaderBackground = data.GetValue<string>("header_background", "#212121");

                JObject headerContentAsObject = data.GetValue<JObject>("header_content");
                if (headerContentAsObject != null)
                    HeaderContent = CreateLayoutItem(headerContentAsObject, package);

                if (!OverrideBackground)
                    Background = "#66212121";

                return true;
            }

            return false;
        }
    }
}
