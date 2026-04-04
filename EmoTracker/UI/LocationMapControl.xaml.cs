using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.Data.Notes;
using EmoTracker.UI.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for LocationMapControl.xaml
    /// </summary>
    public partial class LocationMapControl : ObservableUserControl
    {
        public LocationMapControl()
        {
            InitializeComponent();
            this.Loaded += LocationMapControl_Loaded;
            this.Unloaded += LocationMapControl_Unloaded;
            LocationDetails.Opened += LocationDetails_Opened;
            LocationDetails.Closed += LocationDetails_Closed;
            BadgeDetails.Closed += BadgeDetails_Closed;
        }

        private void LocationMapControl_Loaded(object sender, RoutedEventArgs e)
        {
            MainWindow w = Application.Current.MainWindow as MainWindow;
            if (w != null)
            {
                w.OnGlobalPreviewKeyDown += MainWindow_OnGlobalPreviewKeyEvent;
                w.OnGlobalPreviewKeyUp += MainWindow_OnGlobalPreviewKeyEvent;
            }
        }

        private void LocationMapControl_Unloaded(object sender, RoutedEventArgs e)
        {
            MainWindow w = Application.Current.MainWindow as MainWindow;
            if (w != null)
            {
                w.OnGlobalPreviewKeyDown -= MainWindow_OnGlobalPreviewKeyEvent;
                w.OnGlobalPreviewKeyUp -= MainWindow_OnGlobalPreviewKeyEvent;
            }
        }

        private void MainWindow_OnGlobalPreviewKeyEvent(object sender, KeyEventArgs e)
        {
            NotifyPropertyChanged("IsShiftPressed");
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            bool bOldAspect = sizeInfo.PreviousSize.Height > sizeInfo.PreviousSize.Width;
            bool bNewAspect = sizeInfo.NewSize.Height > sizeInfo.NewSize.Width;

            if (bOldAspect != bNewAspect)
                NotifyPropertyChanged("UseVerticalOrientation");

            base.OnRenderSizeChanged(sizeInfo);
        }

        private void LocationDetails_Opened(object sender, EventArgs e)
        {
            BadgeDetails.IsOpen = false;
        }

        private void LocationDetails_Closed(object sender, EventArgs e)
        {
            LocationDetails.IsOpen = false;
        }

        private void BadgeDetails_Closed(object sender, EventArgs e)
        {
            BadgeDetails.IsOpen = false;
        }

        #region -- Details View --

        FrameworkElement mDetailsTarget;
        Location mDetailsLocation;

        public FrameworkElement DetailsTarget
        {
            get { return mDetailsTarget; }
            set
            {
                if (SetProperty(ref mDetailsTarget, value) || !LocationDetails.IsOpen)
                {
                    if (DetailsTarget != null)
                    {
                        LocationDetails.IsOpen = false;
                        LocationDetails.IsOpen = true;
                    }
                }
            }
        }

        public Location DetailsLocation
        {
            get { return mDetailsLocation; }
            set { SetProperty(ref mDetailsLocation, value); }
        }

        #endregion

        #region -- Badges Display --

        FrameworkElement mBadgesTarget;
        Location mBadgesLocation;

        public FrameworkElement BadgesTarget
        {
            get { return mBadgesTarget; }
            set
            {
                if (SetProperty(ref mBadgesTarget, value))
                {
                    if (BadgesTarget != null)
                    {
                        BadgeDetails.IsOpen = false;
                        BadgeDetails.IsOpen = true;
                    }
                    else
                    {
                        BadgeDetails.IsOpen = false;
                    }
                }
            }
        }

        public Location BadgesLocation
        {
            get { return mBadgesLocation; }
            set { SetProperty(ref mBadgesLocation, value); }
        }

        ObservableCollection<ImageReference> mBadgeImages = new ObservableCollection<ImageReference>();

        public IReadOnlyList<ImageReference> BadgeImages
        {
            get { return mBadgeImages; }
        }

        private void CollectBadgeImages(Location location)
        {
            mBadgeImages.Clear();

            if (location != null)
            {
                foreach (ImageReference badge in location.Badges)
                {
                    mBadgeImages.Add(badge);
                }

                HashSet<ITrackableItem> items = new HashSet<ITrackableItem>();
                foreach (Section s in location.Sections)
                {
                    if (s.CapturedItem != null)
                    {
                        if (!items.Contains(s.CapturedItem))
                            items.Add(s.CapturedItem);
                    }
                }

                foreach (Note note in location.NoteTakingSite.Notes)
                {
                    IItemCollection itemsContainer = note as IItemCollection;
                    if (itemsContainer != null)
                    {
                        foreach (ITrackableItem item in itemsContainer.Items)
                        {
                            if (!items.Contains(item))
                                items.Add(item);
                        }
                    }
                }

                foreach (ITrackableItem item in items)
                {
                    if (item.PotentialIcon != null)
                        mBadgeImages.Add(item.PotentialIcon);
                }
            }
        }

        FrameworkElement FindDataContextRoot(FrameworkElement element)
        {
            if (element != null)
            {
                object context = element.DataContext;

                while (element.Parent as FrameworkElement != null && (element.Parent as FrameworkElement).DataContext == context)
                    element = element.Parent as FrameworkElement;
            }

            return element;
        }

        #endregion

        public bool UseVerticalOrientation
        {
            get
            {
                return ActualHeight > ActualWidth;
            }
        }

        public bool IsShiftPressed
        {
            get
            {
                //  TODO: Migrate to hit test
                return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && Keyboard.FocusedElement as TextBox == null && IsMouseOver;
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            FrameworkElement element = e.MouseDevice.DirectlyOver as FrameworkElement;
            if (element != null)
            {
                MapLocation data = element.DataContext as MapLocation;
                if (data != null && !LocationDetails.IsOpen)
                {
                    if (data.Location != BadgesLocation || BadgesTarget == null)
                    {
                        CollectBadgeImages(data.Location);

                        if (mBadgeImages.Count > 0)
                        {
                            element = FindDataContextRoot(element);

                            BadgesLocation = data.Location;
                            BadgesTarget = element;
                        }
                    }
                }
                else
                {
                    BadgesTarget = null;
                }
            }

            base.OnPreviewMouseMove(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            NotifyPropertyChanged("IsShiftPressed");
            base.OnMouseMove(e);
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            FrameworkElement element = e.MouseDevice.DirectlyOver as FrameworkElement;
            if (element != null)
            {
                MapLocation data = element.DataContext as MapLocation;
                if (data != null)
                {
                    DetailsLocation = data.Location;
                    DetailsTarget = element;
                    e.Handled = true;
                }
            }

            base.OnPreviewMouseLeftButtonDown(e);
        }

        protected override void OnPreviewMouseDoubleClick(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                FrameworkElement element = e.MouseDevice.DirectlyOver as FrameworkElement;
                if (element != null)
                {
                    MapLocation data = element.DataContext as MapLocation;
                    if (data != null && data.Location != null)
                    {
                        data.Location.Pinned = true;
                        e.Handled = true;
                        return;
                    }
                }
            }

            base.OnPreviewMouseDoubleClick(e);
        }

        protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
        {
            FrameworkElement element = e.MouseDevice.DirectlyOver as FrameworkElement;
            if (element != null)
            {
                MapLocation data = element.DataContext as MapLocation;
                if (data != null)
                {
                    if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        using (TransactionProcessor.Current.OpenTransaction())
                        {
                            data.Location.FullClearAllPossible();
                            data.Location.ModifiedByUser = true;
                            e.Handled = true;
                        }
                    }
                }
            }

            base.OnPreviewMouseRightButtonDown(e);
        }
    }
}
