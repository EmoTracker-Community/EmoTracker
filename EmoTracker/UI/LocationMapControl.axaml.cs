using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.UI.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Location = EmoTracker.Data.Locations.Location;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for LocationMapControl.axaml
    /// </summary>
    public partial class LocationMapControl : ObservableUserControl
    {
        // Keyboard modifier tracking (replaces WPF Keyboard.Modifiers)
        private bool mShiftKeyDown = false;

        public LocationMapControl()
        {
            InitializeComponent();
            this.AttachedToVisualTree += LocationMapControl_Loaded;
            this.DetachedFromVisualTree += LocationMapControl_Unloaded;
            this.DoubleTapped += LocationMapControl_DoubleTapped;
        }

        private void LocationMapControl_Loaded(object? sender, VisualTreeAttachmentEventArgs e)
        {
            MainWindow? w = (Avalonia.Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
            if (w != null)
            {
                w.OnGlobalPreviewKeyDown += MainWindow_OnGlobalPreviewKeyEvent;
                w.OnGlobalPreviewKeyUp += MainWindow_OnGlobalPreviewKeyEvent;
            }
        }

        private void LocationMapControl_Unloaded(object? sender, VisualTreeAttachmentEventArgs e)
        {
            MainWindow? w = (Avalonia.Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
            if (w != null)
            {
                w.OnGlobalPreviewKeyDown -= MainWindow_OnGlobalPreviewKeyEvent;
                w.OnGlobalPreviewKeyUp -= MainWindow_OnGlobalPreviewKeyEvent;
            }
        }

        private void MainWindow_OnGlobalPreviewKeyEvent(object? sender, KeyEventArgs e)
        {
            mShiftKeyDown = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            NotifyPropertyChanged(nameof(IsShiftPressed));
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            bool bOldAspect = e.PreviousSize.Height > e.PreviousSize.Width;
            bool bNewAspect = e.NewSize.Height > e.NewSize.Width;

            if (bOldAspect != bNewAspect)
                NotifyPropertyChanged(nameof(UseVerticalOrientation));

            base.OnSizeChanged(e);
        }

        #region -- Details View --

        private Control? mDetailsTarget;
        private Location? mDetailsLocation;

        public Control? DetailsTarget
        {
            get => mDetailsTarget;
            set
            {
                if (SetProperty(ref mDetailsTarget, value))
                {
                    // In Avalonia, popup management is done in code-behind
                    // Trigger property notification so bindings update
                    NotifyPropertyChanged(nameof(DetailsTarget));
                }
            }
        }

        public Location? DetailsLocation
        {
            get => mDetailsLocation;
            set => SetProperty(ref mDetailsLocation, value);
        }

        #endregion

        #region -- Badges Display --

        private Control? mBadgesTarget;
        private Location? mBadgesLocation;

        public Control? BadgesTarget
        {
            get => mBadgesTarget;
            set
            {
                if (SetProperty(ref mBadgesTarget, value))
                {
                    NotifyPropertyChanged(nameof(BadgesTarget));
                }
            }
        }

        public Location? BadgesLocation
        {
            get => mBadgesLocation;
            set => SetProperty(ref mBadgesLocation, value);
        }

        private readonly ObservableCollection<ImageReference> mBadgeImages = new();

        public IReadOnlyList<ImageReference> BadgeImages => mBadgeImages;

        private void CollectBadgeImages(Location? location)
        {
            mBadgeImages.Clear();

            if (location != null)
            {
                foreach (ImageReference badge in location.Badges)
                    mBadgeImages.Add(badge);

                var items = new HashSet<ITrackableItem>();
                foreach (Section s in location.Sections)
                {
                    if (s.CapturedItem != null && items.Add(s.CapturedItem))
                    {
                        // already added via Add
                    }
                }

                foreach (Data.Notes.Note note in location.NoteTakingSite.Notes)
                {
                    if (note is IItemCollection itemsContainer)
                    {
                        foreach (ITrackableItem item in itemsContainer.Items)
                            items.Add(item);
                    }
                }

                foreach (ITrackableItem item in items)
                {
                    if (item.PotentialIcon != null)
                        mBadgeImages.Add(item.PotentialIcon);
                }
            }
        }

        private Control? FindDataContextRoot(Control? element)
        {
            if (element != null)
            {
                object? context = element.DataContext;
                while (element.Parent is Control parent && parent.DataContext == context)
                    element = parent;
            }
            return element;
        }

        #endregion

        public bool UseVerticalOrientation => Bounds.Height > Bounds.Width;

        public bool IsShiftPressed
        {
            get
            {
                // Track shift state via KeyDown/KeyUp handlers attached to MainWindow
                return mShiftKeyDown && IsPointerOver;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            NotifyPropertyChanged(nameof(IsShiftPressed));

            var element = e.Source as Control;
            if (element != null)
            {
                MapLocation? data = element.DataContext as MapLocation;
                if (data != null)
                {
                    if (data.Location != BadgesLocation || BadgesTarget == null)
                    {
                        CollectBadgeImages(data.Location);

                        if (mBadgeImages.Count > 0)
                        {
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

            base.OnPointerMoved(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsLeftButtonPressed)
            {
                var element = e.Source as Control;
                if (element != null)
                {
                    MapLocation? data = element.DataContext as MapLocation;
                    if (data != null)
                    {
                        DetailsLocation = data.Location;
                        DetailsTarget = element;
                        e.Handled = true;
                    }
                }
            }
            else if (props.IsRightButtonPressed)
            {
                var element = e.Source as Control;
                if (element != null)
                {
                    MapLocation? data = element.DataContext as MapLocation;
                    if (data != null)
                    {
                        if (!mShiftKeyDown)
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
            }

            base.OnPointerPressed(e);
        }

        private void LocationMapControl_DoubleTapped(object sender, TappedEventArgs e)
        {
            var element = e.Source as Control;
            if (element != null)
            {
                MapLocation? data = element.DataContext as MapLocation;
                if (data?.Location != null)
                {
                    data.Location.Pinned = true;
                    e.Handled = true;
                    return;
                }
            }

        }
    }
}
