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
        DragPreviewWindow mDragPreview;
        const double DragThreshold = 5.0;

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
                    // Phase 7 polish: capture the pointer so subsequent
                    // PointerMoved / PointerReleased events still fire on
                    // this border even after the pointer leaves it. Without
                    // this, dragging out of the strip's bounds never
                    // triggers the tear-off threshold.
                    e.Pointer.Capture(b);
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
                {
                    // Drag begins: spawn the preview window. The preview
                    // follows the cursor in screen-space; it's closed in
                    // PointerReleased / EndDrag.
                    mDragging = true;
                    try
                    {
                        mDragPreview = new DragPreviewWindow(mDragState.Name ?? "(unnamed)");
                        mDragPreview.FollowCursor(ComposeScreenPoint(pos));
                        mDragPreview.Show();
                    }
                    catch
                    {
                        // Preview is best-effort: if the platform refuses to
                        // open the auxiliary window, keep the drag alive but
                        // without visual feedback.
                        mDragPreview = null;
                    }
                }
                if (mDragging && mDragPreview != null)
                {
                    try { mDragPreview.FollowCursor(ComposeScreenPoint(pos)); }
                    catch { /* defensive */ }
                }
                // Drop decision happens at PointerReleased — DO NOT tear off
                // mid-move. Earlier behavior would rip the tab into a new
                // window the moment the cursor crossed an arbitrary
                // threshold around the strip, which made it impossible to
                // dock a tab back into an existing window once you'd left
                // its strip's bounds. With release-time decisioning, the
                // user can drag through any region of the desktop and only
                // the final drop point matters.
            }
            catch (Exception)
            {
                EndDrag();
            }
        }

        void OnTabPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            // Phase 7 polish: release the pointer capture established in
            // PointerPressed so events route normally afterwards.
            try { e.Pointer.Capture(null); } catch { }

            // Capture state BEFORE we tear down drag-mode flags (EndDrag
            // also closes the preview window — which we want to do FIRST so
            // it doesn't sit on top as a topmost window while we spawn /
            // activate a tear-off window).
            bool wasDragging = mDragging;
            var movingState = mDragState;
            var sourceCtx = mContext;
            Avalonia.PixelPoint screenPt = default;
            Point localPt = default;
            if (wasDragging && movingState != null && sourceCtx != null)
            {
                localPt = e.GetPosition(this);
                screenPt = ComposeScreenPoint(localPt);
            }

            // Close the drag preview before any window-creation work runs:
            // the preview is Topmost and could obscure / steal Z-order from
            // the new tear-off window otherwise.
            EndDrag();

            try
            {
                if (wasDragging && movingState != null && sourceCtx != null)
                {
                    HandleDrop(movingState, sourceCtx, screenPt, localPt);
                    e.Handled = true;
                }
            }
            catch (Exception)
            {
                // Defensive — drop handling can touch many subsystems
                // (window creation, extension lifecycle, etc.); a throw
                // here shouldn't tear down the input pipeline.
            }
        }

        // Decides what to do with the dragged tab on release. The pointer's
        // screen-space coordinate steers the routing:
        //
        //   * Over the source's own strip           -> reorder within the
        //                                              source window.
        //   * Over another window's tab strip       -> dock into that strip.
        //   * Over another EmoTracker window (any   -> dock at the end of
        //     region, but not on its strip)            that window's strip.
        //   * Over the source window (not strip)    -> no-op, keep current.
        //   * Outside every EmoTracker window       -> spawn a new window
        //                                              at the drop point
        //                                              and dock there.
        //
        // The "drop on any window" behavior makes docking forgiving: the
        // user doesn't have to hit the narrow tab strip rectangle — anywhere
        // on the destination window's frame counts.
        //
        // <paramref name="localPt"/> is the pointer's position in this strip's
        // local coordinate space, used by the same-strip reorder path so it
        // can pick a target index without re-projecting from screen-space.
        void HandleDrop(TrackerState movingState, WindowContext sourceCtx, Avalonia.PixelPoint screenPt, Point localPt)
        {
            var targetStrip = ApplicationModel.Instance.FindTabStripAtScreenPoint(screenPt);

            // Same-strip drop -> reorder.
            if (targetStrip != null && ReferenceEquals(targetStrip, this))
            {
                ReorderWithinStrip(movingState, sourceCtx, localPt);
                return;
            }

            // Cross-strip drop -> dock into target window's strip.
            if (targetStrip != null && targetStrip.mContext != null)
            {
                sourceCtx.RemoveState(movingState);
                targetStrip.mContext.AddState(movingState);
                return;
            }

            var targetCtx = ApplicationModel.Instance.FindWindowContextAtScreenPoint(screenPt);
            if (targetCtx != null && !ReferenceEquals(targetCtx, sourceCtx))
            {
                sourceCtx.RemoveState(movingState);
                targetCtx.AddState(movingState);
                return;
            }
            if (targetCtx != null && ReferenceEquals(targetCtx, sourceCtx))
            {
                // Released over the source window but not on the strip —
                // user signaled "stay where I am". Keep the tab put.
                return;
            }

            // Outside every live EmoTracker window — spawn a new window
            // at the drop point and dock the state there.
            ApplicationModel.Instance.OpenStateInNewWindow(sourceCtx, movingState, screenPt);
        }

        // Reorder <paramref name="movingState"/> within <paramref name="sourceCtx"/>'s
        // OpenStates based on the cursor's <paramref name="localPt"/> within
        // this strip. Picks a target index by walking each tab Border's
        // local-space bounds and finding the slot whose horizontal midpoint
        // the cursor is closest to. Inserts at the end if the cursor is past
        // the last tab. No-ops if the resulting index would leave the order
        // unchanged.
        void ReorderWithinStrip(TrackerState movingState, WindowContext sourceCtx, Point localPt)
        {
            var openStates = sourceCtx.OpenStates;
            int sourceIdx = openStates.IndexOf(movingState);
            if (sourceIdx < 0) return;

            int targetIdx = FindTargetTabIndex(localPt);
            if (targetIdx < 0) return;

            // After removing source first, indices to its right shift down
            // by one. ObservableCollection.Move handles this internally,
            // but our targetIdx was computed against the original layout —
            // adjust if the target is past the source's slot.
            if (targetIdx > sourceIdx) targetIdx--;
            if (targetIdx == sourceIdx) return;
            if (targetIdx >= openStates.Count) targetIdx = openStates.Count - 1;
            if (targetIdx < 0) targetIdx = 0;

            openStates.Move(sourceIdx, targetIdx);
        }

        // Walks the tab strip's child Borders in horizontal order and
        // returns the index where a tab dropped at <paramref name="localPt"/>
        // should be inserted. The drop slot is determined by which tab's
        // horizontal midpoint <paramref name="localPt"/>.X falls before;
        // if past every tab's right edge, returns the count (i.e. append).
        int FindTargetTabIndex(Point localPt)
        {
            var host = this.FindControl<ItemsControl>("TabsHost");
            if (host == null) return -1;

            // Collect tab borders and order by their on-screen X so the
            // returned index reflects the visual order regardless of
            // which order GetVisualDescendants happens to return them in.
            var borders = new System.Collections.Generic.List<(Border B, double Left, double Width)>();
            foreach (var v in host.GetVisualDescendants())
            {
                if (v is Border b && b.Tag is TrackerState)
                {
                    var p = b.TranslatePoint(new Point(0, 0), this);
                    if (p.HasValue)
                        borders.Add((b, p.Value.X, b.Bounds.Width));
                }
            }
            borders.Sort((a, c) => a.Left.CompareTo(c.Left));

            for (int i = 0; i < borders.Count; i++)
            {
                var entry = borders[i];
                var center = entry.Left + entry.Width / 2.0;
                if (localPt.X < center)
                    return i;
            }
            return borders.Count;
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
            if (mDragPreview != null)
            {
                try { mDragPreview.Close(); }
                catch { /* defensive — closing a window can throw if it's
                           in a transient state from the platform's POV */ }
                mDragPreview = null;
            }
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
                        // If this window IS the desktop MainWindow, promote
                        // a sibling first — closing the MainWindow with
                        // ShutdownMode.OnMainWindowClose would terminate the
                        // app even though other windows are still alive.
                        ApplicationModel.Instance.PromoteAlternativeMainWindowIfNeeded(w);
                        try { w.Close(); } catch { }
                    }
                }
            }
        }
    }
}
