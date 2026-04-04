using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("composite_toggle")]
    public class CompositeToggleItem : ItemBase
    {
        ToggleItem mItemA;
        ToggleItem mItemB;

        CodeProvider mProvidedCodes = new CodeProvider();

        Dictionary<KeyValuePair<bool, bool>, ImageReference> mIcons = new Dictionary<KeyValuePair<bool, bool>, ImageReference>();

        public class Stage : CodeProvider
        {
            public ImageReference Icon { get; set; }
        }

        public override bool CanProvideCode(string code)
        {
            if (mProvidedCodes.ProvidesCode(code))
                return true;

            return false;
        }

        public override uint ProvidesCode(string code)
        {
            if (mProvidedCodes.ProvidesCode(code))
                return 1;

            return 0;
        }
        public override void AdvanceToCode(string code = null)
        {
        }

        public override void OnLeftClick()
        {
            if (mItemA != null)
                mItemA.Active = !mItemA.Active;
        }

        public override void OnRightClick()
        {
            if (mItemB != null)
                mItemB.Active = !mItemB.Active;
        }

        protected void UpdateImage()
        {
            bool activeA = mItemA != null ? mItemA.Active : false;
            bool activeB = mItemB != null ? mItemB.Active : false;

            ImageReference icon = null;
            if (mIcons.TryGetValue(new KeyValuePair<bool, bool>(activeA, activeB), out icon))
            {
                Icon = icon;
            }
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            //  CompositeToggle items are never capturable
            Capturable = false;

            mProvidedCodes.AddCodes(data.GetValue<string>("codes"));

            mItemA = ItemDatabase.Instance.FindProvidingItemForCode(data.GetValue<string>("item_left")) as ToggleItem;
            mItemB = ItemDatabase.Instance.FindProvidingItemForCode(data.GetValue<string>("item_right")) as ToggleItem;

            if (mItemA != null)
                mItemA.PropertyChanged += MonitoredItem_PropertyChanged;

            if (mItemB != null)
                mItemB.PropertyChanged += MonitoredItem_PropertyChanged;

            JArray imageMap = (JArray)data.GetValue("images");
            foreach (JObject imageData in imageMap)
            {
                string imgName = imageData.GetValue<string>("img");
                var img = ImageReference.FromPackRelativePath(package, imgName, imageData.GetValue<string>("img_mods"));

                if (img != null)
                {
                    bool bLeftState = imageData.GetValue<bool>("left");
                    bool bRightState = imageData.GetValue<bool>("right");

                    mIcons[new KeyValuePair<bool, bool>(bLeftState, bRightState)] = img;
                }
            }

            UpdateImage();
        }

        private void MonitoredItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateImage();
        }
    }
}
