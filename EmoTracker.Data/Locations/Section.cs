using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Media;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    public partial class Section : LocationVisualProperties
    {
        private static AccessibilityLevel Min(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a < b) ? a : b;
        }

        private static AccessibilityLevel Max(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a > b) ? a : b;
        }

        // Definition data: rule sets parsed once and never re-assigned at runtime.
        // Held as private fields per the same exemption used elsewhere in the data
        // model (reference-typed, don't fit IDeepCopyable cleanly). Forks share
        // by reference via OnForked.
        private AccessibilityRuleSet mGateAccessibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mGateBypassRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mAccessibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mVisibilityRules = new AccessibilityRuleSet();

        // Cross-references via the Phase 2.5 framework.
        private ModelReference<Location> mOwnerRef;
        private ModelReference<ITrackableItem> mHostedItemRef;
        private ModelReference<ITrackableItem> mGateItemRef;
        private ModelReference<ITrackableItem> mCapturedItemRef;

        // Computed accessibility / visibility cache. Held as fields and refreshed
        // by RefreshAccessibility; per-state state. (Could move into MutableData
        // for clean per-state semantics; that's a follow-up if accessibility
        // values need to differ across forks before the state-lifecycle phase.)
        private AccessibilityLevel mCachedAccessibility = AccessibilityLevel.None;
        private AccessibilityLevel mCachedGateAccessibility = AccessibilityLevel.None;
        private bool mbCachedVisibilty = true;

        // Reentrancy guard for the ClearOnCapture cascade. Lifetime: a single
        // setter invocation. Stays as a transient private field — see Phase 3 §3.1.
        private bool mSuppressCaptureClearing = false;

        // Parameterless ctor — required for Fork's Activator path.
        public Section()
        {
            mOwnerRef = new ModelReference<Location>(this);
            mHostedItemRef = new ModelReference<ITrackableItem>(this);
            mGateItemRef = new ModelReference<ITrackableItem>(this);
            mCapturedItemRef = new ModelReference<ITrackableItem>(this);

            WireScriptManagerCallbacks();
        }

        public Section(EmoTracker.Core.DataModel.ITrackerStateContext state)
        {
            mOwnerRef = new ModelReference<Location>(this);
            mHostedItemRef = new ModelReference<ITrackableItem>(this);
            mGateItemRef = new ModelReference<ITrackableItem>(this);
            mCapturedItemRef = new ModelReference<ITrackableItem>(this);

            WireScriptManagerCallbacks();
            OwnerState = state;
        }

        public Section(Location owner) : this()
        {
            VisualParent = owner;
            mOwnerRef.Set(owner);
        }

        void WireScriptManagerCallbacks()
        {
            // Dispatch through GetScriptManager() so the Lua callback fires
            // against the owning state's interpreter.
            PropertyChanging += (sender, e) =>
            {
                if (e.PropertyName == nameof(CapturedItem) || e.PropertyName == nameof(AvailableChestCount))
                    GetScriptManager()?.InvokeStandardCallback(StandardCallback.LocationUpdating, this);
            };

            PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(CapturedItem) || e.PropertyName == nameof(AvailableChestCount))
                    GetScriptManager()?.InvokeStandardCallback(StandardCallback.LocationUpdated, this);
            };
        }

        public Location Owner
        {
            get { return mOwnerRef.Target; }
        }

        // -------- KVMutable scalar / string properties -----------------------

        [KVMutable]
        public partial string Name { get; set; }

        // ShortName falls back to Name. Hand-written getter; the actual storage
        // is a private [KVMutable] partial so the public surface exposes only
        // the wrapper.
        [KVMutable]
        private partial string ShortNameRaw { get; set; }

        public string ShortName
        {
            get
            {
                var raw = ShortNameRaw;
                if (raw != null) return raw;
                return Name;
            }
            set { ShortNameRaw = value; }
        }

        [KVMutable]
        public partial string ItemCaptureLayout { get; set; }

        [KVMutable]
        public partial ImageReference Thumbnail { get; set; }

        // ChestCount is definition-time today (set once during parse). Pre-Phase-3
        // had no equality check (`mnNumChests = value; NotifyPropertyChanged()`),
        // so we always notify on assignment — preserving that quirk via a
        // hand-written setter rather than [KVMutable]'s on-change-only firing.
        public uint ChestCount
        {
            get { return MutableData.GetValue<uint>(nameof(ChestCount), 0); }
            set
            {
                MutableData.SetValue(nameof(ChestCount), value);
                NotifyPropertyChanged();
            }
        }

        [KVMutable]
        public partial bool ClearAsGroup { get; set; }

        [KVMutable]
        public partial bool CaptureItem { get; set; }

        [KVMutable]
        public partial bool CaptureBadge { get; set; }

        [KVMutable]
        public partial double CaptureBadgeOffsetX { get; set; }

        [KVMutable]
        public partial double CaptureBadgeOffsetY { get; set; }

        [KVMutable]
        public partial bool ClearOnCapture { get; set; }

        [KVMutable]
        public partial bool CapturePersist { get; set; }

        [KVMutable]
        public partial bool ShowGateItem { get; set; }

        // -------- Cross-reference accessors (Guid + ModelReference wrapper) ---

        // The transactable storage slots. The Guid is the undo-tracked value
        // and the ModelReference is a per-instance cache that resolves through
        // this section's GetModelResolver(). [TransactablePropertyReadBehavior
        // .AllowOpenTransactionRead] on CapturedItemId so reads inside an open
        // transaction scope (e.g. the side-effect cascade itself) see the
        // in-flight value, matching the pre-Phase-3 attribute on CapturedItem.

        [KVTransactable]
        [OnChanged(nameof(OnHostedItemIdChanged))]
        private partial Guid HostedItemId { get; set; }

        [KVTransactable]
        [OnChanged(nameof(OnGateItemIdChanged))]
        private partial Guid GateItemId { get; set; }

        [KVTransactable]
        [OnChanged(nameof(OnCapturedItemIdChanged))]
        [TransactablePropertyReadBehavior(TransactablePropertyReadBehavior.AllowOpenTransactionRead)]
        private partial Guid CapturedItemIdStored { get; set; }

        // Resolves the typed item by syncing the local cache to the in-flight Guid.
        ITrackableItem ResolveTyped(ModelReference<ITrackableItem> cache, Guid currentId)
        {
            if (cache.DefinitionId != currentId)
                cache.Set(currentId);
            return cache.Target;
        }

        public ITrackableItem HostedItem
        {
            get { return ResolveTyped(mHostedItemRef, HostedItemId); }
            private set { HostedItemId = (value as ModelTypeBase)?.DefinitionId ?? Guid.Empty; }
        }

        public ITrackableItem GateItem
        {
            get { return ResolveTyped(mGateItemRef, GateItemId); }
            private set { GateItemId = (value as ModelTypeBase)?.DefinitionId ?? Guid.Empty; }
        }

        // CapturedItem is hand-written end-to-end because its setter has a
        // synchronous side-effect cascade (capture-badge management,
        // ClearOnCapture, AutoUnpinIfAppropriate) that must run BEFORE the
        // transaction commits.
        public ITrackableItem CapturedItem
        {
            get
            {
                // Honors AllowOpenTransactionRead via GetTransactableProperty.
                Guid currentId = GetTransactableProperty<Guid>(nameof(CapturedItemIdStored));
                return ResolveTyped(mCapturedItemRef, currentId);
            }
            set
            {
                Guid newId = (value as ModelTypeBase)?.DefinitionId ?? Guid.Empty;
                using (this.OpenTransaction())
                {
                    // PropertyChanging("CapturedItem") fires explicitly for the
                    // ScriptManager LocationUpdating callback (the constructor's
                    // subscription).
                    NotifyPropertyChanging(nameof(CapturedItem));

                    bool queued = SetTransactableProperty<Guid>(newId, _ =>
                    {
                        // Post-commit callback: refresh the cache, fire INPC
                        // for the typed wrapper, refresh accessibility.
                        Guid commitedId = GetTransactableProperty<Guid>(nameof(CapturedItemIdStored));
                        if (mCapturedItemRef.DefinitionId != commitedId)
                            mCapturedItemRef.Set(commitedId);
                        NotifyPropertyChanged(nameof(CapturedItem));
                        // Phase 6 step 11: prefer the owning state's
                        // LocationDatabase; fall back to singleton when
                        // OwnerState hasn't been stamped yet.
                        var locDb = (this.OwnerState as Sessions.TrackerState)?.Locations;
                        locDb?.RefeshAccessibility();
                    }, nameof(CapturedItemIdStored));

                    if (queued)
                    {
                        // Synchronous side effects (run before commit, inside
                        // the open transaction scope so they participate in undo).
                        if (CaptureBadge)
                        {
                            string badgeKey = "capture_" + Name;
                            if (value?.PotentialIcon != null)
                            {
                                Owner?.AddBadge(badgeKey, value.PotentialIcon, null, CaptureBadgeOffsetX, CaptureBadgeOffsetY);
                            }
                            else
                            {
                                Owner?.RemoveBadge(badgeKey);
                            }
                        }

                        if (ClearOnCapture && value != null)
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

                        // Pinned read inside an open transaction is allowed via the
                        // Pinned property's AllowOpenTransactionRead.
                        Owner?.AutoUnpinIfAppropriate();
                    }
                }
            }
        }

        // OnChanged callback for HostedItemId.
        void OnHostedItemIdChanged()
        {
            mHostedItemRef.Set(HostedItemId);
            NotifyPropertyChanged(nameof(HostedItem));
        }

        // OnChanged callback for GateItemId.
        void OnGateItemIdChanged()
        {
            mGateItemRef.Set(GateItemId);
            NotifyPropertyChanged(nameof(GateItem));
            // Phase 6 step 11: prefer the owning state's LocationDatabase.
            var locDb = (this.OwnerState as Sessions.TrackerState)?.Locations;
            locDb?.RefeshAccessibility();
        }

        // OnChanged callback for CapturedItemIdStored. Most of the side-effect
        // cascade runs in the typed CapturedItem setter (synchronous, before
        // commit); this callback handles the post-commit pieces only.
        void OnCapturedItemIdChanged()
        {
            // Side effects already ran in the typed setter; nothing more here.
            // (The typed setter wires its own post-commit callback into
            // SetTransactableProperty, which is the canonical commit hook.)
        }

        // -------- HostedItemCode / GateItemCode (re-resolve on change) -------

        // HostedItemCode setter today always notifies (no equality check). The
        // [KVMutable] generator's on-change-only fire is acceptable — same code
        // resolves to the same item, so no observable difference.
        [KVMutable]
        [OnChanged(nameof(OnHostedItemCodeChanged))]
        public partial string HostedItemCode { get; set; }

        void OnHostedItemCodeChanged()
        {
            // Phase 6 step 11: prefer the owning state's ItemDatabase.
            var itemDb = (this.OwnerState as Sessions.TrackerState)?.Items;
            HostedItem = itemDb?.FindProvidingItemForCode(HostedItemCode);
        }

        [KVMutable]
        [OnChanged(nameof(OnGateItemCodeChanged))]
        public partial string GateItemCode { get; set; }

        void OnGateItemCodeChanged()
        {
            // Phase 6 step 11: prefer the owning state's ItemDatabase.
            var itemDb = (this.OwnerState as Sessions.TrackerState)?.Items;
            GateItem = itemDb?.FindProvidingItemForCode(GateItemCode);
        }

        // -------- AvailableChestCount: transactable + side-effect cascade ----

        [KVTransactable]
        [TransactablePropertyReadBehavior(TransactablePropertyReadBehavior.AllowOpenTransactionRead)]
        private partial uint AvailableChestCountStored { get; set; }

        [TransactablePropertyReadBehavior(TransactablePropertyReadBehavior.AllowOpenTransactionRead)]
        public uint AvailableChestCount
        {
            get { return AvailableChestCountStored; }
            set
            {
                using (this.OpenTransaction())
                {
                    // Drop-captured-on-clear cascade, gated by the same exclusions
                    // as pre-Phase-3 (CaptureBadge / mSuppressCaptureClearing /
                    // CapturePersist).
                    if (value == 0 && CapturedItem != null && !mSuppressCaptureClearing && !CapturePersist)
                    {
                        CapturedItem.AdvanceToCode();
                        CapturedItem = null;
                    }

                    NotifyPropertyChanging(nameof(AvailableChestCount));

                    bool queued = SetTransactableProperty<uint>(value, _ =>
                    {
                        NotifyPropertyChanged(nameof(AvailableChestCount));
                        // Phase 6 step 11: prefer the owning state's
                        // LocationDatabase; fall back to singleton when
                        // OwnerState hasn't been stamped yet.
                        var locDb = (this.OwnerState as Sessions.TrackerState)?.Locations;
                        locDb?.RefeshAccessibility();
                    }, nameof(AvailableChestCountStored));

                    if (queued)
                    {
                        Owner?.AutoUnpinIfAppropriate();
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

        // -------- Definition-data accessors ----------------------------------

        public AccessibilityRuleSet GateAccessibilityRules { get { return mGateAccessibilityRules; } }
        public AccessibilityRuleSet GateBypassRules { get { return mGateBypassRules; } }
        public AccessibilityRuleSet AccessibilityRules { get { return mAccessibilityRules; } }
        public AccessibilityRuleSet VisibilityRules { get { return mVisibilityRules; } }

        // -------- Computed / cached accessibility ---------------------------

        public AccessibilityLevel AccessibilityLevel { get { return mCachedAccessibility; } }
        public AccessibilityLevel GateAccessibilityLevel { get { return mCachedGateAccessibility; } }
        public bool Visible { get { return mbCachedVisibilty; } }

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
                    Section dependentSection = Tracker.Instance.FindObjectForCode(this.OwnerState as Sessions.TrackerState, code.mCode) as Section;
                    if (dependentSection != null)
                        dependentSection.ComputeGateDependencies(aggregateGateRequirements, false);
                }
            }

            foreach (AccessibilityRule rule in AccessibilityRules.Rules)
            {
                foreach (AccessibilityRule.CodeRule code in rule.Codes)
                {
                    Section dependentSection = Tracker.Instance.FindObjectForCode(this.OwnerState as Sessions.TrackerState, code.mCode) as Section;
                    if (dependentSection != null)
                        dependentSection.ComputeGateDependencies(aggregateGateRequirements, false);
                }
            }
        }

        public void RefreshAccessibility()
        {
            var owner = Owner;
            if (owner == null) return;

            // Phase 7.2: rule evaluation context = the section's owning state.
            var state = this.OwnerState as Sessions.TrackerState;

            if (GateItem != null)
            {
                mCachedGateAccessibility = Min(owner.BaseAccessibilityLevel, GateAccessibilityRules.GetAccessibilityWithoutModifiers(state));

                if (GateItem.ProvidesCode(GateItemCode) > 0)
                    mCachedGateAccessibility = AccessibilityLevel.Normal;
            }

            if (CapturedItem != null)
                mCachedAccessibility = Min(owner.BaseAccessibilityLevel, AccessibilityRules.GetAccessibilityWithoutModifiers(state));
            else
                mCachedAccessibility = Min(owner.BaseAccessibilityLevel, AccessibilityRules.GetAccessibility(state));

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

                    AccessibilityLevel _unused = AccessibilityLevel.Normal;
                    // Phase 7.1: prefer the owning state's ItemDatabase. ProviderCountForCode
                    // takes an out arg so we can't `?.` through it cleanly; null-guard manually.
                    var itemDb = state?.Items;
                    uint providedCount = itemDb != null ? itemDb.ProviderCountForCode(code, out _unused) : 0u;

                    AccessibilityLevel bypassLevel = (!GateBypassRules.Empty && providedCount >= (count - localCount)) ? GateBypassRules.GetAccessibilityWithoutModifiers(state) : AccessibilityLevel.None;
                    AccessibilityLevel gateLevel = (providedCount >= count && GateAccessibilityLevel >= AccessibilityLevel.Unlockable) ? GateAccessibilityLevel : AccessibilityLevel.None;

                    if (bypassLevel <= AccessibilityLevel.SequenceBreak && gateLevel >= AccessibilityLevel.SequenceBreak)
                        mCachedAccessibility = AccessibilityLevel.Unlockable;
                    else
                        mCachedAccessibility = Max(Min(bypassLevel, mCachedAccessibility), gateLevel);
                }
            }

            mbCachedVisibilty = (VisibilityRules.GetAccessibilityForVisibility(state) >= AccessibilityLevel.Normal);

            NotifyPropertyChanged("AccessibilityLevel");
            NotifyPropertyChanged("GateAccessibilityLevel");
            NotifyPropertyChanged("Visible");
        }

        // -------- Internal: setting Owner during coordinated fork -------------

        // Called by Location.Fork() on each forked Section to rewire the
        // back-reference. The ModelReference's identity (the parent location's
        // DefinitionId) is preserved, but the cache is updated to the actual
        // newly-constructed parent.
        internal void SetOwner(Location newOwner)
        {
            mOwnerRef.Set(newOwner);
            // VisualParent is also the owning Location.
            VisualParent = newOwner;
        }

        // -------- Fork --------------------------------------------------------

        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
            var copy = new Section();
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            return copy;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (Section)source;

            // Definition rule sets shared by reference.
            mGateAccessibilityRules = src.mGateAccessibilityRules;
            mGateBypassRules = src.mGateBypassRules;
            mAccessibilityRules = src.mAccessibilityRules;
            mVisibilityRules = src.mVisibilityRules;

            // Carry cross-references across by identity. The owning Location
            // will overwrite mOwnerRef via SetOwner() during its coordinated
            // fork; until then, the ref points at the *source's* owner — fine
            // because we've inherited the source's MutableData via COW.
            mOwnerRef = src.mOwnerRef.ForFork(this);
            mHostedItemRef = src.mHostedItemRef.ForFork(this);
            mGateItemRef = src.mGateItemRef.ForFork(this);
            mCapturedItemRef = src.mCapturedItemRef.ForFork(this);

            // Computed accessibility cache: copy as a starting point; will be
            // refreshed by RefreshAccessibility on the parent's next pass.
            mCachedAccessibility = src.mCachedAccessibility;
            mCachedGateAccessibility = src.mCachedGateAccessibility;
            mbCachedVisibilty = src.mbCachedVisibilty;
        }
    }
}
