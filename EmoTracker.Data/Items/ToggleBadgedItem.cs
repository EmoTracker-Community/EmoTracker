using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("toggle_badged")]
    public class ToggleBadgedItem : ItemBase
    {
        ImageReference mInactiveIcon;
        ImageReference mActiveIcon;
        CodeProvider mCodeProvider = new CodeProvider();
        ITrackableItem mBaseItem;

        public ITrackableItem BaseItem
        {
            get { return mBaseItem; }
            set
            {
                if (SetProperty(ref mBaseItem, value))
                {
                    UpdateDisplayIcon();
                }
            }
        }

        public ImageReference ActiveIcon
        {
            get { return mActiveIcon; }
            set
            {
                mActiveIcon = value;
                UpdateDisplayIcon();
            }
        }

        public bool Active
        {
            get { return GetTransactableProperty<bool>(); }
            set
            {
                SetTransactableProperty(value, (processedValue) =>
                {
                    UpdateDisplayIcon();
                });
            }
        }

        private void UpdateDisplayIcon()
        {
            if (BaseItem != null)
            {
                PotentialIcon = LayeredImageReference.FromLayeredImageReferences(BaseItem.PotentialIcon, mActiveIcon);
                Icon = LayeredImageReference.FromLayeredImageReferences(BaseItem.Icon, Active ? mActiveIcon : mInactiveIcon);
            }
            else
            {
                PotentialIcon = mActiveIcon;
                Icon = Active ? mActiveIcon : mInactiveIcon;
            }
        }

        public override void OnLeftClick()
        {
            if (BaseItem != null)
                BaseItem.OnLeftClick();
        }

        public override void OnRightClick()
        {
            Active = !Active;
        }

        public override uint ProvidesCode(string code)
        {
            if (Active && CanProvideCode(code))
                return 1;

            return 0;
        }

        public override bool CanProvideCode(string code)
        {
            return mCodeProvider.ProvidesCode(code);
        }

        public override IEnumerable<string> GetAllProvidedCodes() => mCodeProvider.ProvidedCodes;

        public override void AdvanceToCode(string code = null)
        {
            Active = true;
        }

        public void AddProvidedCodes(string spec)
        {
            mCodeProvider.AddCodes(spec);
        }

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            BaseItem = TrackerSession.Current.Items.FindProvidingItemForCode(data.GetValue<string>("base_item"));
            if (BaseItem != null)
                BaseItem.PropertyChanged += BaseItem_PropertyChanged;

            string disabledImgMods = data.GetValue<string>("disabled_img_spec");
            disabledImgMods = data.GetValue<string>("disabled_img_mods", disabledImgMods);

            mInactiveIcon = ImageReference.FromPackRelativePath(package, data.GetValue<string>("disabled_img"), disabledImgMods ?? DisabledImageFilterSpec);
            ActiveIcon = ImageReference.FromPackRelativePath(package, data.GetValue<string>("img"), data.GetValue<string>("img_mods"));

            Active = data.GetValue<bool>("initial_active_state", Active);
            AddProvidedCodes(data.GetValue<string>("codes"));
        }

        protected override bool Save(JObject data)
        {
            data["active"] = Active;
            return true;
        }

        protected override bool Load(JObject data)
        {
            Active = data.GetValue<bool>("active", Active);
            return true;
        }

        private void BaseItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateDisplayIcon();
        }
    }
}
