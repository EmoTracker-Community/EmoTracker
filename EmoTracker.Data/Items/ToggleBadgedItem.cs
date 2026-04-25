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

        // Cross-item reference (Phase 2.5). ITrackableItem is an interface; the
        // concrete implementation is always an ItemBase (which is ModelTypeBase-
        // derived, so DefinitionId extraction works).
        ModelReference<ITrackableItem> mBaseItemRef;

        // Tracks the previous resolved target for PropertyChanged unwiring on
        // setter changes. ModelReference itself doesn't expose a "what was the
        // old target" hook, so the holder maintains it explicitly.
        ITrackableItem mSubscribedBaseItem;

        public ToggleBadgedItem()
        {
            mBaseItemRef = new ModelReference<ITrackableItem>(this);
        }

        // Hand-written: setter manages the PropertyChanged subscription on the
        // referenced base item, then triggers the icon refresh.
        public ITrackableItem BaseItem
        {
            get { return mBaseItemRef.Target; }
            set
            {
                var current = mBaseItemRef.Target;
                if (ReferenceEquals(current, value)) return;
                NotifyPropertyChanging();
                if (mSubscribedBaseItem != null)
                {
                    mSubscribedBaseItem.PropertyChanged -= BaseItem_PropertyChanged;
                    mSubscribedBaseItem = null;
                }
                mBaseItemRef.Set(value);
                if (value != null)
                {
                    value.PropertyChanged += BaseItem_PropertyChanged;
                    mSubscribedBaseItem = value;
                }
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
            var baseItem = BaseItem;
            if (baseItem != null)
            {
                PotentialIcon = LayeredImageReference.FromLayeredImageReferences(baseItem.PotentialIcon, mActiveIcon);
                Icon = LayeredImageReference.FromLayeredImageReferences(baseItem.Icon, Active ? mActiveIcon : mInactiveIcon);
            }
            else
            {
                PotentialIcon = mActiveIcon;
                Icon = Active ? mActiveIcon : mInactiveIcon;
            }
        }

        public override void OnLeftClick()
        {
            var baseItem = BaseItem;
            if (baseItem != null)
                baseItem.OnLeftClick();
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
            BaseItem = ItemDatabase.Instance.FindProvidingItemForCode(data.GetValue<string>("base_item"));

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

            // Carry the reference across by identity. ForFork(this) creates a
            // fresh ModelReference bound to this fork — same DefinitionId,
            // no cache — so resolution flows through this fork's
            // GetModelResolver() on first read.
            mBaseItemRef = src.mBaseItemRef.ForFork(this);

            // Wire up PropertyChanged subscription on the (per-state) resolved
            // base. Done directly rather than via the BaseItem setter because
            // the setter's ReferenceEquals shortcut would early-exit (mBaseItemRef
            // .Target now caches the resolved value, and value == that same
            // instance) and skip the resubscribe.
            mSubscribedBaseItem = null;
            var resolved = mBaseItemRef.Target;
            if (resolved != null)
            {
                resolved.PropertyChanged += BaseItem_PropertyChanged;
                mSubscribedBaseItem = resolved;
            }
        }
    }
}
