using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("button_popup")]
    public class ButtonPopup : LayoutItem
    {
        public enum ButtonStyle
        {
            Settings,
            Solid,
            Image
        }

        ButtonStyle mStyle = ButtonStyle.Settings;
        ImageReference mImage;
        Layout mLayout;
        string mPopupBackground = "#FF212121";
        bool mbMaskInput = false;

        public ButtonStyle Style
        {
            get { return mStyle; }
            set { SetProperty(ref mStyle, value); }
        }

        public ImageReference Image
        {
            get { return mImage; }
            set { SetProperty(ref mImage, value); }
        }

        public Layout Layout
        {
            get { return mLayout; }
            set { SetProperty(ref mLayout, value); }
        }

        public string PopupBackground
        {
            get { return mPopupBackground; }
            set { SetProperty(ref mPopupBackground, value); }
        }

        public bool MaskInput
        {
            get { return mbMaskInput; }
            set { SetProperty(ref mbMaskInput, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            Style = data.GetEnumValue<ButtonStyle>("style", ButtonStyle.Settings);
            Image = ImageReference.FromPackRelativePath(package, data.GetValue<string>("image"), data.GetValue<string>("image_filter"));
            Layout = TrackerSession.Current.Layouts.FindLayout(data.GetValue<string>("layout"));
            PopupBackground = data.GetValue<string>("popup_background", "#ff212121");
            MaskInput = data.GetValue<bool>("mask_input", false);
            return true;
        }
    }
}
