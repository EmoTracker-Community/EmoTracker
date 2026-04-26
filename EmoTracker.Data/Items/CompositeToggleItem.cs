using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("composite_toggle")]
    public partial class CompositeToggleItem : ItemBase
    {
        // Cross-item references represented by identity (Phase 2.5). Each
        // ModelReference holds the target's DefinitionId and a per-instance
        // cache slot; resolution flows through this holder's
        // GetModelResolver() (which returns the global ambient resolver in
        // Phase 2.5; per-state resolvers will plug in later). On Fork, OnForked
        // copies the references via ForFork(this) so the new fork has its own
        // cache, and resolution chases the fork's resolver — naturally
        // upgrading to per-state behavior when state lifecycle lands.
        ModelReference<ToggleItem> mItemA;
        ModelReference<ToggleItem> mItemB;

        // Definition data: parsed once at pack-load.
        CodeProvider mProvidedCodes = new CodeProvider();
        Dictionary<KeyValuePair<bool, bool>, ImageReference> mIcons = new Dictionary<KeyValuePair<bool, bool>, ImageReference>();

        public CompositeToggleItem()
        {
            mItemA = new ModelReference<ToggleItem>(this);
            mItemB = new ModelReference<ToggleItem>(this);
        }

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

        public override IEnumerable<string> GetAllProvidedCodes() => mProvidedCodes.ProvidedCodes;

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
            var itemA = mItemA.Target;
            if (itemA != null)
                itemA.Active = !itemA.Active;
        }

        public override void OnRightClick()
        {
            var itemB = mItemB.Target;
            if (itemB != null)
                itemB.Active = !itemB.Active;
        }

        protected void UpdateImage()
        {
            var itemA = mItemA.Target;
            var itemB = mItemB.Target;
            bool activeA = itemA != null ? itemA.Active : false;
            bool activeB = itemB != null ? itemB.Active : false;

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

            // Resolve once via the legacy by-code lookup; the resolved instance's
            // DefinitionId becomes the stable identity carried by the ModelReference.
            // Pre-Phase-2.5 behavior on subsequent pack reloads (re-running
            // ParseDataInternal) is preserved: the ref is updated to point at the
            // newly-resolved item.
            // Phase 6 step 11: at parse time OwnerState may not yet be set
            // (parse runs before adoption); fall back to SessionContext or
            // singleton.
            var itemDb = (this.OwnerState as Sessions.TrackerState)?.Items
                ?? Sessions.SessionContext.ActiveState?.Items
#pragma warning disable CS0618
                ?? ItemDatabase.Instance;
#pragma warning restore CS0618
            mItemA.Set(itemDb.FindProvidingItemForCode(data.GetValue<string>("item_left")) as ToggleItem);
            mItemB.Set(itemDb.FindProvidingItemForCode(data.GetValue<string>("item_right")) as ToggleItem);

            SubscribeSiblingChanges();

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

        // Resolves the current targets and (re-)subscribes to their PropertyChanged
        // events. Idempotent — safe to call after ParseDataInternal and from
        // OnForked. We unsubscribe defensively first so repeat calls don't
        // double-subscribe.
        void SubscribeSiblingChanges()
        {
            var itemA = mItemA.Target;
            var itemB = mItemB.Target;
            if (itemA != null)
            {
                itemA.PropertyChanged -= MonitoredItem_PropertyChanged;
                itemA.PropertyChanged += MonitoredItem_PropertyChanged;
            }
            if (itemB != null)
            {
                itemB.PropertyChanged -= MonitoredItem_PropertyChanged;
                itemB.PropertyChanged += MonitoredItem_PropertyChanged;
            }
        }

        private void MonitoredItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateImage();
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (CompositeToggleItem)source;
            // Definition fields shared by reference.
            mProvidedCodes = src.mProvidedCodes;
            mIcons = src.mIcons;

            // Cross-item references carried over by identity. ForFork(this)
            // creates a fresh ModelReference bound to this fork — same
            // DefinitionId, no cache — so resolution flows through this fork's
            // GetModelResolver() on first read. Phase 2 known limitation
            // (per §2.3): the ambient resolver still walks the singleton
            // ItemDatabase, so the ref still resolves to the original state's
            // sibling. The flip to per-state resolution lands when state
            // lifecycle does.
            mItemA = src.mItemA.ForFork(this);
            mItemB = src.mItemB.ForFork(this);

            SubscribeSiblingChanges();
        }
    }
}
