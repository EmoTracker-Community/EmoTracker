using System;
using Newtonsoft.Json.Linq;
using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("consumable")]
    public class ConsumableItem : ItemBase
    {
        int mMaxCount = int.MaxValue;
        int mMinCount = 0;
        int mIncrement = 1;

        bool mbSwapActions = false;
        bool mbDisplayAsFractionOfMax = false;

        ImageReference mEmptyIcon;
        ImageReference mFullIcon;

        CodeProvider mCodeProvider = new CodeProvider();

        public bool SwapActions
        {
            get { return mbSwapActions; }
            set { SetProperty(ref mbSwapActions, value); }
        }

        public bool DisplayAsFractionOfMax
        {
            get { return mbDisplayAsFractionOfMax; }
            set
            {
                if (SetProperty(ref mbDisplayAsFractionOfMax, value))
                {
                    UpdateBadgeAndIcon();
                }
            }
        }

        [DependentProperty("AvailableCount")]
        public int AcquiredCount
        {
            get { return GetTransactableProperty<int>(); }
            set
            {
                int filteredValue = Math.Min(Math.Max(Math.Max(value, ConsumedCount), mMinCount), mMaxCount);
                SetTransactableProperty(filteredValue, (processedValue) =>
                {
                    UpdateBadgeAndIcon();
                    LocationDatabase.Instance.RefeshAccessibility();
                });
            }
        }

        [DependentProperty("AvailableCount")]
        public int ConsumedCount
        {
            get { return GetTransactableProperty<int> (); }
            set
            {
                int filteredValue = Math.Max(Math.Min(value, AvailableCount), 0);
                SetTransactableProperty(filteredValue, (processedValue) =>
                {
                    UpdateBadgeAndIcon();
                    LocationDatabase.Instance.RefeshAccessibility();
                });
            }
        }

        public int AvailableCount
        {
            get { return AcquiredCount - ConsumedCount; }
        }

        public int CountIncrement
        {
            get { return mIncrement; }
            set { SetProperty(ref mIncrement, value); }
        }

        public int MinCount
        {
            get { return mMinCount; }
            set
            {
                if (SetProperty(ref mMinCount, value))
                {
                    AcquiredCount = AcquiredCount;
                    ConsumedCount = ConsumedCount;

                    UpdateBadgeAndIcon();
                }
            }
        }

        public int MaxCount
        {
            get { return mMaxCount; }
            set
            {
                if (SetProperty(ref mMaxCount, value))
                {
                    AcquiredCount = AcquiredCount;
                    ConsumedCount = ConsumedCount;

                    UpdateBadgeAndIcon();
                }
            }
        }

        public override bool CanProvideCode(string code)
        {
            return mCodeProvider.ProvidesCode(code);
        }

        public override uint ProvidesCode(string code)
        {
            if (AvailableCount > 0 && mCodeProvider.ProvidesCode(code))
                return (uint)AvailableCount;

            return 0;
        }

        public override void AdvanceToCode(string code = null)
        {
            //  For now, we just simulate a left click so that the swap_actions option is respected "intelligently". This could potentially
            //  cause issues in some packs in the future, so we might want to consider actually making it an option - LeftClick, RightClick, Increment, Decrement
            OnLeftClick();
        }

        public override void OnLeftClick()
        {
            if (!mbSwapActions)
                Increment();
            else
                Decrement();
        }

        public int Increment(int count = 1)
        {
            int newCount = Math.Min(MaxCount, Math.Max(MinCount, AcquiredCount + (mIncrement * count)));
            AcquiredCount = newCount;
            return newCount;
        }

        public int Decrement(int count = 1)
        {
            int newCount = Math.Min(MaxCount, Math.Max(MinCount, AcquiredCount - (mIncrement * count)));
            AcquiredCount = newCount;
            return newCount;
        }

        public override void OnRightClick()
        {
            if (!mbSwapActions)
                Decrement();
            else
                Increment();
        }

        public bool Consume(int quantity = 1)
        {
            if (AvailableCount >= quantity)
            {
                ConsumedCount += quantity;
                return true;
            }

            return false;
        }

        public bool Release(int quantity = 1)
        {
            if (ConsumedCount >= quantity)
            {
                ConsumedCount -= quantity;
                return true;
            }

            return false;
        }

        protected void UpdateBadgeAndIcon()
        {
            if (AvailableCount == 0)
            {
                Icon = AcquiredCount > 0 ? mFullIcon : mEmptyIcon;
                BadgeText = null;
            }
            else
            {
                Icon = mFullIcon;
                if (!DisplayAsFractionOfMax)
                    BadgeText = AvailableCount.ToString();
                else
                    BadgeText = string.Format("{0}/{1}", AvailableCount.ToString(), MaxCount.ToString());
            }

            if (AcquiredCount >= mMaxCount)
                BadgeTextColor = "#00ff00";
            else
                BadgeTextColor = "WhiteSmoke";

        }

        #region Serialization

        protected override void ParseDataInternal(JObject data, IGamePackage package)
        {
            mCodeProvider.Clear();
            mCodeProvider.AddCodes(data.GetValue<string>("codes"));

            mFullIcon = ImageReference.FromPackRelativePath(package, data.GetValue<string>("img"), data.GetValue<string>("img_mods"));

            //  Allow loading a custom disabled image, and then apply filters
            mEmptyIcon = ImageReference.FromPackRelativePath(package, data.GetValue<string>("disabled_img"), data.GetValue<string>("disabled_img_mods") ?? DisabledImageFilterSpec);
            if (mEmptyIcon == null)
                mEmptyIcon = ImageReference.FromImageReference(mFullIcon, data.GetValue<string>("disabled_img_mods") ?? DisabledImageFilterSpec);

            MaxCount = data.GetValue<int>("max_quantity", int.MaxValue);
            MinCount = data.GetValue<int>("min_quantity", 0);
            AcquiredCount = data.GetValue<int>("initial_quantity", 0);
            CountIncrement = data.GetValue<int>("increment", 1);

            mbSwapActions = data.GetValue<bool>("swap_actions", false);
            mbDisplayAsFractionOfMax = data.GetValue<bool>("display_as_fraction_of_max", false);

            UpdateBadgeAndIcon();
        }

        protected override bool Save(JObject data)
        {
            data["acquired_count"] = AcquiredCount;
            data["consumed_count"] = ConsumedCount;
            data["max_count"] = MaxCount;
            data["min_count"] = MinCount;


            return true;
        }

        protected override bool Load(JObject data)
        {
            int acquired = data.GetValue<int>("acquired_count", -1);
            int consumed = data.GetValue<int>("consumed_count", -1);

            if (acquired < 0 || consumed < 0)
                return false;

            AcquiredCount = acquired;
            ConsumedCount = consumed;

            MaxCount = data.GetValue<int>("max_count", MaxCount);
            MinCount = data.GetValue<int>("min_count", MinCount);

            return true;
        }

        #endregion
    }
}
