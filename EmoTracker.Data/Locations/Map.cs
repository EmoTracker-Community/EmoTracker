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
        Thickness mBadgeMargin = new Thickness(-17.5, -17.5, 0, 0);

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
            mBadgeMargin = new Thickness((mSize * 0.5) + (mBadgeSize * -0.5), (mSize * 0.5) + (mBadgeSize * -0.5), 0, 0);
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
