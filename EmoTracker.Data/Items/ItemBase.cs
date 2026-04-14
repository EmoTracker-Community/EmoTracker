using EmoTracker.Core;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Data.Items
{
    public abstract class ItemBase : TransactableObject, ITrackableItem
    {
        protected ItemBase()
        {
        }

        // Phase 3 of the TrackerSession refactor: items source their mutable property
        // dictionary from the session-owned ItemStateStore rather than holding it on
        // the instance. This puts every item's runtime state behind a single store
        // that future Fork() can deep-clone, without having to recreate item objects
        // (preserving XAML bindings on the originals).
        //
        // During very early construction (before the first session is built), and as
        // a defensive fallback if no session is current, we fall back to the per-
        // instance dictionary on the base class. In practice the session is always
        // available by the time any pack is loaded.
        protected override System.Collections.Generic.Dictionary<string, object> PropertyStore
        {
            get
            {
                var session = Session.TrackerSession.Current;
                var states = session?.ItemStates;
                if (states != null)
                    return states.StateFor(this);

                return base.PropertyStore;
            }
        }

        public string Name
        {
            get { return mName; }
            set { SetProperty(ref mName, value); }
        }

        public string BadgeText
        {
            get { return mBadgeText; }
            set { SetProperty(ref mBadgeText, value); }
        }

        public string BadgeTextColor
        {
            get { return mBadgeTextColor; }
            set { SetProperty(ref mBadgeTextColor, value); }
        }

        public string DisabledImageFilterSpec
        {
            get
            {
                if (mDisabledImageFilterSpec != null)
                    return mDisabledImageFilterSpec;

                return Tracker.Instance.DisabledImageFilterSpec;
            }
            set { SetProperty(ref mDisabledImageFilterSpec, value); }
        }

        public bool Capturable
        {
            get { return mbCapturable; }
            set { SetProperty(ref mbCapturable, value); }
        }

        public bool MaskInput
        {
            get { return mbMaskInput; }
            set { SetProperty(ref mbMaskInput, value); }
        }

        public bool IgnoreUserInput
        {
            get { return mbIgnoreUserInput; }
            set { SetProperty(ref mbIgnoreUserInput, value); }
        }

        public string[] PhoneticSubstitutes
        {
            get { return mPhoneticSubstitutes; }
            set { SetProperty(ref mPhoneticSubstitutes, value); }
        }

        [DependentProperty("PotentialIcon")]
        public ImageReference Icon
        {
            get { return mCurrentIcon; }
            set
            {
                if (SetProperty(ref mCurrentIcon, value))
                    LocationDatabase.Instance.RefeshAccessibility();
            }
        }

        public ImageReference PotentialIcon
        {
            get
            {
                if (mPotentialIcon != null)
                    return mPotentialIcon;

                return mCurrentIcon;
            }
            set
            {
                mPotentialIcon = value;
                NotifyPropertyChanged();
            }
        }

        public void InvalidateAccessibility()
        {
            LocationDatabase.Instance.RefeshAccessibility();
        }

        public abstract void OnLeftClick();
        public abstract void OnRightClick();

        public abstract uint ProvidesCode(string code);
        public abstract bool CanProvideCode(string code);
        public abstract void AdvanceToCode(string code = null);

        /// <summary>
        /// Returns the set of all codes this item can potentially provide, for indexing purposes.
        /// Returns null if the item's codes are dynamic and cannot be statically enumerated
        /// (e.g. LuaItem with a Lua callback).
        /// </summary>
        public virtual IEnumerable<string> GetAllProvidedCodes() => null;


        #region -- Static Methods ---

        public static ITrackableItem CreateItem(JObject data, IGamePackage package)
        {
            ItemBase instance = JsonTypeTagsAttribute.CreateIntanceForTypeTag<ItemBase>(data.GetValue<string>("type"));

            if (instance != null)
            {
                instance.Name = data.GetValue<string>("name");
                instance.Capturable = data.GetValue<bool>("capturable", true);
                instance.MaskInput = data.GetValue<bool>("mask_input", false);
                instance.IgnoreUserInput = data.GetValue<bool>("ignore_user_input", false);
                instance.DisabledImageFilterSpec = data.GetValue<string>("disabled_image_filter", null);

                var phonetics = data["phonetic_substitutes"] as Newtonsoft.Json.Linq.JArray;
                if (phonetics != null)
                    instance.PhoneticSubstitutes = phonetics.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

                instance.ParseDataInternal(data, package);
            }

            return instance;
        }

        #endregion

        #region --- Serialization ---

        protected abstract void ParseDataInternal(JObject data, IGamePackage package);

        protected virtual bool Save(JObject data)
        {
            return false;
        }

        protected virtual bool Load(JObject data)
        {
            return true;
        }

        bool ITrackableItem.Save(JObject data)
        {
            return Save(data);
        }

        bool ITrackableItem.Load(JObject data)
        {
            return Load(data);
        }

        #endregion

        #region --- Fields ---

        ImageReference mCurrentIcon;
        ImageReference mPotentialIcon;
        string mName;
        string mDisabledImageFilterSpec;
        string mBadgeText;
        string mBadgeTextColor = "WhiteSmoke";
        string[] mPhoneticSubstitutes;
        bool mbCapturable = true;
        bool mbMaskInput = false;
        bool mbIgnoreUserInput = false;

        #endregion
    }
}
