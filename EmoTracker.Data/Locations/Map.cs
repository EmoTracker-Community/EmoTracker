using EmoTracker.Core;
using EmoTracker.Data.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Locations
{
    public class MapLocation : ObservableObject
    {
        private AccessibilityRuleSet mRestrictVisibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mForceVisibilityRules = new AccessibilityRuleSet();
        private AccessibilityRuleSet mForceInvisibilityRules = new AccessibilityRuleSet();

        private Location mLocation;
        private double mX;
        private double mY;
        private double mSize;

        double mBorderThickness = 8;
        Thickness mItemMargin = new Thickness(-35, -35, 0, 0);

        double mBadgeSize = 35;
        Thickness mBadgeMargin = new Thickness(0, 0, 0, 0);
        BadgeAlignment mBadgeAlignment = BadgeAlignment.BottomRight;
        double mBadgeOffsetX = 0;
        double mBadgeOffsetY = 0;

        double mNoteIndicatorSize = 35;
        Thickness mNoteIndicatorMargin = new Thickness(-17.5, -17.5, 0, 0);

        bool mbAlwaysVisible = false;
        public bool AlwaysVisible
        {
            get { return mbAlwaysVisible; }
            set { SetProperty(ref mbAlwaysVisible, value); }
        }

        bool mbBadgeSizeOverride = false;
        public bool OverrideBadgeSize
        {
            get { return mbBadgeSizeOverride; }
            set { SetProperty(ref mbBadgeSizeOverride, value); }
        }

        private bool mbEnableBadgeHitTest = false;
        public bool EnableBadgeHitTest
        {
            get { return mbEnableBadgeHitTest; }
            set { SetProperty(ref mbEnableBadgeHitTest, value); }
        }


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

        public double X
        {
            get { return mX; }
            set { SetProperty(ref mX, value); }
        }

        public double Y
        {
            get { return mY; }
            set { SetProperty(ref mY, value); }
        }

        public double Size
        {
            get { return mSize; }
            set {

                SetProperty(ref mSize, value);

                mItemMargin.Top = mItemMargin.Left = -1 * (mSize / 2);
                NotifyPropertyChanged("LocationMargin");

                mNoteIndicatorSize = Math.Min(mSize * 0.75, 50);
                mNoteIndicatorMargin = new Thickness(0, mNoteIndicatorSize * -0.5, mNoteIndicatorSize * -0.5, 0);

                NotifyPropertyChanged("NoteIndicatorSize");
                NotifyPropertyChanged("NoteIndicatorMargin");

                if (!OverrideBadgeSize)
                    BadgeSize = mSize * 0.75;

                UpdateBadgeMargin();
            }
        }

        private void UpdateBadgeMargin()
        {
            // The blip is centered at (0,0) in the MinifiedLocation Grid coordinate space.
            // We anchor the chosen point on the badge to that center, then apply any
            // additional pixel offset.  "tl" is the resulting top-left of the badge.
            double half = mBadgeSize / 2.0;
            double left, top;

            switch (mBadgeAlignment)
            {
                // Each case: badge margin so that the badge appears in the named position
                // relative to the blip center (Grid (0,0)).  The badge top-left is placed at
                // the required offset so the badge visually sits in the correct quadrant/edge.
                case BadgeAlignment.TopLeft:    left = -mBadgeSize; top = -mBadgeSize; break;
                case BadgeAlignment.Top:        left = -half;       top = -mBadgeSize; break;
                case BadgeAlignment.TopRight:   left = 0;           top = -mBadgeSize; break;
                case BadgeAlignment.Left:       left = -mBadgeSize; top = -half;       break;
                case BadgeAlignment.Right:      left = 0;           top = -half;       break;
                case BadgeAlignment.BottomLeft: left = -mBadgeSize; top = 0;           break;
                case BadgeAlignment.Bottom:     left = -half;       top = 0;           break;
                case BadgeAlignment.BottomRight:left = 0;           top = 0;           break;
                default: /* Center */           left = -half;       top = -half;       break;
            }

            mBadgeMargin = new Thickness(left + mBadgeOffsetX, top + mBadgeOffsetY, 0, 0);
            NotifyPropertyChanged("BadgeMargin");
        }

        public double BorderThickness
        {
            get { return mBorderThickness; }
            set { SetProperty(ref mBorderThickness, value); }
        }

        public Thickness Margin
        {
            get { return mItemMargin; }
        }

        public double BadgeSize
        {
            get { return mBadgeSize; }
            set
            {
                if (SetProperty(ref mBadgeSize, value))
                {
                    UpdateBadgeMargin();
                    NotifyPropertyChanged("ShowBadge");
                }
            }
        }

        public bool ShowBadge
        {
            get { return mBadgeSize != 0; }
        }

        public BadgeAlignment BadgeAlignment
        {
            get { return mBadgeAlignment; }
            set
            {
                if (SetProperty(ref mBadgeAlignment, value))
                    UpdateBadgeMargin();
            }
        }

        public double BadgeOffsetX
        {
            get { return mBadgeOffsetX; }
            set
            {
                if (SetProperty(ref mBadgeOffsetX, value))
                    UpdateBadgeMargin();
            }
        }

        public double BadgeOffsetY
        {
            get { return mBadgeOffsetY; }
            set
            {
                if (SetProperty(ref mBadgeOffsetY, value))
                    UpdateBadgeMargin();
            }
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

        public Location Location
        {
            get { return mLocation; }
            set
            {
                if (SetProperty(ref mLocation, value) && mLocation != null)
                {
                    mLocation.PropertyChanged += MLocation_PropertyChanged;
                }
            }
        }

        public override void Dispose()
        {
            if (Location != null)
                Location.PropertyChanged -= MLocation_PropertyChanged;

            Location = null;

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
    }

    public class Map : ObservableObject
    {
        string mName;
        string mDisplayName;
        ImageReference mImage;
        ObservableCollection<MapLocation> mLocations = new ObservableCollection<MapLocation>();
       
        public string Name
        {
            get { return mName; }
            set
            {
                SetProperty(ref mName, value);
                if (string.IsNullOrEmpty(mDisplayName))
                    NotifyPropertyChanged("DisplayName");
            }
        }

        public string DisplayName
        {
            get { if (!string.IsNullOrEmpty(mDisplayName)) return mDisplayName; return mName; }
            set { SetProperty(ref mDisplayName, value); }
        }


        public ImageReference Image
        {
            get { return mImage; }
            set { SetProperty(ref mImage, value); }
        }

        public double LocationSize { get; set; }

        public double LocationBorderThickness { get; set; }

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
    }
}
