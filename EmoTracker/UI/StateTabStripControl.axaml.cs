using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmoTracker.Data.Sessions;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace EmoTracker.UI
{
    /// <summary>
    /// Phase 7.8 / 7.9: VS Code-style tab strip listing the WindowContext's
    /// OpenStates. Click switches active state. Middle-click closes a tab.
    /// Drag tab outside the strip's bounds tears it off into a new window
    /// (Phase 7.9). Drag tab to another window's strip docks it there
    /// (Phase 7.9, app-level coordination via ApplicationModel).
    /// </summary>
    public partial class StateTabStripControl : UserControl
    {
        // ---------- DependencyProperties / direct-properties ---------------

        public static readonly DirectProperty<StateTabStripControl, ObservableCollection<TrackerState>> OpenStatesProperty =
            AvaloniaProperty.RegisterDirect<StateTabStripControl, ObservableCollection<TrackerState>>(
                nameof(OpenStates), o => o.OpenStates);

        ObservableCollection<TrackerState> mOpenStates;
        public ObservableCollection<TrackerState> OpenStates
        {
            get => mOpenStates;
            private set => SetAndRaise(OpenStatesProperty, ref mOpenStates, value);
        }

        WindowContext mContext;

        // ---------- Drag tracking ------------------------------------------

        bool mDragging;
        Point mDragStart;
        TrackerState mDragState;
        Border mDragBorder;
        const double DragThreshold = 5.0;
        // Phase 7.9: drag outside strip bounds by this many px → tear off.
        const double TearOffThreshold = 40.0;

        public StateTabStripControl()
        {
            InitializeComponent();

            this.AttachedToVisualTree += (_, __) => OnAttached();
            this.DetachedFromVisualTree += (_, __) => OnDetached();
        }

        void OnAttached()
        {
            var win = this.GetVisualRoot() as MainWindow;
            mContext = win?.WindowContext;
            if (mContext != null)
            {
                OpenStates = mContext.OpenStates;
                mContext.PropertyChanged += OnContextPropertyChanged;
                if (mContext.OpenStates is INotifyCollectionChanged ncc)
                    ncc.CollectionChanged += OnOpenStatesChanged;
            }
            RefreshHighlights();
            RefreshEmptyHint();
        }

        void OnDetached()
        {
            if (mContext != null)
            {
                mContext.PropertyChanged -= OnContextPropertyChanged;
                if (mContext.OpenStates is INotifyCollectionChanged ncc)
                    ncc.CollectionChanged -= OnOpenStatesChanged;
            }
            mContext = null;
        }

        void OnContextPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowContext.ActiveState))
                RefreshHighlights();
        }

        void OnOpenStatesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshEmptyHint();
            RefreshHighlights();
        }

        void RefreshEmptyHint()
        {
            var hint = this.FindControl<TextBlock>("EmptyHint");
            if (hint != null)
                hint.IsVisible = (OpenStates == null || OpenStates.Count == 0);
        }

        // Apply selected highlight (a 2px accent underline on Background) to
        // whichever tab matches ActiveState.
        void RefreshHighlights()
        {
            var host = this.FindControl<ItemsControl>("TabsHost");
            if (host == null) return;
            // Walk visual descendants for our Border name "TabBorder".
            foreach (var border in host.GetVisualDescendants())
            {
                if (border is Border b && b.Tag is TrackerState st)
                {
                    bool isActive = mContext != null && ReferenceEquals(st, mContext.ActiveState);
                    b.Background = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D))
                        : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    b.BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    // Underline accent: bottom border thickness 2 when active.
                    b.BorderThickness = isActive
                        ? new Thickness(0, 0, 1, 2)
                        : new Thickness(0, 0, 1, 0);
                }
            }
        }

        // ---------- Pointer events: click / middle-close / drag-tear-off ----

        void OnTabPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not Border b) return;
            var state = b.Tag as TrackerState;
            if (state == null) return;
            var props = e.GetCurrentPoint(b).Properties;
            if (props.IsMiddleButtonPressed)
            {
                // Middle-click → close.
                mContext?.RemoveState(state);
                e.Handled = true;
                return;
            }
            if (props.IsLeftButtonPressed)
            {
                // Activate; record drag start.
                if (mContext != null) mContext.ActiveState = state;
                mDragStart = e.GetPosition(this);
                mDragState = state;
                mDragBorder = b;
                mDragging = false;
                e.Handled = true;
            }
        }

        void OnTabPointerMoved(object sender, PointerEventArgs e)
        {
            if (mDragState == null) return;
            var pos = e.GetPosition(this);
            var delta = pos - mDragStart;
            var dist = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            if (!mDragging && dist > DragThreshold)
                mDragging = true;
            // Phase 7.9: if dragged below the strip by TearOffThreshold,
            // tear off into a new window. (Above-the-strip / sideways tear
            // outside the strip bounds also count.)
            if (mDragging)
            {
                bool outsideStrip =
                    pos.Y < -TearOffThreshold ||
                    pos.Y > Bounds.Height + TearOffThreshold ||
                    pos.X < -TearOffThreshold ||
                    pos.X > Bounds.Width + TearOffThreshold;
                if (outsideStrip)
                {
                    var state = mDragState;
                    var ctx = mContext;
                    EndDrag();
                    if (ctx != null && state != null)
                    {
                        // Remove from this window, spawn new.
                        ApplicationModel.Instance.OpenStateInNewWindow(ctx, state);
                    }
                }
            }
        }

        void OnTabPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            // For Phase 7.9 cross-window dock: a release while dragging that
            // lands inside ANOTHER window's tab strip moves the state. The
            // app-level helper resolves the target.
            if (mDragging && mDragState != null && mContext != null)
            {
                // Compose a screen-pixel point: this window's screen
                // origin + this control's offset in the window + pointer
                // position in this control.
                var win = this.GetVisualRoot() as Window;
                Avalonia.PixelPoint screenPt = default;
                if (win != null)
                {
                    double offX = 0, offY = 0;
                    Avalonia.Visual cur = this;
                    while (cur != null && !ReferenceEquals(cur, win))
                    {
                        offX += cur.Bounds.X;
                        offY += cur.Bounds.Y;
                        cur = cur.GetVisualParent();
                    }
                    var pIn = e.GetPosition(this);
                    screenPt = new Avalonia.PixelPoint(
                        win.Position.X + (int)(offX + pIn.X),
                        win.Position.Y + (int)(offY + pIn.Y));
                }
                var target = ApplicationModel.Instance.FindTabStripAtScreenPoint(screenPt);
                if (target != null && !ReferenceEquals(target, this))
                {
                    var movingState = mDragState;
                    var sourceCtx = mContext;
                    EndDrag();
                    sourceCtx.RemoveState(movingState);
                    target.mContext?.AddState(movingState);
                    e.Handled = true;
                    return;
                }
            }
            EndDrag();
        }

        void EndDrag()
        {
            mDragging = false;
            mDragState = null;
            mDragBorder = null;
        }

        // ---------- Tab close button + context menu ------------------------

        void OnCloseTab(object sender, RoutedEventArgs e)
        {
            var state = (sender as Control)?.Tag as TrackerState;
            if (state == null) return;
            mContext?.RemoveState(state);
            e.Handled = true;
        }

        void OnContextOpenInNewWindow(object sender, RoutedEventArgs e)
        {
            var state = (sender as Control)?.Tag as TrackerState;
            if (state == null || mContext == null) return;
            ApplicationModel.Instance.OpenStateInNewWindow(mContext, state);
        }

        void OnContextSave(object sender, RoutedEventArgs e)
        {
            // Defer to existing per-state save command (delegated via the
            // BundleService for now; a polished version surfaces a "Save
            // this state to file…" dialog).
            ApplicationModel.Instance.SaveCommand?.Execute(null);
        }

        void OnContextClose(object sender, RoutedEventArgs e)
        {
            var state = (sender as Control)?.Tag as TrackerState;
            if (state == null) return;
            mContext?.RemoveState(state);
        }
    }
}
