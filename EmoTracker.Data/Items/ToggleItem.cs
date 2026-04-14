using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("toggle")]
    public class ToggleItem : ItemBase, IConsumingItem
    {
        ImageReference mInactiveIcon;
        ImageReference mActiveIcon;
        bool mbLoop = false;
        string mConsumedCode;
        CodeProvider mCodeProvider = new CodeProvider();

        public bool Loop
        {
            get { return mbLoop; }
            set { SetProperty(ref mbLoop, value); }
        }

        public string ConsumedCode
        {
            get { return mConsumedCode; }
        }

        public ImageReference ActiveIcon
        {
            get { return mActiveIcon; }
            set
            {
                mActiveIcon = value;
                UpdateBaseIcon();
            }
        }

        public bool Active
        {
            get { return GetTransactableProperty<bool>(); }
            set
            {
                bool bFilteredValue = value;

                if (!string.IsNullOrWhiteSpace(mConsumedCode))
                {
                    ConsumableItem provider = TrackerSession.Current.Items.FindProvidingItemForCode(mConsumedCode) as ConsumableItem;
                    if (provider != null)
                    {
                        if (!Active && value)
                        {
                            if (!provider.Consume())
                                bFilteredValue = false;
                        }
                        else if (Active && !value)
                        {
                            if (!provider.Release())
                                bFilteredValue = true;
                        }
                    }
                }

                SetTransactableProperty(bFilteredValue, (processedValue) =>
                {
                    UpdateBaseIcon();
                });
            }
        }

        private void UpdateBaseIcon()
        {
            PotentialIcon = mActiveIcon;

            if (Active)
                this.Icon = mActiveIcon;
            else
                this.Icon = mInactiveIcon;
        }

        public override void OnLeftClick()
        {
            if (Loop)
                Active = !Active;
            else
                Active = true;
        }

        public override void OnRightClick()
        {
            if (Loop)
                Active = !Active;
            else
                Active = false;
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
            string imgName = data.GetValue<string>("img");
            ActiveIcon = ImageReference.FromPackRelativePath(package, imgName, data.GetValue<string>("img_mods"));

            string disabledImgMods = data.GetValue<string>("disabled_img_spec", DisabledImageFilterSpec);
            disabledImgMods = data.GetValue<string>("disabled_img_mods", disabledImgMods);

            mInactiveIcon = ImageReference.FromPackRelativePath(package, data.GetValue<string>("disabled_img"), disabledImgMods);

            if (mActiveIcon != null && mInactiveIcon == null)
                mInactiveIcon = ImageReference.FromImageReference(mActiveIcon, disabledImgMods);

            Active = data.GetValue<bool>("initial_active_state", Active);

            UpdateBaseIcon();

            AddProvidedCodes(data.GetValue<string>("codes"));
            mConsumedCode = data.GetValue<string>("consume");

            Loop = data.GetValue<bool>("loop", false);
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

        public bool GetPotentialConsumedItem(out string code, out uint count)
        {
            if (!Active && !string.IsNullOrWhiteSpace(ConsumedCode))
            {
                code = ConsumedCode;
                count = 1;
                return true;
            }

            code = null;
            count = 0;
            return false;
        }
    }

}
