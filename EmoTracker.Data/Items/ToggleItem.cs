using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("toggle")]
    public partial class ToggleItem : ItemBase, IConsumingItem
    {
        // Definition data: parsed once at pack-load, never re-assigned at runtime.
        // Kept as private fields rather than ImmutableData entries because
        // ImageReference and CodeProvider are reference-typed and the KV-store
        // boundary's IDeepCopyable contract makes them awkward to store there
        // (per Phase 2 §2.6). Forks share these by reference via OnForked.
        ImageReference mInactiveIcon;
        ImageReference mActiveIcon;
        string mConsumedCode;
        CodeProvider mCodeProvider = new CodeProvider();

        // Per-state runtime config: public setter is part of the API surface, so
        // this lives in MutableData even though the value is nominally definition-
        // time (matches the Loop/Capturable rationale in the Phase 2 plan).
        [KVMutable]
        public partial bool Loop { get; set; }

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

        // Hand-written: setter coordinates with the mConsumedCode provider's
        // Consume() / Release() before queueing the transaction, mirroring the
        // pre-Phase-2 behavior verbatim. KVTransactable can't express the
        // "filter the value before writing" pattern, so this stays manual.
        public bool Active
        {
            get { return GetTransactableProperty<bool>(); }
            set
            {
                bool bFilteredValue = value;

                if (!string.IsNullOrWhiteSpace(mConsumedCode))
                {
                    // Phase 6 step 11: prefer the owning state's ItemDatabase.
                    var itemDb = (this.OwnerState as Sessions.TrackerState)?.Items;
                    ConsumableItem provider = itemDb?.FindProvidingItemForCode(mConsumedCode) as ConsumableItem;
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

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (ToggleItem)source;
            // Definition fields are shared by reference across forks. CodeProvider
            // is mutated only at parse time; ImageReference instances are logically
            // immutable (see ImageReference.IDeepCopyable.DeepCopy → this).
            mInactiveIcon = src.mInactiveIcon;
            mActiveIcon = src.mActiveIcon;
            mConsumedCode = src.mConsumedCode;
            mCodeProvider = src.mCodeProvider;
        }
    }

}
