#nullable enable annotations
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.UI.Controls;
using Avalonia.VisualTree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
            LocationDetails.Opened += LocationDetails_Opened;
            LocationDetails.Closed += LocationDetails_Closed;
            BadgeDetails.Closed   += (s, e) => BadgeDetails.IsOpen = false;
            BadgeItemsControl.ItemsSource = mBadgeImages;
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

            // The ItemsPanelTemplate may not have materialized yet at this point.
            // Dispatch so the visual tree is fully built before we walk it.
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateMapOrientation,
                Avalonia.Threading.DispatcherPriority.Loaded);
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

            // Ensure the global dismiss handler is removed if the popup was still open
            LocationDetails.IsOpen = false;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                topLevel.RemoveHandler(PointerPressedEvent, TopLevel_PointerPressedTunnel);
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
            {
                NotifyPropertyChanged(nameof(UseVerticalOrientation));
                NotifyPropertyChanged(nameof(EffectiveMapOrientation));
                UpdateMapOrientation();
            }

            base.OnSizeChanged(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == DataContextProperty)
            {
                NotifyPropertyChanged(nameof(EffectiveMapOrientation));
                UpdateMapOrientation();
            }
        }

        /// <summary>
        /// Imperatively sets the Orientation of the MapsPanel StackPanel inside the
        /// ItemsPanelTemplate. Bindings from inside ItemsPanelTemplate to ancestor
        /// controls don't reliably resolve in Avalonia, so we walk the visual tree instead.
        /// </summary>
        private void UpdateMapOrientation()
        {
            var panel = MapsItemsControl.GetVisualDescendants()
                .OfType<StackPanel>()
                .FirstOrDefault(p => p.Name == "MapsPanel");
            if (panel != null)
                panel.Orientation = EffectiveMapOrientation;
        }

        #region -- Details View --

        private Control? mDetailsTarget;
        private Location? mDetailsLocation;

        public Control? DetailsTarget
        {
            get => mDetailsTarget;
            set
            {
                SetProperty(ref mDetailsTarget, value);
                if (mDetailsTarget != null)
                {
                    BadgeDetails.IsOpen = false;
                    // Set DataContext imperatively — ElementName bindings don't work inside
                    // Popup's OverlayLayer because it renders outside the normal visual tree.
                    LocationDetailsContent.DataContext = mDetailsLocation;
                    LocationDetails.PlacementTarget = mDetailsTarget;
                    LocationDetails.IsOpen = true;
                }
            }
        }

        public Location? DetailsLocation
        {
            get => mDetailsLocation;
            set => SetProperty(ref mDetailsLocation, value);
        }

        /// <summary>
        /// When the location details popup opens, install a tunneling PointerPressed handler
        /// on the top-level window so that clicks anywhere outside the popup will close it.
        /// This replaces IsLightDismissEnabled (which creates an overlay that swallows the
        /// second click of a double-click sequence).
        /// </summary>
        private void LocationDetails_Opened(object? sender, EventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                topLevel.AddHandler(PointerPressedEvent, TopLevel_PointerPressedTunnel, RoutingStrategies.Tunnel);
            }
        }

        private void LocationDetails_Closed(object? sender, EventArgs e)
        {
            LocationDetails.IsOpen = false;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                topLevel.RemoveHandler(PointerPressedEvent, TopLevel_PointerPressedTunnel);
            }
        }

        /// <summary>
        /// Tunneling handler on the top-level window. Closes the location details popup
        /// when the pointer press lands outside the popup content. The press still reaches
        /// the target control (map location, item, etc.) because we don't mark it handled.
        /// </summary>
        private void TopLevel_PointerPressedTunnel(object? sender, PointerPressedEventArgs e)
        {
            if (!LocationDetails.IsOpen)
                return;

            // Check whether the press landed inside the popup content.
            // The popup's child renders on the overlay layer, outside the normal visual tree,
            // so we walk up from e.Source looking for our LocationDetailsContent control.
            var source = e.Source as Visual;
            bool insidePopup = false;
            while (source != null)
            {
                if (source == LocationDetailsContent)
                {
                    insidePopup = true;
                    break;
                }
                source = source.GetVisualParent();
            }

            if (!insidePopup)
            {
                LocationDetails.IsOpen = false;
            }
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
                    if (mBadgesTarget != null)
                    {
                        BadgeDetails.PlacementTarget = mBadgesTarget;
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

        /// <summary>
        /// Resolves the effective map orientation for the inner StackPanel.
        /// WPF used DataTriggers to switch ItemsPanel; in Avalonia we compute
        /// the orientation and bind the StackPanel's Orientation directly.
        /// </summary>
        public Avalonia.Layout.Orientation EffectiveMapOrientation
        {
            get
            {
                if (DataContext is MapPanel mapPanel)
                {
                    return mapPanel.Orientation switch
                    {
                        MapPanel.MapOrientation.Vertical => Avalonia.Layout.Orientation.Vertical,
                        MapPanel.MapOrientation.Horizontal => Avalonia.Layout.Orientation.Horizontal,
                        MapPanel.MapOrientation.Auto => UseVerticalOrientation
                            ? Avalonia.Layout.Orientation.Vertical
                            : Avalonia.Layout.Orientation.Horizontal,
                        _ => Avalonia.Layout.Orientation.Horizontal
                    };
                }
                return Avalonia.Layout.Orientation.Horizontal;
            }
        }

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
            // The popup is closed by TopLevel_PointerPressedTunnel (a tunneling handler on the
            // top-level window) so that clicks on controls outside this map control also dismiss
            // the popup. The tunnel handler fires before this override, so the popup is already
            // closed by the time we get here. If a map location was clicked, we reopen it below.

            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsLeftButtonPressed)
            {
                var element = e.Source as Control;
                if (element != null)
                {
                    MapLocation? data = element.DataContext as MapLocation;
                    if (data != null)
                    {
                        // Open the details popup on every press.
                        // Double-click is handled separately by LocationMapControl_DoubleTapped,
                        // which fires on PointerReleased and doesn't rely on ClickCount.
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

        private void LocationMapControl_DoubleTapped(object? sender, TappedEventArgs e)
        {
            // DoubleTapped fires on PointerReleased and is not affected by PointerPressed.Handled,
            // making it more reliable than ClickCount for detecting double-clicks.
            var element = e.Source as Control;
            MapLocation? data = element?.DataContext as MapLocation;
            if (data?.Location != null)
            {
                data.Location.Pinned = true;
                LocationDetails.IsOpen = false;
                e.Handled = true;
            }
        }
    }
}
