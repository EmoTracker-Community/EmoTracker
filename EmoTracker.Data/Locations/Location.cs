using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Media;
using EmoTracker.Data.Notes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmoTracker.Data.Locations
{
    public partial class Location : LocationVisualProperties
    {
        public const string DefaultBadgeKey = "";

        private static AccessibilityLevel Min(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a < b) ? a : b;
        }

        private static AccessibilityLevel Max(AccessibilityLevel a, AccessibilityLevel b)
        {
            return (a > b) ? a : b;
        }

        // Owned subtrees: live per-state instances.
        ObservableCollection<Location> mChildren = new ObservableCollection<Location>();
        ObservableCollection<Section> mSections = new ObservableCollection<Section>();
        ObservableDictionary<string, BadgeEntry> mBadges = new ObservableDictionary<string, BadgeEntry>();
        ObservableCollection<BadgeEntry> mBadgeItems = new ObservableCollection<BadgeEntry>();

        // Definition data: parsed once at pack-load.
        AccessibilityRuleSet mAccessibility = new AccessibilityRuleSet();

        // Cross-references via Phase 2.5 framework. Parent is set externally
        // (LocationDatabase parser; coordinated fork). Group is partly
        // identity-based (set explicitly) and partly inherited from Parent.
        ModelReference<Location> mParentRef;
        ModelReference<Group> mGroupRef;

        // Computed/cached accessibility values: per-instance state, refreshed
        // by RefreshAccessibility.
        AccessibilityLevel mCachedAccessibility = AccessibilityLevel.None;
        AccessibilityLevel mCachedBaseAccessibility = AccessibilityLevel.None;

        // Instance-local state (not in any KV store).
        NoteTakingSite mNoteTakingSite = new NoteTakingSite();

        public Location()
        {
            mParentRef = new ModelReference<Location>(this);
            mGroupRef = new ModelReference<Group>(this);
            mBadges.CollectionChanged += Badges_CollectionChanged;
        }

        public Location(EmoTracker.Core.DataModel.ITrackerStateContext state)
        {
            mParentRef = new ModelReference<Location>(this);
            mGroupRef = new ModelReference<Group>(this);
            mBadges.CollectionChanged += Badges_CollectionChanged;
            OwnerState = state;
        }

        // -------- KVMutable strings & flags ----------------------------------

        [KVMutable]
        [OnChanged(nameof(OnNameChanged))]
        public partial string Name { get; set; }

        void OnNameChanged()
        {
            // Pre-Phase-3 the setter additionally raised "ShortName" for
            // ShortName's fallback semantics. Keep that.
            NotifyPropertyChanged("ShortName");
        }

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
        public partial bool ModifiedByUser { get; set; }

        // Color: per-location override, falls back to Parent's color.
        [KVMutable]
        private partial string ColorRaw { get; set; }

        public string Color
        {
            get
            {
                var raw = ColorRaw;
                if (raw != null) return raw;

                var parent = Parent;
                if (parent != null) return parent.Color;

                return null;
            }
            set { ColorRaw = value; NotifyPropertyChanged(); }
        }

        [KVMutable]
        public partial ImageReference Thumbnail { get; set; }

        // Per-instance HasAvailableItems / HasVisibleSections — these are
        // updated by RefreshAccessibility, not via direct assignment from the
        // outside, so we keep them as straightforward backing storage.
        [KVMutable]
        public partial bool HasAvailableItems { get; set; }

        [KVMutable]
        public partial bool HasVisibleSections { get; set; }

        // -------- Pinned (transactable) ---------------------------------------

        // Hand-written so we can preserve ForceSetTransactableProperty (always
        // invokes the callback, even on no-op writes — pre-Phase-3 quirk).
        [TransactablePropertyReadBehavior(TransactablePropertyReadBehavior.AllowOpenTransactionRead)]
        public bool Pinned
        {
            get { return GetTransactableProperty<bool>(); }
            set
            {
                ForceSetTransactableProperty(value, (processedValue) =>
                {
                    // Phase 6 step 11: prefer the owning state's LocationDatabase.
                    var locDb = (this.OwnerState as Sessions.TrackerState)?.Locations;
                    if (processedValue)
                        locDb?.PinLocation(this);
                    else
                        locDb?.UnpinLocation(this);
                });
            }
        }

        // -------- Cross-references --------------------------------------------

        public Location Parent
        {
            get { return mParentRef.Target; }
            set
            {
                var current = mParentRef.Target;
                if (ReferenceEquals(current, value)) return;
                NotifyPropertyChanging();
                mParentRef.Set(value);
                VisualParent = value;
                NotifyPropertyChanged();
            }
        }

        public Group Group
        {
            get
            {
                var local = mGroupRef.Target;
                if (local != null) return local;

                var parent = Parent;
                if (parent != null) return parent.Group;

                return null;
            }
            set { mGroupRef.Set(value); NotifyPropertyChanged(); }
        }

        // -------- Owned children / sections collections ----------------------

        public IEnumerable<Location> Children
        {
            get { return mChildren; }
        }

        public IEnumerable<Section> Sections
        {
            get { return mSections; }
        }

        public bool HasLocalItems
        {
            get { return mSections.Count > 0; }
        }

        public Section FindSection(string name)
        {
            foreach (Section s in Sections)
            {
                if (name.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            return null;
        }

        public bool AddChild(Location child)
        {
            if (child != null && !mChildren.Contains(child))
            {
                mChildren.Add(child);
                return true;
            }

            return false;
        }

        public bool AddSection(Section section)
        {
            if (section != null && !mSections.Contains(section))
            {
                mSections.Add(section);
                return true;
            }

            return false;
        }

        // -------- Definition data accessors ----------------------------------

        public AccessibilityRuleSet AccessibilityRules { get { return mAccessibility; } }

        public AccessibilityLevel BaseAccessibilityLevel { get { return mCachedBaseAccessibility; } }
        public AccessibilityLevel AccessibilityLevel { get { return mCachedAccessibility; } }

        public uint AvailableItemCount
        {
            get
            {
                uint count = 0;
                foreach (Section s in Sections)
                {
                    count += s.AvailableChestCount;

                    if (s.HostedItem != null && 0 == s.HostedItem.ProvidesCode(s.HostedItemCode))
                        ++count;
                }

                return count;
            }
        }

        // -------- Domain operations -------------------------------------------

        public void FullClearAllPossible()
        {
            using (TransactionProcessor.Current.OpenTransaction())
            {
                int numClearedSections = 0;
                foreach (Section s in Sections)
                {
                    if (AlwaysAllowChestManipulation || s.AccessibilityLevel >= AccessibilityLevel.Unlockable || ApplicationSettings.Instance.AlwaysAllowClearing)
                    {
                        if (s.Visible)
                        {
                            if (s.HostedItem != null)
                                s.HostedItem.AdvanceToCode(s.HostedItemCode);

                            s.AvailableChestCount = 0;

                            if (s.CapturedItem != null && s.CaptureBadge)
                                s.CapturedItem = null;

                            ++numClearedSections;
                        }
                    }
                }

                if (numClearedSections >= Sections.Count() && AutoUnpinOnClear)
                    Pinned = false;
            }
        }

        public void AutoUnpinIfAppropriate()
        {
            if (!AutoUnpinOnClear)
                return;

            if (!Pinned)
                return;

            foreach (Section section in Sections)
            {
                if (section.Visible && section.HasUnclaimedItems)
                    return;
            }

            Pinned = false;
        }

        public void RefreshAccessibility()
        {
            AccessibilityLevel prevBaseAccessibility = mCachedBaseAccessibility;
            AccessibilityLevel prevAccessibility = mCachedAccessibility;

            // Phase 7.2: thread state into rule evaluation so the per-state cache is consulted.
            var __ruleState = this.OwnerState as Sessions.TrackerState;
            mCachedBaseAccessibility = AccessibilityRules.GetAccessibility(__ruleState);
            if (Parent != null)
                mCachedBaseAccessibility = Min(mCachedBaseAccessibility, Parent.BaseAccessibilityLevel);

            AccessibilityLevel localAccessibility = AccessibilityLevel.None;
            AccessibilityLevel localMinCompletableAccessibility = AccessibilityLevel.Normal;
            HasAvailableItems = false;
            HasVisibleSections = false;

            if (mSections.Count > 0)
            {
                Dictionary<string, uint> aggregateGateRequirements = new Dictionary<string, uint>();
                HashSet<IConsumingItem> processedGateItems = new HashSet<IConsumingItem>();

                int numAccessibleSections = 0;
                int numAccessibleSectionsWithItems = 0;
                int numAccessibleSectionsWithInspectableItems = 0;
                int numSectionsWithItems = 0;
                uint numAccessibleItems = 0;
                uint numInspectableItems = 0;

                foreach (Section section in Sections)
                {
                    section.RefreshAccessibility();

                    if (section.Visible)
                        HasVisibleSections = true;
                    else
                        continue;

                    if (section.HasUnclaimedItems)
                    {
                        ++numSectionsWithItems;

                        if (section.AccessibilityLevel >= AccessibilityLevel.SequenceBreak)
                        {
                            localMinCompletableAccessibility = Min(localMinCompletableAccessibility, section.AccessibilityLevel);
                        }

                        if (section.AccessibilityLevel >= AccessibilityLevel.Unlockable)
                        {
                            localAccessibility = Max(localAccessibility, section.AccessibilityLevel);

                            ++numAccessibleSections;

                            if (section.HasUnclaimedItems)
                            {
                                ++numAccessibleSectionsWithItems;

                                if (section.AccessibilityLevel == AccessibilityLevel.Inspect)
                                {
                                    ++numAccessibleSectionsWithInspectableItems;
                                    numInspectableItems += section.AvailableChestCount;
                                }
                            }

                            numAccessibleItems += section.AvailableChestCount;

                            if (section.HostedItem != null && 0 == section.HostedItem.ProvidesCode(section.HostedItemCode))
                                ++numAccessibleItems;

                            if (section.AccessibilityLevel == AccessibilityLevel.Unlockable)
                            {
                                IConsumingItem consumer = section.GateItem as IConsumingItem;
                                if (consumer != null)
                                {
                                    if (!processedGateItems.Contains(consumer))
                                    {
                                        if (consumer.GetPotentialConsumedItem(out string code, out uint count))
                                        {
                                            if (count > 0 && !string.IsNullOrWhiteSpace(code))
                                            {
                                                if (aggregateGateRequirements.ContainsKey(code))
                                                    aggregateGateRequirements[code] = count + aggregateGateRequirements[code];
                                                else
                                                    aggregateGateRequirements[code] = count;
                                            }
                                        }

                                        processedGateItems.Add(consumer);
                                    }
                                }
                            }
                        }
                    }
                }

                mCachedAccessibility = mCachedBaseAccessibility;

                if (numAccessibleItems > 0)
                {
                    HasAvailableItems = true;
                    if (Group != null)
                        Group.HasAvailableItems = true;

                    if (numAccessibleSectionsWithItems < numSectionsWithItems || (numInspectableItems > 0 && numInspectableItems < numAccessibleItems))
                        mCachedAccessibility = AccessibilityLevel.Partial;
                    else if (localAccessibility >= AccessibilityLevel.SequenceBreak)
                        mCachedAccessibility = localMinCompletableAccessibility;
                    else
                        mCachedAccessibility = localAccessibility;

                    // Phase 6 step 11: prefer the owning state's ItemDatabase.
                    // OwnerState may be null mid-load (locations register before
                    // their state stamp completes during streaming pack loads);
                    // skip the gate-requirement check in that case rather than NRE.
                    var itemDbForGates = (this.OwnerState as Sessions.TrackerState)?.Items;
                    if (itemDbForGates != null)
                    {
                        foreach (var entry in aggregateGateRequirements)
                        {
                            AccessibilityLevel _unused;
                            if (itemDbForGates.ProviderCountForCode(entry.Key, out _unused) < entry.Value)
                            {
                                mCachedAccessibility = AccessibilityLevel.Partial;
                                break;
                            }
                        }
                    }
                }
                else if (numSectionsWithItems > 0)
                {
                    mCachedAccessibility = AccessibilityLevel.None;
                }
                else if (numSectionsWithItems == 0)
                {
                    mCachedAccessibility = AccessibilityLevel.Cleared;

                    if (AutoUnpinOnClear)
                        Pinned = false;

                    if (mCachedBaseAccessibility != prevBaseAccessibility)
                    {
                        // Phase 6 step 11: prefer the owning state's LocationDatabase.
                        var locDbForClear = (this.OwnerState as Sessions.TrackerState)?.Locations;
                        if (locDbForClear != null)
                            locDbForClear.LastClearedLocation = this;
                    }
                }
            }
            else
            {
                mCachedAccessibility = mCachedBaseAccessibility;
            }

            NotifyPropertyChanged("BaseAccessibilityLevel");
            NotifyPropertyChanged("AccessibilityLevel");

            foreach (Location child in Children)
            {
                child.RefreshAccessibility();
            }
        }

        // -------- Section References (persistable) ---------------------------

        public string GetPersistableSectionReference(Section section)
        {
            int idx = mSections.IndexOf(section);

            if (idx < 0)
                throw new InvalidOperationException("Cannot generate persistable reference for section that is not in the parent location");

            if (!string.IsNullOrWhiteSpace(section.Name))
                return string.Format("{0}:{1}", idx, Uri.EscapeDataString(section.Name));
            else
                return string.Format("{0}", idx);
        }

        public Section ResolvePersistableSectionReference(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            string[] tokens = reference.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 1)
                return null;

            int idx = -1;
            if (!int.TryParse(tokens[0], out idx) || idx < 0 || idx > (mSections.Count - 1))
                return null;

            Section section = mSections[idx];

            if (tokens.Length >= 2)
            {
                string name = Uri.UnescapeDataString(tokens[1]);

                if (!string.Equals(name, section.Name, StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            return section;
        }

        // -------- INoteTaking -------------------------------------------------

        public NoteTakingSite NoteTakingSite { get { return mNoteTakingSite; } }

        // -------- Badges ------------------------------------------------------

        public ObservableDictionary<string, BadgeEntry> Badges { get { return mBadges; } }
        public ObservableCollection<BadgeEntry> BadgeItems { get { return mBadgeItems; } }
        public bool HasBadges { get { return mBadges.Count > 0; } }

        public ImageReference AddBadge(string imageRef, string filterSpec = null)
        {
            try
            {
                ImageReference badge = ImageReference.FromPackRelativePath(Tracker.Instance.ActiveGamePackage, imageRef, filterSpec);
                if (badge == null) return null;
                mBadges[DefaultBadgeKey] = new BadgeEntry(DefaultBadgeKey, badge);
                return badge;
            }
            catch
            {
                return null;
            }
        }

        public ImageReference AddBadge(ImageReference imageRef, string filter = null)
        {
            try
            {
                ImageReference badge = ImageReference.FromImageReference(imageRef, filter);
                if (badge == null) return null;
                mBadges[DefaultBadgeKey] = new BadgeEntry(DefaultBadgeKey, badge);
                return badge;
            }
            catch
            {
                return null;
            }
        }

        public ImageReference AddBadge(string key, ImageReference imageRef, string filter = null, double offsetX = 0, double offsetY = 0)
        {
            try
            {
                ImageReference badge = ImageReference.FromImageReference(imageRef, filter);
                if (badge == null) return null;
                mBadges[key] = new BadgeEntry(key, badge, offsetX, offsetY);
                return badge;
            }
            catch
            {
                return null;
            }
        }

        public void RemoveBadge(ImageReference badge)
        {
            try
            {
                if (badge == null) return;
                string keyToRemove = null;
                foreach (var kvp in mBadges)
                {
                    if (kvp.Value.Image == badge || (kvp.Value.Image != null && kvp.Value.Image.Equals(badge)))
                    {
                        keyToRemove = kvp.Key;
                        break;
                    }
                }
                if (keyToRemove != null)
                    mBadges.Remove(keyToRemove);
            }
            catch
            {
            }
        }

        public void RemoveBadge(string key)
        {
            try
            {
                if (key != null)
                    mBadges.Remove(key);
            }
            catch
            {
            }
        }

        public void ClearBadges()
        {
            mBadges.Clear();
        }

        private void SyncBadgeItems()
        {
            mBadgeItems.Clear();
            foreach (var kvp in mBadges)
                mBadgeItems.Add(kvp.Value);
        }

        private void Badges_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SyncBadgeItems();
            NotifyPropertyChanged("HasBadges");
        }

        // -------- Fork --------------------------------------------------------

        public override ModelTypeBase Fork()
        {
            var copy = new Location();
            copy.InitializeAsForkOf(this);

            // Coordinated fork: walk owned subtrees and rewire back-references.
            // Sections first.
            foreach (var section in this.mSections)
            {
                var forkedSection = (Section)section.Fork();
                forkedSection.SetOwner(copy);
                copy.mSections.Add(forkedSection);
            }
            // Children second; rewire each child's Parent to the new fork.
            foreach (var child in this.mChildren)
            {
                var forkedChild = (Location)child.Fork();
                forkedChild.Parent = copy;
                copy.mChildren.Add(forkedChild);
            }

            return copy;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (Location)source;
            mAccessibility = src.mAccessibility;

            // Cross-references rebound to this fork. Parent will be set
            // explicitly by the parent's coordinated fork (via the Parent
            // setter); Group is identity-preserved.
            mParentRef = src.mParentRef.ForFork(this);
            mGroupRef = src.mGroupRef.ForFork(this);

            // Computed cache: copy as a starting point; refresh sweep updates it.
            mCachedAccessibility = src.mCachedAccessibility;
            mCachedBaseAccessibility = src.mCachedBaseAccessibility;
        }
    }
}
