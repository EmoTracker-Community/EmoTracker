using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Items
{
    [JsonTypeTags("consumable")]
    public partial class ConsumableItem : ItemBase
    {
        // Definition data: parsed once at pack-load. See note on ToggleItem for
        // why these are private fields rather than ImmutableData entries.
        ImageReference mEmptyIcon;
        ImageReference mFullIcon;
        CodeProvider mCodeProvider = new CodeProvider();

        public ConsumableItem()
        {
            // Seed defaults for the runtime-mutable bound properties so the
            // generator-emitted getters return the historical values.
            MutableData.SetValue(nameof(MaxCount), int.MaxValue);
            MutableData.SetValue(nameof(MinCount), 0);
            MutableData.SetValue(nameof(CountIncrement), 1);
        }

        [KVMutable]
        public partial bool SwapActions { get; set; }

        [KVMutable]
        [OnChanged(nameof(UpdateBadgeAndIcon))]
        public partial bool DisplayAsFractionOfMax { get; set; }

        // Hand-written: clamps the input against the dynamic bounds set by
        // MinCount / MaxCount / ConsumedCount, then routes the result through the
        // transactable accessors (so the clamp happens before the undo entry is
        // captured rather than after). Cannot be expressed via [KVTransactable]
        // alone because the generated setter has no clamping hook.
        [DependentProperty("AvailableCount")]
        public int AcquiredCount
        {
            get { return GetTransactableProperty<int>(); }
            set
            {
                int filteredValue = Math.Min(Math.Max(Math.Max(value, ConsumedCount), MinCount), MaxCount);
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
            get { return GetTransactableProperty<int>(); }
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

        [KVMutable]
        public partial int CountIncrement { get; set; }

        // Hand-written: changing MinCount / MaxCount must re-clamp the existing
        // AcquiredCount / ConsumedCount values, which the generator can't do on
        // its own. The "AcquiredCount = AcquiredCount" trick re-runs the
        // hand-written clamp logic above.
        public int MinCount
        {
            get { return MutableData.GetValue<int>(nameof(MinCount), 0); }
            set
            {
                int current = MutableData.GetValue<int>(nameof(MinCount), 0);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(MinCount), value);

                    AcquiredCount = AcquiredCount;
                    ConsumedCount = ConsumedCount;
                    UpdateBadgeAndIcon();

                    NotifyPropertyChanged();
                }
            }
        }

        public int MaxCount
        {
            get { return MutableData.GetValue<int>(nameof(MaxCount), int.MaxValue); }
            set
            {
                int current = MutableData.GetValue<int>(nameof(MaxCount), int.MaxValue);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(MaxCount), value);

                    AcquiredCount = AcquiredCount;
                    ConsumedCount = ConsumedCount;
                    UpdateBadgeAndIcon();

                    NotifyPropertyChanged();
                }
            }
        }

        public override bool CanProvideCode(string code)
        {
            return mCodeProvider.ProvidesCode(code);
        }

        public override IEnumerable<string> GetAllProvidedCodes() => mCodeProvider.ProvidedCodes;

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
            if (!SwapActions)
                Increment();
            else
                Decrement();
        }

        public int Increment(int count = 1)
        {
            int newCount = Math.Min(MaxCount, Math.Max(MinCount, AcquiredCount + (CountIncrement * count)));
            AcquiredCount = newCount;
            return newCount;
        }

        public int Decrement(int count = 1)
        {
            int newCount = Math.Min(MaxCount, Math.Max(MinCount, AcquiredCount - (CountIncrement * count)));
            AcquiredCount = newCount;
            return newCount;
        }

        public override void OnRightClick()
        {
            if (!SwapActions)
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

            if (AcquiredCount >= MaxCount)
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

            SwapActions = data.GetValue<bool>("swap_actions", false);
            DisplayAsFractionOfMax = data.GetValue<bool>("display_as_fraction_of_max", false);

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

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (ConsumableItem)source;
            mFullIcon = src.mFullIcon;
            mEmptyIcon = src.mEmptyIcon;
            mCodeProvider = src.mCodeProvider;
        }
    }
}
