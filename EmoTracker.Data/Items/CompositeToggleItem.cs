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
        // Cross-item references resolved through ItemDatabase. Stored as private
        // fields, not in the KV stores — Phase 2 §2.3 known limitation: forks of
        // these cross-referencing items resolve back to the original state's
        // siblings via the singleton ItemDatabase. Multi-state correctness is
        // deferred. OnForked re-resolves below.
        ToggleItem mItemA;
        ToggleItem mItemB;

        // Definition data: parsed once at pack-load.
        CodeProvider mProvidedCodes = new CodeProvider();
        Dictionary<KeyValuePair<bool, bool>, ImageReference> mIcons = new Dictionary<KeyValuePair<bool, bool>, ImageReference>();

        // Used to remember the codes string so OnForked can re-resolve mItemA/mItemB
        // through the (singleton) ItemDatabase: we only store the *codes* used to
        // resolve, not the resolved instances.
        string mItemACode;
        string mItemBCode;

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

            mItemACode = data.GetValue<string>("item_left");
            mItemBCode = data.GetValue<string>("item_right");

            ResolveSiblingItems();

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

        // Resolves mItemA / mItemB through the singleton ItemDatabase using the
        // cached codes, then subscribes to their PropertyChanged events. Used both
        // during initial parse and from OnForked when this instance is being
        // re-bound to (the singleton's view of) the same siblings on a new fork.
        void ResolveSiblingItems()
        {
            if (mItemA != null) mItemA.PropertyChanged -= MonitoredItem_PropertyChanged;
            if (mItemB != null) mItemB.PropertyChanged -= MonitoredItem_PropertyChanged;

            mItemA = ItemDatabase.Instance.FindProvidingItemForCode(mItemACode) as ToggleItem;
            mItemB = ItemDatabase.Instance.FindProvidingItemForCode(mItemBCode) as ToggleItem;

            if (mItemA != null) mItemA.PropertyChanged += MonitoredItem_PropertyChanged;
            if (mItemB != null) mItemB.PropertyChanged += MonitoredItem_PropertyChanged;
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
            mItemACode = src.mItemACode;
            mItemBCode = src.mItemBCode;

            // Cross-item references re-resolved against the (singleton)
            // ItemDatabase. Phase 2 known limitation: this resolves to the
            // original state's siblings. Multi-state correctness is deferred.
            ResolveSiblingItems();
        }
    }
}
