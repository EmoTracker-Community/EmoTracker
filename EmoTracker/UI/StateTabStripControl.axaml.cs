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
    ///
    /// <para>
    /// <b>Switching content on tab switch:</b> the in-Data layer's active
    /// state slot (<c>SessionContext.ActiveState</c>) is updated when this
    /// window is the active window. For tabs from <i>different</i>
    /// PackageInstances, switching also triggers a <c>Tracker.Reload</c>
    /// of the target's package so XAML bindings against
    /// <c>{x:Static Tracker.Instance}</c> rebuild against the new pack's
    /// catalogs. For tabs from the <i>same</i> PackageInstance (forks),
    /// content rebinding is incomplete pending the Phase 7.6 polish XAML
    /// migration to <c>WindowContext.ActiveState.X</c> bindings.
    /// </para>
    /// </summary>
    public partial class StateTabStripControl : UserControl
    {
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
        const double DragThreshold = 5.0;
        const double TearOffThreshold = 40.0;

        public StateTabStripControl()
        {
            InitializeComponent();
            this.AttachedToVisualTree += (_, __) => OnAttached();
            this.DetachedFromVisualTree += (_, __) => OnDetached();
        }

        // ---------- Lifecycle ----------------------------------------------

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
            RefreshVisibility();
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
            EndDrag();
        }

        void OnContextPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowContext.ActiveState))
                RefreshHighlights();
        }

        void OnOpenStatesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshVisibility();
            // Defer highlight refresh until templates re-realize their
            // visual children for the new collection.
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshHighlights);
        }

        // Hide the strip + reserve only minimal space when there are
        // 0 or 1 tabs — the single-state scenario looks identical to
        // pre-7.8 (no strip), and the empty-state hint shows when the
        // window has no states at all.
        void RefreshVisibility()
        {
            int n = mContext?.OpenStates?.Count ?? 0;
            // Strip is collapsed when there's 0 or 1 tab.
            this.Height = (n >= 2) ? 28 : 0;
            this.IsVisible = (n >= 2);
            var hint = this.FindControl<TextBlock>("EmptyHint");
            if (hint != null) hint.IsVisible = (n == 0);
        }

        // Apply selected highlight (top-border accent + lighter background)
        // to whichever tab matches ActiveState.
        void RefreshHighlights()
        {
            var host = this.FindControl<ItemsControl>("TabsHost");
            if (host == null) return;
            foreach (var border in host.GetVisualDescendants())
            {
                if (border is Border b && b.Tag is TrackerState st)
                {
                    bool isActive = mContext != null && ReferenceEquals(st, mContext.ActiveState);
                    b.Background = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D))
                        : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    // Top-edge accent for the active tab (VS Code style):
                    // 2px coloured top, 0px elsewhere except a 1px right
                    // separator that's always present.
                    b.BorderThickness = new Thickness(0, 2, 1, 0);
                    b.BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                }
            }
        }

        // ---------- Pointer events: click / middle-close / drag-tear-off ----

        void OnTabPointerPressed(object sender, PointerPressedEventArgs e)
        {
            try
            {
                if (sender is not Border b || b.Tag is not TrackerState state)
                    return;
                if (mContext == null) return;

                var props = e.GetCurrentPoint(b).Properties;
                if (props.IsMiddleButtonPressed)
                {
                    // Middle-click → close. Mark the event handled so
                    // PointerReleased's drag-end logic doesn't also fire.
                    EndDrag();
                    e.Handled = true;
                    SafeRemoveState(state);
                    return;
                }
                if (props.IsLeftButtonPressed)
                {
                    SwitchActiveState(state);
                    mDragStart = e.GetPosition(this);
                    mDragState = state;
                    mDragging = false;
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                // Defensive: any pointer-event handler that throws would
                // tear down the input pipeline. Swallow + reset.
                EndDrag();
            }
        }

        void OnTabPointerMoved(object sender, PointerEventArgs e)
        {
            try
            {
                if (mDragState == null) return;
                var pos = e.GetPosition(this);
                var delta = pos - mDragStart;
                var dist = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                if (!mDragging && dist > DragThreshold)
                    mDragging = true;
                if (!mDragging) return;

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
                        ApplicationModel.Instance.OpenStateInNewWindow(ctx, state);
                }
            }
            catch (Exception)
            {
                EndDrag();
            }
        }

        void OnTabPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            try
            {
                if (mDragging && mDragState != null && mContext != null)
                {
                    // Compose a screen-pixel point: this window's screen
                    // origin + this control's offset in the window + pointer
                    // position in this control.
                    Avalonia.PixelPoint screenPt = ComposeScreenPoint(e.GetPosition(this));
                    var target = ApplicationModel.Instance.FindTabStripAtScreenPoint(screenPt);
                    if (target != null && !ReferenceEquals(target, this) && target.mContext != null)
                    {
                        var movingState = mDragState;
                        var sourceCtx = mContext;
                        var targetCtx = target.mContext;
                        EndDrag();
                        sourceCtx.RemoveState(movingState);
                        targetCtx.AddState(movingState);
                        e.Handled = true;
                        return;
                    }
                }
            }
            catch (Exception)
            {
            }
            EndDrag();
        }

        Avalonia.PixelPoint ComposeScreenPoint(Point pIn)
        {
            var win = this.GetVisualRoot() as Window;
            if (win == null) return default;
            double offX = 0, offY = 0;
            Avalonia.Visual cur = this;
            while (cur != null && !ReferenceEquals(cur, win))
            {
                offX += cur.Bounds.X;
                offY += cur.Bounds.Y;
                cur = cur.GetVisualParent();
            }
            return new Avalonia.PixelPoint(
                win.Position.X + (int)(offX + pIn.X),
                win.Position.Y + (int)(offY + pIn.Y));
        }

        void EndDrag()
        {
            mDragging = false;
            mDragState = null;
        }

        // ---------- Tab close + context menu --------------------------------

        void OnCloseTab(object sender, RoutedEventArgs e)
        {
            try
            {
                var state = (sender as Control)?.Tag as TrackerState;
                if (state == null) return;
                e.Handled = true;
                SafeRemoveState(state);
            }
            catch (Exception)
            {
            }
        }

        void OnContextOpenInNewWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var state = (sender as Control)?.Tag as TrackerState;
                if (state == null || mContext == null) return;
                ApplicationModel.Instance.OpenStateInNewWindow(mContext, state);
            }
            catch (Exception)
            {
            }
        }

        void OnContextSave(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplicationModel.Instance.SaveCommand?.Execute(null);
            }
            catch (Exception)
            {
            }
        }

        void OnContextClose(object sender, RoutedEventArgs e)
        {
            try
            {
                var state = (sender as Control)?.Tag as TrackerState;
                if (state == null) return;
                SafeRemoveState(state);
            }
            catch (Exception)
            {
            }
        }

        // ---------- State-switching support ---------------------------------

        // Switches the WindowContext's active state and (if this is the
        // currently-active window) drives the in-Data layer's active state
        // slot to match. Returns true if a switch happened.
        void SwitchActiveState(TrackerState state)
        {
            if (state == null || mContext == null) return;
            if (ReferenceEquals(mContext.ActiveState, state)) return;

            mContext.ActiveState = state;

            // Update the global active-state slot iff this is the active window.
            if (ReferenceEquals(mContext, ApplicationModel.Instance.CurrentlyActiveWindowContext))
                ApplicationModel.Instance.OnActiveStateSwitched(state);
        }

        // Robust state removal: removes from ALL windows that contain it,
        // then removes from its owning PackageInstance (which fires
        // per-state extension cleanup). Closes a now-empty non-last window.
        void SafeRemoveState(TrackerState state)
        {
            if (state == null) return;

            // Snapshot windows first because RemoveState mutates collections.
            var windowSnapshot = new System.Collections.Generic.List<WindowContext>(ApplicationModel.Instance.Windows);
            foreach (var ctx in windowSnapshot)
                ctx.RemoveState(state);

            // Remove from owning PackageInstance.
            foreach (var pi in ApplicationModel.Instance.PackageInstances)
            {
                if (pi.States.ContainsKey(state.Id))
                {
                    pi.RemoveState(state.Id);
                    break;
                }
            }

            // If a window is now empty and it isn't the last window, close it.
            var liveWindows = new System.Collections.Generic.List<WindowContext>(ApplicationModel.Instance.Windows);
            if (liveWindows.Count > 1)
            {
                foreach (var ctx in liveWindows)
                {
                    if (ctx.OpenStates.Count == 0 && ctx.OwnerWindow is Window w)
                    {
                        try { w.Close(); } catch { }
                    }
                }
            }
        }
    }
}
