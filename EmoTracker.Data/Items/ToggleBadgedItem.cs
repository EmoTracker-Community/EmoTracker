using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("toggle_badged")]
    public partial class ToggleBadgedItem : ItemBase
    {
        // Definition data: parsed once at pack-load.
        ImageReference mInactiveIcon;
        ImageReference mActiveIcon;
        CodeProvider mCodeProvider = new CodeProvider();

        // Cross-item reference. Stored as a private field, not in MutableData —
        // see CompositeToggleItem and Phase 2 §2.3 for the rationale.
        ITrackableItem mBaseItem;
        // Cached resolution code for OnForked.
        string mBaseItemCode;

        // Hand-written: setter manages the PropertyChanged subscription on the
        // referenced base item, then triggers the icon refresh. Cannot be
        // generated because the subscription unwiring needs the previous value.
        public ITrackableItem BaseItem
        {
            get { return mBaseItem; }
            set
            {
                if (ReferenceEquals(mBaseItem, value)) return;
                NotifyPropertyChanging();
                if (mBaseItem != null) mBaseItem.PropertyChanged -= BaseItem_PropertyChanged;
                mBaseItem = value;
                if (mBaseItem != null) mBaseItem.PropertyChanged += BaseItem_PropertyChanged;
                NotifyPropertyChanged();
                UpdateDisplayIcon();
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

        // Generator-emitted: per-state, undo-tracked. UpdateDisplayIcon runs on
        // the post-commit callback.
        [KVTransactable]
        [OnChanged(nameof(UpdateDisplayIcon))]
        public partial bool Active { get; set; }

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
            mBaseItemCode = data.GetValue<string>("base_item");
            BaseItem = ItemDatabase.Instance.FindProvidingItemForCode(mBaseItemCode);

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

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (ToggleBadgedItem)source;
            mInactiveIcon = src.mInactiveIcon;
            mActiveIcon = src.mActiveIcon;
            mCodeProvider = src.mCodeProvider;
            mBaseItemCode = src.mBaseItemCode;

            // Re-resolve the base item via the singleton ItemDatabase. Phase 2
            // known limitation: resolves to the original state's instance. The
            // setter handles re-subscribing PropertyChanged.
            BaseItem = ItemDatabase.Instance.FindProvidingItemForCode(mBaseItemCode);
        }
    }
}
