using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Media;
using System.Collections.Generic;
using EmoTracker.Data.Session;

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
        // Phase 7: cached accessibility + visibility live in session-local
        // storage (PropertyStore → LocationStateStore) so fork and parent don't
        // clobber each other on shared Section instances.
        private bool mbClearAsGroup = false;
        private bool mbCaptureItem = false;
        private bool mbShowGateItem = true;

        public Section(Location owner)
        {
            VisualParent = owner;
            mOwner = owner;
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
                        TrackerSession.Current.Locations.RefeshAccessibility();
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

        public string GateItemCode
        {
            get { return mGateCode; }
            set
            {
                if (SetProperty(ref mGateCode, value))
                {
                    GateItem = TrackerSession.Current.Items.FindProvidingItemForCode(mGateCode);
                }
            }
        }

        public ITrackableItem GateItem
        {
            get { return mGateItem; }
            private set
            {
                if (SetProperty(ref mGateItem, value))
                    TrackerSession.Current.Locations.RefeshAccessibility();
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

                HostedItem = TrackerSession.Current.Items.FindProvidingItemForCode(mHostedItemCode);
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
                    if (value == 0 && CapturedItem != null)
                    {
                        CapturedItem.AdvanceToCode();
                        CapturedItem = null;
                    }

                    if (SetTransactableProperty(value, (processedValue) =>
                    {
                        TrackerSession.Current.Locations.RefeshAccessibility();
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
            get { return GetSessionLocal<AccessibilityLevel>(); }
        }

        public AccessibilityLevel GateAccessibilityLevel
        {
            get { return GetSessionLocal<AccessibilityLevel>(); }
        }

        public bool Visible
        {
            // Initial session-local default is false; for parity with the
            // previous instance-field default, new Section owners get true on
            // first read if the store has no prior value for Visible (i.e. the
            // dict entry is missing). We special-case here rather than pre-
            // populating the store on construction, because construction
            // happens before the owning session's LocationStates entry for
            // this section exists.
            get
            {
                var store = PropertyStore;
                if (store.TryGetValue("Visible", out var v) && v is bool b)
                    return b;
                return true;
            }
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
                    Section dependentSection = TrackerSession.Current.Tracker.FindObjectForCode(code.mCode) as Section;
                    if (dependentSection != null)
                        dependentSection.ComputeGateDependencies(aggregateGateRequirements, false);
                }
            }

            foreach (AccessibilityRule rule in AccessibilityRules.Rules)
            {
                foreach (AccessibilityRule.CodeRule code in rule.Codes)
                {
                    Section dependentSection = TrackerSession.Current.Tracker.FindObjectForCode(code.mCode) as Section;
                    if (dependentSection != null)
                        dependentSection.ComputeGateDependencies(aggregateGateRequirements, false);
                }
            }
        }

        public void RefreshAccessibility()
        {
            // Phase 7: work in locals, publish to session-local store at the
            // end so fork and parent don't clobber each other on the shared
            // Section instance.
            AccessibilityLevel mCachedGateAccessibility = GetSessionLocal<AccessibilityLevel>("GateAccessibilityLevel");

            if (GateItem != null)
            {
                mCachedGateAccessibility = Min(mOwner.BaseAccessibilityLevel, GateAccessibilityRules.AccessibilityWithoutModifiers);

                if (GateItem.ProvidesCode(GateItemCode) > 0)
                    mCachedGateAccessibility = AccessibilityLevel.Normal;
            }

            AccessibilityLevel mCachedAccessibility;
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
                    uint providedCount = TrackerSession.Current.Items.ProviderCountForCode(code, out _unused);

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

            bool visible = (VisibilityRules.AccessibilityForVisibility >= AccessibilityLevel.Normal);

            // Publish computed values to session-local storage. SetSessionLocal
            // only raises INPC on change, so explicit NotifyPropertyChanged is
            // unnecessary for the three properties below.
            SetSessionLocal(mCachedAccessibility, "AccessibilityLevel");
            SetSessionLocal(mCachedGateAccessibility, "GateAccessibilityLevel");
            SetSessionLocal(visible, "Visible");
        }
    }
}
