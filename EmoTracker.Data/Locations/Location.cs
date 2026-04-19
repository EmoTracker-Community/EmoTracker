using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Media;
using EmoTracker.Data.Notes;
using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmoTracker.Data.Locations
{
    public class Location : LocationVisualProperties
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
               
        public Section FindSection(string name)
        {
            foreach (Section s in Sections)
            {
                if (name.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            return null;
        }

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

#region --- Fields ---

        ObservableCollection<Location> mChildren = new ObservableCollection<Location>();
        ObservableCollection<Section> mSections = new ObservableCollection<Section>();
        ObservableDictionary<string, BadgeEntry> mBadges = new ObservableDictionary<string, BadgeEntry>();
        ObservableCollection<BadgeEntry> mBadgeItems = new ObservableCollection<BadgeEntry>();
        AccessibilityRuleSet mAccessibility = new AccessibilityRuleSet();
        AccessibilityLevel mCachedAccessibility = AccessibilityLevel.None;
        AccessibilityLevel mCachedBaseAccessibility = AccessibilityLevel.None;
        Location mParent;
        Group mGroup;
        ImageReference mThumbnail;
        string mName;
        string mShortName;
        string mColor;
        bool mModifiedByUser = false;

#endregion

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

        public bool ModifiedByUser
        {
            get { return mModifiedByUser; }
            set { SetProperty(ref mModifiedByUser, value); }
        }

        public AccessibilityRuleSet AccessibilityRules
        {
            get { return mAccessibility; }
        }

        public AccessibilityLevel BaseAccessibilityLevel
        {
            get { return mCachedBaseAccessibility; }
        }

        public AccessibilityLevel AccessibilityLevel
        {
            get { return mCachedAccessibility; }
        }

        public bool HasLocalItems
        {
            get { return mSections.Count > 0; }
        }

        private bool mbHasAvailableItems = false;

        public bool HasAvailableItems
        {
            get { return mbHasAvailableItems; }
            set { mbHasAvailableItems = value; NotifyPropertyChanged(); }
        }

        bool mbHasVisibleSections = false;
        public bool HasVisibleSections
        {
            get { return mbHasVisibleSections; }
            set { SetProperty(ref mbHasVisibleSections, value); }
        }

        public Location Parent
        {
            get { return mParent; }
            set
            {
                if (SetProperty(ref mParent, value))
                {
                    VisualParent = mParent;
                }
            }
        }
        public Group Group
        {
            get
            {
                if (mGroup != null)
                    return mGroup;

                if (Parent != null)
                    return Parent.Group;

                return null;
            }
            set { mGroup = value; NotifyPropertyChanged(); }
        }

        public string Color
        {
            get
            {
                if (mColor != null)
                    return mColor;

                if (mParent != null)
                    return mParent.Color;

                return null;
            }

            set { SetProperty(ref mColor, value); }
        }

        public IEnumerable<Location> Children
        {
            get { return mChildren; }
        }

        public IEnumerable<Section> Sections
        {
            get { return mSections; }
        }

        public ImageReference Thumbnail
        {
            get { return mThumbnail; }
            set { mThumbnail = value; NotifyPropertyChanged(); }
        }

        [TransactablePropertyReadBehavior(TransactablePropertyReadBehavior.AllowOpenTransactionRead)]
        public bool Pinned
        {
            get { return GetTransactableProperty<bool>(); }
            set
            {
                ForceSetTransactableProperty(value, (processedValue) =>
                {
                    if (processedValue)
                        LocationDatabase.Instance.PinLocation(this);
                    else
                        LocationDatabase.Instance.UnpinLocation(this);
                });
            }
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

            mCachedBaseAccessibility = AccessibilityRules.Accessibility;
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

                    //  Update our notion of section visibility
                    if (section.Visible)
                        HasVisibleSections = true;
                    else
                        continue;   // Invisible sections do not count towards accessibility

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

                    foreach (var entry in aggregateGateRequirements)
                    {
                        AccessibilityLevel _unused;
                        if (ItemDatabase.Instance.ProviderCountForCode(entry.Key, out _unused) < entry.Value)
                        {
                            mCachedAccessibility = AccessibilityLevel.Partial;
                            break;
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
                        LocationDatabase.Instance.LastClearedLocation = this;
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

        #region -- Section References --

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

        #endregion

        #region -- INoteTaking --

        NoteTakingSite mNoteTakingSite = new NoteTakingSite();

        public NoteTakingSite NoteTakingSite
        {
            get { return mNoteTakingSite; }
        }

        #endregion

        #region -- Badges --

        public ObservableDictionary<string, BadgeEntry> Badges
        {
            get { return mBadges; }
        }

        public ObservableCollection<BadgeEntry> BadgeItems
        {
            get { return mBadgeItems; }
        }

        public bool HasBadges
        {
            get { return mBadges.Count > 0; }
        }

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

        #endregion

        public Location()
        {
            mBadges.CollectionChanged += Badges_CollectionChanged;
        }
    }
}
