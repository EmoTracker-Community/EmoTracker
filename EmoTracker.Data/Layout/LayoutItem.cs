using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    //
    // Summary:
    //     Indicates where an element should be displayed on the horizontal axis relative
    //     to the allocated layout slot of the parent element.
    public enum HorizontalAlignment
    {
        //
        // Summary:
        //     An element aligned to the left of the layout slot for the parent element.
        Left = 0,
        //
        // Summary:
        //     An element aligned to the center of the layout slot for the parent element.
        Center = 1,
        //
        // Summary:
        //     An element aligned to the right of the layout slot for the parent element.
        Right = 2,
        //
        // Summary:
        //     An element stretched to fill the entire layout slot of the parent element.
        Stretch = 3
    }

    //
    // Summary:
    //     Describes how a child element is vertically positioned or stretched within a
    //     parent's layout slot.
    public enum VerticalAlignment
    {
        //
        // Summary:
        //     The child element is aligned to the top of the parent's layout slot.
        Top = 0,
        //
        // Summary:
        //     The child element is aligned to the center of the parent's layout slot.
        Center = 1,
        //
        // Summary:
        //     The child element is aligned to the bottom of the parent's layout slot.
        Bottom = 2,
        //
        // Summary:
        //     The child element stretches to fill the parent's layout slot.
        Stretch = 3
    }

    public abstract class LayoutItem : ObservableObject
    {
        string mBackground;
        string mForeground;
        private string mMargin;
        private HorizontalAlignment mHorizontalAlignment = HorizontalAlignment.Stretch;
        private VerticalAlignment mVerticalAlignment = VerticalAlignment.Stretch;

        double mScale = -1.0;
        double mWidth = -1.0;
        double mHeight = -1.0;
        double mMinWidth = -1.0;
        double mMinHeight = -1.0;
        double mMaxWidth = -1.0;
        double mMaxHeight = -1.0;
        double mCanvasX = -1.0;
        double mCanvasY = -1.0;
        double mCanvasDepth = -1.0;

        string mUniqueID;

        string mDockLocation;
        bool mbEnableDropShadow = false;
        bool mbEnableBroadcastDropShadow = false;
        bool mbHitTestVisible = true;

        public string UniqueID
        {
            get { return mUniqueID; }
        }

        public bool DropShadow
        {
            get { return mbEnableDropShadow; }
            set { SetProperty(ref mbEnableDropShadow, value); }
        }

        public bool BroadcastShadow
        {
            get { return mbEnableBroadcastDropShadow; }
            set { SetProperty(ref mbEnableBroadcastDropShadow, value); }
        }

        public bool HitTestVisible
        {
            get { return mbHitTestVisible; }
            set { SetProperty(ref mbHitTestVisible, value); }
        }

        public bool OverrideBackground
        {
            get { return !string.IsNullOrEmpty(mBackground); }
        }

        public bool OverrideForeground
        {
            get { return !string.IsNullOrEmpty(mForeground); }
        }

        public bool OverrideDockLocation
        {
            get { return !string.IsNullOrEmpty(mDockLocation); }
        }

        public bool OverrideWidth
        {
            get { return mWidth >= 0.0; }
        }

        public bool OverrideHeight
        {
            get { return mHeight >= 0.0; }
        }

        public bool OverrideMinWidth
        {
            get { return mMinWidth >= 0.0; }
        }

        public bool OverrideMinHeight
        {
            get { return mMinHeight >= 0.0; }
        }

        public bool OverrideMaxWidth
        {
            get { return mMaxWidth >= 0.0; }
        }

        public bool OverrideMaxHeight
        {
            get { return mMaxHeight >= 0.0; }
        }

        public bool OverrideScale
        {
            get { return mScale > 0.0; }
        }

        public bool OverrideCanvasX
        {
            get { return mCanvasX > 0.0; }
        }

        public bool OverrideCanvasY
        {
            get { return mCanvasY > 0.0; }
        }

        public bool OverrideCanvasDepth
        {
            get { return mCanvasDepth > 0.0; }
        }

        public string Background
        {
            get { return mBackground; }
            set { SetProperty(ref mBackground, value); }
        }

        public string Foreground
        {
            get { return mForeground; }
            set { SetProperty(ref mForeground, value); }
        }

        public string DockLocation
        {
            get { return mDockLocation; }
            set { SetProperty(ref mDockLocation, value); }
        }

        public double Scale
        {
            get { return mScale; }
            protected set { SetProperty(ref mScale, value); NotifyPropertyChanged("OverrideScale"); }
        }

        public double Width
        {
            get { return mWidth; }
            protected set { SetProperty(ref mWidth, value); NotifyPropertyChanged("OverrideWidth"); }
        }
        public double Height
        {
            get { return mHeight; }
            protected set { SetProperty(ref mHeight, value); NotifyPropertyChanged("OverrideHeight"); }
        }

        public double MinWidth
        {
            get { return mMinWidth; }
            protected set { SetProperty(ref mMinWidth, value); NotifyPropertyChanged("OverrideMinWidth"); }
        }
        public double MinHeight
        {
            get { return mMinHeight; }
            protected set { SetProperty(ref mMinHeight, value); NotifyPropertyChanged("OverrideMinHeight"); }
        }

        public double MaxWidth
        {
            get { return mMaxWidth; }
            protected set { SetProperty(ref mMaxWidth, value); NotifyPropertyChanged("OverrideMaxWidth"); }
        }
        public double MaxHeight
        {
            get { return mMaxHeight; }
            protected set { SetProperty(ref mMaxHeight, value); NotifyPropertyChanged("OverrideMaxHeight"); }
        }

        public double CanvasX
        {
            get { return mCanvasX; }
            protected set { SetProperty(ref mCanvasX, value); NotifyPropertyChanged("OverrideCanvasX"); }
        }

        public double CanvasY
        {
            get { return mCanvasY; }
            protected set { SetProperty(ref mCanvasY, value); NotifyPropertyChanged("OverrideCanvasY"); }
        }

        public double CanvasDepth
        {
            get { return mCanvasDepth; }
            protected set { SetProperty(ref mCanvasDepth, value); NotifyPropertyChanged("OverrideCanvasDepth"); }
        }

        public string Margin
        {
            get { return mMargin; }
            set { SetProperty(ref mMargin, value); }
        }
        public HorizontalAlignment HorizontalAlignment
        {
            get { return mHorizontalAlignment; }
            set { SetProperty(ref mHorizontalAlignment, value); }
        }
        public VerticalAlignment VerticalAlignment
        {
            get { return mVerticalAlignment; }
            set { SetProperty(ref mVerticalAlignment, value); }
        }

        protected bool TryParse(JObject data, IGamePackage package)
        {
            if (data == null)
                return false;

            mUniqueID = data.GetValue<string>("uid", null);
            
            if (!string.IsNullOrWhiteSpace(mUniqueID))
            {
                LayoutManager.Instance.RegisterLayoutItemForUID(mUniqueID, this);
            }

            Background = data.GetValue<string>("background", null);
            Foreground = data.GetValue<string>("foreground", null);
            DockLocation = data.GetValue<string>("dock", null);
            Margin = data.GetValue<string>("margin", "0");

            if (Tracker.Instance.SwapLeftRight)
            {
                bool bModified = false;

                if (string.Equals(DockLocation, "left", StringComparison.OrdinalIgnoreCase))
                {
                    DockLocation = "right";
                    bModified = true;
                }
                else if (string.Equals(DockLocation, "right", StringComparison.OrdinalIgnoreCase))
                {
                    DockLocation = "left";
                    bModified = true;
                }

                if (bModified && !string.IsNullOrWhiteSpace(Margin))
                {
                    string[] tokens = Margin.Split(',');
                    if (tokens.Length == 4)
                    {
                        Margin = string.Format("{0},{1},{2},{3}", tokens[2], tokens[1], tokens[0], tokens[3]);
                    }
                }
            }

            HorizontalAlignment = data.GetEnumValue<HorizontalAlignment>("h_alignment", HorizontalAlignment.Stretch);
            VerticalAlignment = data.GetEnumValue<VerticalAlignment>("v_alignment", VerticalAlignment.Stretch);
            Scale = data.GetValue<double>("scale", -1.0);
            Width = data.GetValue<double>("width", -1.0);
            Height = data.GetValue<double>("height", -1.0);
            MinWidth = data.GetValue<double>("min_width", -1.0);
            MinHeight = data.GetValue<double>("min_height", -1.0);
            MaxWidth = data.GetValue<double>("max_width", -1.0);
            MaxHeight = data.GetValue<double>("max_height", -1.0);
            CanvasX = data.GetValue<double>("canvas_left", -1.0);
            CanvasY = data.GetValue<double>("canvas_top", -1.0);
            CanvasDepth = data.GetValue<double>("canvas_depth", -1.0);
            DropShadow = data.GetValue<bool>("dropshadow", false);
            BroadcastShadow = data.GetValue<bool>("broadcast_shadow", false);
            HitTestVisible = data.GetValue<bool>("hit_test_visible", true);

            return TryParseInternal(data, package);
        }

        protected void ParseLayoutItemList(JArray list, ICollection<LayoutItem> destination, IGamePackage package)
        {
            if (list != null)
            {
                foreach (JObject entry in list)
                {
                    LayoutItem item = CreateLayoutItem(entry, package);
                    if (item != null)
                        destination.Add(item);
                }
            }
        }

        protected abstract bool TryParseInternal(JObject data, IGamePackage package);

        public static LayoutItem CreateLayoutItem(JObject data, IGamePackage package)
        {
            if (data != null)
            {
                LayoutItem instance = JsonTypeTagsAttribute.CreateIntanceForTypeTag<LayoutItem>(data.GetValue<string>("type"));

                if (instance != null && instance.TryParse(data, package))
                    return instance;
            }

            return null;
        }
    }
}
