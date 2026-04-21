using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Media;
using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    public class Section : LocationVisualProperties
    {
        private static AccessibilityLevel Min(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a < b) ? a : b;
        }

        private static AccessibilityLevel Max(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a > b) ? a : b;
        }

        private Location mOwner;
        private string mName;
        private string mShortName;
        private string mGateCode;
        private string mItemCaptureLayout;
        private uint mnNumChests = 0;
        private ImageReference mThumbnail;
        private AccessibilityRuleSet mGateAccessibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mGateBypassRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mAccessibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mVisibilityRules = new AccessibilityRuleSet();
        private ITrackableItem mGateItem;
        private AccessibilityLevel mCachedAccessibility = AccessibilityLevel.None;
        private AccessibilityLevel mCachedGateAccessibility = AccessibilityLevel.None;
        private bool mbCachedVisibilty = true;
        private bool mbClearAsGroup = false;
        private bool mbCaptureItem = false;
        private bool mbCaptureBadge = false;
        private double mCaptureBadgeOffsetX = 0;
        private double mCaptureBadgeOffsetY = 0;
        private bool mbClearOnCapture = false;
        private bool mbCapturePersist = false;
        private bool mSuppressCaptureClearing = false;
        private bool mbShowGateItem = true;

        public Section(Location owner)
        {
            VisualParent = owner;
            mOwner = owner;

            PropertyChanging += (sender, e) =>
            {
                if (e.PropertyName == nameof(CapturedItem) || e.PropertyName == nameof(AvailableChestCount))
                    ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.LocationUpdating, this);
            };

            PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(CapturedItem) || e.PropertyName == nameof(AvailableChestCount))
                    ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.LocationUpdated, this);
            };
        }

        public Location Owner
        {
            get { return mOwner; }
        }

        public string Name
        {
            get { return mName; }
            set { SetProperty(ref mName, value); NotifyPropertyChanged("ShortName"); }
        }

        public string ShortName
        {
            get
            {
                if (mShortName != null)
                    return mShortName;

                return Name;
            }
            set { SetProperty(ref mShortName, value); }
        }

        public string ItemCaptureLayout
        {
            get { return mItemCaptureLayout; }
            set { SetProperty(ref mItemCaptureLayout, value); }
        }

        public ImageReference Thumbnail
        {
            get { return mThumbnail; }
            set { mThumbnail = value; NotifyPropertyChanged(); }
        }

        public ITrackableItem HostedItem
        {
            get { return GetTransactableProperty<ITrackableItem>(); }
            private set { SetTransactableProperty(value); }
        }

        [TransactablePropertyReadBehavior(TransactablePropertyReadBehavior.AllowOpenTransactionRead)]
        public ITrackableItem CapturedItem
        {
            get { return GetTransactableProperty<ITrackableItem>(); }
            set
            {
                using (TransactionProcessor.Current.OpenTransaction())
                {
                    if (SetTransactableProperty(value, (processedValue) =>
                    {
                        LocationDatabase.Instance.RefeshAccessibility();
                    }))
                    {
                        if (mbCaptureBadge)
                        {
                            string badgeKey = "capture_" + mName;
                            if (value?.PotentialIcon != null)
                            {
                                Owner.AddBadge(badgeKey, value.PotentialIcon, null, mCaptureBadgeOffsetX, mCaptureBadgeOffsetY);
                            }
                            else
                            {
                                Owner.RemoveBadge(badgeKey);
                            }
                        }

                        if (mbClearOnCapture && value != null)
                        {
                            mSuppressCaptureClearing = true;
                            try
                            {
                                if (HostedItem != null)
                                    HostedItem.AdvanceToCode(HostedItemCode);
                                AvailableChestCount = 0;
                            }
                            finally
                            {
                                mSuppressCaptureClearing = false;
                            }
                        }

                        //  Because relevant section data allows open transaction reads,
                        //  we update the pinned status here to include it in the
                        //  current open transaction.
                        Owner.AutoUnpinIfAppropriate();
                    }
                }
            }
        }

        public string GateItemCode
        {
            get { return mGateCode; }
            set
            {
                if (SetProperty(ref mGateCode, value))
                {
                    GateItem = ItemDatabase.Instance.FindProvidingItemForCode(mGateCode);
                }
            }
        }

        public ITrackableItem GateItem
        {
            get { return mGateItem; }
            private set
            {
                if (SetProperty(ref mGateItem, value))
                    LocationDatabase.Instance.RefeshAccessibility();
            }
        }

        private string mHostedItemCode;

        public string HostedItemCode
        {
            get { return mHostedItemCode; }
            set
            {
                mHostedItemCode = value;
                NotifyPropertyChanged();

                HostedItem = ItemDatabase.Instance.FindProvidingItemForCode(mHostedItemCode);
            }
        }

        public uint ChestCount
        {
            get { return mnNumChests; }
            set { mnNumChests = value; NotifyPropertyChanged(); }
        }

        [TransactablePropertyReadBehavior(TransactablePropertyReadBehavior.AllowOpenTransactionRead)]
        public uint AvailableChestCount
        {
            get { return GetTransactableProperty<uint>(); }
            set
            {
                using (TransactionProcessor.Current.OpenTransaction())
                {
                    if (value == 0 && CapturedItem != null && !mSuppressCaptureClearing && !mbCapturePersist)
                    {
                        CapturedItem.AdvanceToCode();
                        CapturedItem = null;
                    }

                    if (SetTransactableProperty(value, (processedValue) =>
                    {
                        LocationDatabase.Instance.RefeshAccessibility();
                    }))
                    {
                        //  Because relevant section data allows open transaction reads,
                        //  we update the pinned status here to include it in the
                        //  current open transaction.
                        Owner.AutoUnpinIfAppropriate();
                    }
                }
            }
        }

        public bool HasUnclaimedItems
        {
            get
            {
                if (AvailableChestCount > 0)
                    return true;

                if (HostedItem != null && 0 == HostedItem.ProvidesCode(HostedItemCode))
                    return true;

                return false;
            }
        }

        public bool ClearAsGroup
        {
            get { return mbClearAsGroup; }
            set { mbClearAsGroup = value; NotifyPropertyChanged(); }
        }

        public bool CaptureItem
        {
            get { return mbCaptureItem; }
            set { mbCaptureItem = value; NotifyPropertyChanged(); }
        }

        public bool CaptureBadge
        {
            get { return mbCaptureBadge; }
            set { SetProperty(ref mbCaptureBadge, value); }
        }

        public double CaptureBadgeOffsetX
        {
            get { return mCaptureBadgeOffsetX; }
            set { SetProperty(ref mCaptureBadgeOffsetX, value); }
        }

        public double CaptureBadgeOffsetY
        {
            get { return mCaptureBadgeOffsetY; }
            set { SetProperty(ref mCaptureBadgeOffsetY, value); }
        }

        public bool ClearOnCapture
        {
            get { return mbClearOnCapture; }
            set { SetProperty(ref mbClearOnCapture, value); }
        }

        public bool CapturePersist
        {
            get { return mbCapturePersist; }
            set { SetProperty(ref mbCapturePersist, value); }
        }

        public bool ShowGateItem
        {
            get { return mbShowGateItem; }
            set { SetProperty(ref mbShowGateItem, value); }
        }

        public AccessibilityRuleSet GateAccessibilityRules
        {
            get { return mGateAccessibilityRules; }
        }

        public AccessibilityRuleSet GateBypassRules
        {
            get { return mGateBypassRules; }
        }

        public AccessibilityRuleSet AccessibilityRules
        {
            get { return mAccessibilityRules; }
        }

        public AccessibilityRuleSet VisibilityRules
        {
            get { return mVisibilityRules; }
        }

        public AccessibilityLevel AccessibilityLevel
        {
            get { return mCachedAccessibility; }
        }

        public AccessibilityLevel GateAccessibilityLevel
        {
            get { return mCachedGateAccessibility; }
        }

        public bool Visible
        {
            get { return mbCachedVisibilty; }
        }

        public void ComputeGateDependencies(Dictionary<string, uint> aggregateGateRequirements, bool bIsRoot = true)
        {
            if (bIsRoot || AccessibilityLevel == AccessibilityLevel.Unlockable)
            {
                IConsumingItem consumer = GateItem as IConsumingItem;
                {
                    string code;
                    uint count;
                    if (consumer != null && consumer.GetPotentialConsumedItem(out code, out count))
                    {
                        if (aggregateGateRequirements.ContainsKey(code))
                            aggregateGateRequirements[code] = count + aggregateGateRequirements[code];
                        else
                            aggregateGateRequirements[code] = count;
                    }
                }
            }

            foreach (AccessibilityRule rule in GateAccessibilityRules.Rules)
            {
                foreach (AccessibilityRule.CodeRule code in rule.Codes)
                {
                    Section dependentSection = Tracker.Instance.FindObjectForCode(code.mCode) as Section;
                    if (dependentSection != null)
                        dependentSection.ComputeGateDependencies(aggregateGateRequirements, false);
                }
            }

            foreach (AccessibilityRule rule in AccessibilityRules.Rules)
            {
                foreach (AccessibilityRule.CodeRule code in rule.Codes)
                {
                    Section dependentSection = Tracker.Instance.FindObjectForCode(code.mCode) as Section;
                    if (dependentSection != null)
                        dependentSection.ComputeGateDependencies(aggregateGateRequirements, false);
                }
            }
        }

        public void RefreshAccessibility()
        {
            if (GateItem != null)
            {
                mCachedGateAccessibility = Min(mOwner.BaseAccessibilityLevel, GateAccessibilityRules.AccessibilityWithoutModifiers);

                if (GateItem.ProvidesCode(GateItemCode) > 0)
                    mCachedGateAccessibility = AccessibilityLevel.Normal;
            }

            if (CapturedItem != null)
                mCachedAccessibility = Min(mOwner.BaseAccessibilityLevel, AccessibilityRules.AccessibilityWithoutModifiers);
            else
                mCachedAccessibility = Min(mOwner.BaseAccessibilityLevel, AccessibilityRules.Accessibility);

            if (mCachedAccessibility >= AccessibilityLevel.Inspect &&
                GateItem != null &&
                GateItem.ProvidesCode(GateItemCode) == 0)
            {
                Dictionary<string, uint> aggregateGateRequirements = new Dictionary<string, uint>();
                ComputeGateDependencies(aggregateGateRequirements);

                IConsumingItem consumer = GateItem as IConsumingItem;
                string code;
                uint localCount;
                if (consumer != null && consumer.GetPotentialConsumedItem(out code, out localCount))
                {
                    uint count = localCount;

                    if (aggregateGateRequirements.ContainsKey(code))
                        count = aggregateGateRequirements[code];

                    AccessibilityLevel _unused;
                    uint providedCount = ItemDatabase.Instance.ProviderCountForCode(code, out _unused);

#if true
                    AccessibilityLevel bypassLevel = (!GateBypassRules.Empty && providedCount >= (count - localCount)) ? GateBypassRules.AccessibilityWithoutModifiers : AccessibilityLevel.None;
                    AccessibilityLevel gateLevel = (providedCount >= count && GateAccessibilityLevel >= AccessibilityLevel.Unlockable) ? GateAccessibilityLevel : AccessibilityLevel.None;

                    if (bypassLevel <= AccessibilityLevel.SequenceBreak && gateLevel >= AccessibilityLevel.SequenceBreak)
                        mCachedAccessibility = AccessibilityLevel.Unlockable;
                    else
                        mCachedAccessibility = Max(Min(bypassLevel, mCachedAccessibility), gateLevel);
#else
                        if (!GateBypassRules.Empty && GateBypassRules.AccessibilityWithoutInspect >= AccessibilityLevel.SequenceBreak)
                        {
                            mCachedAccessibility = Min(mOwner.BaseAccessibilityLevel, GateBypassRules.AccessibilityWithoutInspect);
                        }
                        else if (providedCount >= count && GateAccessibilityLevel >= AccessibilityLevel.SequenceBreak)
                        {
                            mCachedAccessibility = Min(AccessibilityLevel.Unlockable, mCachedAccessibility);
                        }
                        else
                        {
                            mCachedAccessibility = AccessibilityLevel.None;
                        }
#endif
                }
            }

            mbCachedVisibilty = (VisibilityRules.AccessibilityForVisibility >= AccessibilityLevel.Normal);

            NotifyPropertyChanged("AccessibilityLevel");
            NotifyPropertyChanged("GateAccessibilityLevel");
            NotifyPropertyChanged("Visible");
        }
    }
}
