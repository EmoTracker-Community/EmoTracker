using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Locations
{
    public partial class MapLocation : ModelTypeBase
    {
        // Definition data: parsed once, never changes at runtime. Held as private
        // fields per the same exemption used elsewhere in the data model: rule
        // sets are reference-typed and don't fit IDeepCopyable cleanly. Forks
        // share by reference via OnForked.
        private AccessibilityRuleSet mRestrictVisibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mForceVisibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mForceInvisibilityRules = new AccessibilityRuleSet();

        // Cross-reference: Phase 2.5 framework. Stored as a private field, not in
        // MutableData. ForFork(this) on OnForked rebinds; the cached resolved
        // Location's PropertyChanged subscription is rewired explicitly so
        // visibility-dirty propagation continues to fire on the fork's own
        // resolved Location.
        ModelReference<Location> mLocationRef;
        Location mSubscribedLocation;

        // Computed-from-other-props: not in MutableData (values would survive a
        // round-trip through the boundary deep-copy, but Thickness is not a
        // recognized trivially-copyable type and storing it would require either
        // IDeepCopyable on Thickness or an extension to the boundary helper).
        // These are always recomputed from Size / BadgeAlignment / etc., so
        // private-field caching is sufficient.
        Thickness mItemMargin = new Thickness(-35, -35, 0, 0);
        Thickness mBadgeMargin = new Thickness(0, 0, 0, 0);
        Thickness mNoteIndicatorMargin = new Thickness(-17.5, -17.5, 0, 0);
        double mNoteIndicatorSize = 35;

        public MapLocation()
        {
            mLocationRef = new ModelReference<Location>(this);
        }

        // -------- KVMutable scalar properties ---------------------------------

        [KVMutable]
        public partial double X { get; set; }

        [KVMutable]
        public partial double Y { get; set; }

        [KVMutable]
        public partial bool AlwaysVisible { get; set; }

        [KVMutable]
        public partial bool OverrideBadgeSize { get; set; }

        [KVMutable]
        public partial bool EnableBadgeHitTest { get; set; }

        // -------- Hand-written cascading setters ------------------------------

        // Size cascades into ItemMargin, NoteIndicator{Size,Margin}, BadgeSize
        // (if not OverrideBadgeSize), then BadgeMargin via UpdateBadgeMargin.
        public double Size
        {
            get { return MutableData.GetValue<double>(nameof(Size), 0.0); }
            set
            {
                double current = MutableData.GetValue<double>(nameof(Size), 0.0);
                if (current == value) return;

                NotifyPropertyChanging();
                MutableData.SetValue(nameof(Size), value);

                mItemMargin.Top = mItemMargin.Left = -1 * (value / 2);
                NotifyPropertyChanged("LocationMargin");

                mNoteIndicatorSize = Math.Min(value * 0.75, 50);
                mNoteIndicatorMargin = new Thickness(0, mNoteIndicatorSize * -0.5, mNoteIndicatorSize * -0.5, 0);

                NotifyPropertyChanged("NoteIndicatorSize");
                NotifyPropertyChanged("NoteIndicatorMargin");

                if (!OverrideBadgeSize)
                    BadgeSize = value * 0.75;

                UpdateBadgeMargin();

                NotifyPropertyChanged();
            }
        }

        public double BorderThickness
        {
            get { return MutableData.GetValue<double>(nameof(BorderThickness), 8.0); }
            set
            {
                double current = MutableData.GetValue<double>(nameof(BorderThickness), 8.0);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(BorderThickness), value);
                    NotifyPropertyChanged();
                }
            }
        }

        public double BadgeSize
        {
            get { return MutableData.GetValue<double>(nameof(BadgeSize), 35.0); }
            set
            {
                double current = MutableData.GetValue<double>(nameof(BadgeSize), 35.0);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(BadgeSize), value);
                    UpdateBadgeMargin();
                    NotifyPropertyChanged();
                }
            }
        }

        public EmoTracker.Data.Layout.ContentAlignment BadgeAlignment
        {
            get { return MutableData.GetValue<EmoTracker.Data.Layout.ContentAlignment>(nameof(BadgeAlignment), EmoTracker.Data.Layout.ContentAlignment.BottomRight); }
            set
            {
                var current = MutableData.GetValue<EmoTracker.Data.Layout.ContentAlignment>(nameof(BadgeAlignment), EmoTracker.Data.Layout.ContentAlignment.BottomRight);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(BadgeAlignment), value);
                    UpdateBadgeMargin();
                    NotifyPropertyChanged();
                }
            }
        }

        public double BadgeOffsetX
        {
            get { return MutableData.GetValue<double>(nameof(BadgeOffsetX), 0.0); }
            set
            {
                double current = MutableData.GetValue<double>(nameof(BadgeOffsetX), 0.0);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(BadgeOffsetX), value);
                    UpdateBadgeMargin();
                    NotifyPropertyChanged();
                }
            }
        }

        public double BadgeOffsetY
        {
            get { return MutableData.GetValue<double>(nameof(BadgeOffsetY), 0.0); }
            set
            {
                double current = MutableData.GetValue<double>(nameof(BadgeOffsetY), 0.0);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(BadgeOffsetY), value);
                    UpdateBadgeMargin();
                    NotifyPropertyChanged();
                }
            }
        }

        // -------- Read-only computed/cached values -----------------------------

        public Thickness Margin
        {
            get { return mItemMargin; }
        }

        public Thickness BadgeMargin
        {
            get { return mBadgeMargin; }
        }

        public double NoteIndicatorSize
        {
            get { return mNoteIndicatorSize; }
        }

        public Thickness NoteIndicatorMargin
        {
            get { return mNoteIndicatorMargin; }
        }

        // -------- Definition-data (rule-set) accessors -----------------------

        public AccessibilityRuleSet RestrictVisibilityRules
        {
            get { return mRestrictVisibilityRules; }
        }

        public AccessibilityRuleSet ForceVisibilityRules
        {
            get { return mForceVisibilityRules; }
        }

        public AccessibilityRuleSet ForceInvisibilityRules
        {
            get { return mForceInvisibilityRules; }
        }

        public bool ForceVisible
        {
            get
            {
                if (AlwaysVisible)
                    return true;

                if (!ForceVisibilityRules.Empty && ForceVisibilityRules.AccessibilityForVisibility == AccessibilityLevel.Normal)
                    return true;

                return false;
            }
        }

        public bool ForceInvisible
        {
            get
            {
                if (AlwaysVisible)
                    return false;

                if (!ForceInvisibilityRules.Empty && ForceInvisibilityRules.AccessibilityForVisibility == AccessibilityLevel.Normal)
                    return true;

                if (!RestrictVisibilityRules.Empty && RestrictVisibilityRules.AccessibilityForVisibility != AccessibilityLevel.Normal)
                    return true;

                return false;
            }
        }

        // -------- Location cross-reference -------------------------------------

        public Location Location
        {
            get { return mLocationRef.Target; }
            set
            {
                var current = mLocationRef.Target;
                if (ReferenceEquals(current, value)) return;
                NotifyPropertyChanging();
                if (mSubscribedLocation != null)
                {
                    mSubscribedLocation.PropertyChanged -= MLocation_PropertyChanged;
                    mSubscribedLocation = null;
                }
                mLocationRef.Set(value);
                if (value != null)
                {
                    value.PropertyChanged += MLocation_PropertyChanged;
                    mSubscribedLocation = value;
                }
                NotifyPropertyChanged();
            }
        }

        public override void Dispose()
        {
            if (mSubscribedLocation != null)
            {
                mSubscribedLocation.PropertyChanged -= MLocation_PropertyChanged;
                mSubscribedLocation = null;
            }
            mLocationRef?.Clear();

            base.Dispose();
        }

        private void MLocation_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            MarkVisibilityDirty();
        }

        bool mbVisibilityDirty = true;
        internal void MarkVisibilityDirty()
        {
            mbVisibilityDirty = true;
        }

        internal void UpdateVisibilityIfNecessary()
        {
            if (mbVisibilityDirty)
            {
                try
                {
                    NotifyPropertyChanged("ForceVisible");
                    NotifyPropertyChanged("ForceInvisible");
                }
                finally
                {
                    mbVisibilityDirty = false;
                }
            }
        }

        // -------- Margin recomputation ---------------------------------------

        private void UpdateBadgeMargin()
        {
            // The blip square spans from (-mSize/2, -mSize/2) to (mSize/2, mSize/2) in the
            // MinifiedLocation Grid coordinate space (centered at 0,0).
            // Each alignment anchors the badge center on the corresponding point of the blip:
            //   TopLeft     → (-mSize/2, -mSize/2)     Top    → (0, -mSize/2)
            //   TopRight    → ( mSize/2, -mSize/2)     Left   → (-mSize/2, 0)
            //   Center      → (0, 0)                   Right  → ( mSize/2, 0)
            //   BottomLeft  → (-mSize/2,  mSize/2)     Bottom → (0,  mSize/2)
            //   BottomRight → ( mSize/2,  mSize/2)
            // The badge top-left = anchor - badgeSize/2 on each axis.
            double half = BadgeSize / 2.0;
            double sHalf = Size / 2.0;
            double left, top;

            switch (BadgeAlignment)
            {
                case EmoTracker.Data.Layout.ContentAlignment.TopLeft:    left = -sHalf - half; top = -sHalf - half; break;
                case EmoTracker.Data.Layout.ContentAlignment.Top:        left = -half;         top = -sHalf - half; break;
                case EmoTracker.Data.Layout.ContentAlignment.TopRight:   left =  sHalf - half; top = -sHalf - half; break;
                case EmoTracker.Data.Layout.ContentAlignment.Left:       left = -sHalf - half; top = -half;         break;
                case EmoTracker.Data.Layout.ContentAlignment.Right:      left =  sHalf - half; top = -half;         break;
                case EmoTracker.Data.Layout.ContentAlignment.BottomLeft: left = -sHalf - half; top =  sHalf - half; break;
                case EmoTracker.Data.Layout.ContentAlignment.Bottom:     left = -half;         top =  sHalf - half; break;
                case EmoTracker.Data.Layout.ContentAlignment.BottomRight:left =  sHalf - half; top =  sHalf - half; break;
                default: /* Center */                                    left = -half;         top = -half;         break;
            }

            mBadgeMargin = new Thickness(left + BadgeOffsetX, top + BadgeOffsetY, 0, 0);
            NotifyPropertyChanged("BadgeMargin");
        }

        // -------- Fork --------------------------------------------------------

        public override ModelTypeBase Fork()
        {
            var copy = new MapLocation();
            copy.InitializeAsForkOf(this);
            return copy;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (MapLocation)source;
            // Definition rule sets shared by reference.
            mRestrictVisibilityRules = src.mRestrictVisibilityRules;
            mForceVisibilityRules = src.mForceVisibilityRules;
            mForceInvisibilityRules = src.mForceInvisibilityRules;

            // Carry the Location reference across by identity. ForFork(this) gives
            // us a fresh ModelReference bound to this fork — same DefinitionId,
            // empty cache — so resolution flows through this fork's resolver on
            // first read.
            mLocationRef = src.mLocationRef.ForFork(this);

            // Re-subscribe PropertyChanged on the (fork's) resolved Location.
            // Done explicitly rather than via the public setter because the
            // setter's ReferenceEquals shortcut would early-exit (the resolved
            // Target is identical via the singleton resolver) and skip the
            // resubscribe.
            mSubscribedLocation = null;
            var resolved = mLocationRef.Target;
            if (resolved != null)
            {
                resolved.PropertyChanged += MLocation_PropertyChanged;
                mSubscribedLocation = resolved;
            }

            // Recompute derived margins from the fork's inherited Size /
            // BadgeAlignment / etc. (which are inherited via the COW MutableData).
            UpdateBadgeMargin();
            mItemMargin.Top = mItemMargin.Left = -1 * (Size / 2);
            mNoteIndicatorSize = Math.Min(Size * 0.75, 50);
            mNoteIndicatorMargin = new Thickness(0, mNoteIndicatorSize * -0.5, mNoteIndicatorSize * -0.5, 0);
        }
    }

    public partial class Map : ModelTypeBase
    {
        // Owned subtree: live MapLocation instances per state. Held as a private
        // field; on Fork, the override below walks and forks each entry.
        ObservableCollection<MapLocation> mLocations = new ObservableCollection<MapLocation>();

        // Name + DisplayName: hand-written so that Name's setter additionally
        // raises PropertyChanged for "DisplayName" when DisplayName is falling
        // back to Name (matches the pre-Phase-3 behavior).
        public string Name
        {
            get { return MutableData.GetValue<string>(nameof(Name), null); }
            set
            {
                var current = MutableData.GetValue<string>(nameof(Name), null);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(Name), value);
                    NotifyPropertyChanged();
                    var raw = MutableData.GetValue<string>("__DisplayName_Raw", null);
                    if (string.IsNullOrEmpty(raw))
                        NotifyPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DisplayName
        {
            get
            {
                var raw = MutableData.GetValue<string>("__DisplayName_Raw", null);
                if (!string.IsNullOrEmpty(raw)) return raw;
                return Name;
            }
            set
            {
                var current = MutableData.GetValue<string>("__DisplayName_Raw", null);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue("__DisplayName_Raw", value);
                    NotifyPropertyChanged();
                }
            }
        }

        [KVMutable]
        public partial ImageReference Image { get; set; }

        [KVMutable]
        public partial double LocationSize { get; set; }

        [KVMutable]
        public partial double LocationBorderThickness { get; set; }

        public IEnumerable<MapLocation> Locations
        {
            get { return mLocations; }
        }

        public void AddLocation(MapLocation location)
        {
            mLocations.Add(location);
        }

        public override void Dispose()
        {
            Reset();
            base.Dispose();
        }

        public void Reset()
        {
            DisposeCollection(mLocations);
            mLocations.Clear();
        }

        // -------- Fork --------------------------------------------------------

        public override ModelTypeBase Fork()
        {
            var copy = new Map();
            copy.InitializeAsForkOf(this);
            // Walk the owned MapLocation subtree, forking each.
            foreach (var ml in this.mLocations)
            {
                var forked = (MapLocation)ml.Fork();
                copy.mLocations.Add(forked);
            }
            return copy;
        }
    }
}
