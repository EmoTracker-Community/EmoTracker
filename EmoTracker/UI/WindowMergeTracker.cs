using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EmoTracker.UI
{
    /// <summary>
    /// Detects when a <see cref="MainWindow"/> is being dragged by its title
    /// bar onto another EmoTracker window and, when the user releases the
    /// mouse with the source window inside the target's bounds, merges all
    /// of the source window's tabs into the target's
    /// <see cref="WindowContext"/>. Source window closes as part of the
    /// merge.
    ///
    /// <para>
    /// <b>Why this exists:</b> when a window has only one tab the
    /// <see cref="StateTabStripControl"/> hides itself, so the user can't
    /// re-dock by dragging the tab from the strip. Dragging the entire
    /// window onto another EmoTracker window is the alternate UX —
    /// matches the IDE-style "dock window into another" interaction.
    /// </para>
    ///
    /// <para>
    /// <b>Drag-end detection:</b> Avalonia does not surface a "title bar
    /// drag finished" event because the OS handles the drag natively, and
    /// `PointerReleased` only fires for events delivered to the
    /// application's input pipeline (which a title-bar drag bypasses).
    /// We poll the global mouse button state with
    /// <c>GetAsyncKeyState(VK_LBUTTON)</c> on Windows — the only platform
    /// EmoTracker primarily ships to. The merge fires the instant the user
    /// releases the button, not on an arbitrary debounce.
    /// </para>
    ///
    /// <para>
    /// <b>Visual feedback:</b> while the source's TL is inside a target's
    /// bounds AND the mouse button is held, a translucent
    /// <see cref="MergeIndicatorOverlay"/> window is shown over the target
    /// so the user knows release will merge. The overlay is closed when
    /// the drag moves away from any target, when the merge completes, or
    /// when this tracker is disposed.
    /// </para>
    /// </summary>
    internal sealed class WindowMergeTracker : IDisposable
    {
        readonly MainWindow mWindow;
        readonly DispatcherTimer mPollTimer;
        WindowContext mPendingTarget;
        MergeIndicatorOverlay mOverlay;

        // 30 ms gives a responsive feel — release-to-merge feels instant —
        // without burning the CPU. Only runs while the source window is
        // actively being moved.
        static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);

        // Win32 mouse-button poll: GetAsyncKeyState(VK_LBUTTON) high bit
        // (0x8000) is set when the button is currently down. This is the
        // only signal Avalonia exposes (transitively, through P/Invoke) for
        // the OS-level title-bar drag end — Avalonia's InputManager doesn't
        // see WM_NCLBUTTONUP because the drag operation is processed by
        // DefWindowProc.
        const int VK_LBUTTON = 0x01;
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        public WindowMergeTracker(MainWindow window)
        {
            mWindow = window ?? throw new ArgumentNullException(nameof(window));
            mPollTimer = new DispatcherTimer { Interval = PollInterval };
            mPollTimer.Tick += OnPollTick;
            mWindow.PositionChanged += OnPositionChanged;
            mWindow.Closed += (_, __) => Dispose();
        }

        void OnPositionChanged(object sender, PixelPointEventArgs e)
        {
            // Maximized / minimized window-state changes also fire
            // PositionChanged but aren't title-bar drags. Ignore them.
            if (mWindow.WindowState != WindowState.Normal)
            {
                ClearTarget();
                return;
            }

            // Only react if the user is actively dragging — i.e. the left
            // mouse button is currently down. Programmatic position
            // changes (window startup, etc.) come through with the button
            // up; those should not start a merge candidate.
            if (!IsLeftMouseDown())
            {
                ClearTarget();
                return;
            }

            UpdateTarget(FindMergeTarget());

            // Start polling so we detect the precise instant the user
            // releases the mouse button. Restarting the timer on each
            // PositionChanged is a no-op when it's already running.
            if (!mPollTimer.IsEnabled)
                mPollTimer.Start();
        }

        // Polls the mouse button + target. Two duties:
        //   1. While button is held, refresh overlay placement (covers the
        //      case where the user moves the cursor inside the target's
        //      bounds without crossing a window edge — PositionChanged
        //      doesn't fire then).
        //   2. Detect the button-up edge — that's our drag-end signal.
        void OnPollTick(object sender, EventArgs e)
        {
            bool isDown = IsLeftMouseDown();

            if (isDown)
            {
                // Mid-drag: refresh target in case the cursor moved while
                // the source window itself wasn't moved (rare but possible
                // due to Windows' adjusted drag offsets).
                UpdateTarget(FindMergeTarget());
                return;
            }

            // Button-up edge: the user just released the mouse. Stop
            // polling and decide whether to merge based on whether the
            // source's TL was inside a target window at the moment of
            // release. We use mPendingTarget which was set during the
            // last "down" tick; that's the most recent overlap state.
            mPollTimer.Stop();
            var target = mPendingTarget;
            HideOverlay();
            mPendingTarget = null;

            if (target != null)
                ExecuteMerge(target);
        }

        // The "drop point" we test is the source window's top-left corner.
        // That's where the cursor sits during a title-bar drag (the OS
        // moves the window so the cursor stays at its grab offset within
        // the title bar). Treating TL as the drop point therefore behaves
        // as the user expects — wherever they release the cursor.
        WindowContext FindMergeTarget()
        {
            var src = mWindow.Position;
            var srcCtx = mWindow.WindowContext;
            if (srcCtx == null) return null;

            foreach (var ctx in ApplicationModel.Instance.Windows)
            {
                if (ReferenceEquals(ctx, srcCtx)) continue;
                if (!(ctx.OwnerWindow is Window other)) continue;
                try
                {
                    if (other.WindowState != WindowState.Normal) continue;
                    var op = other.Position;
                    var w = (int)other.Bounds.Width;
                    var h = (int)other.Bounds.Height;
                    if (src.X >= op.X && src.X <= op.X + w
                        && src.Y >= op.Y && src.Y <= op.Y + h)
                        return ctx;
                }
                catch { /* defensive */ }
            }
            return null;
        }

        void UpdateTarget(WindowContext newTarget)
        {
            if (ReferenceEquals(newTarget, mPendingTarget))
            {
                if (mOverlay != null && newTarget?.OwnerWindow is Window w)
                    mOverlay.SyncTo(w);
                return;
            }

            mPendingTarget = newTarget;
            HideOverlay();
            if (newTarget?.OwnerWindow is Window targetWin)
            {
                mOverlay = new MergeIndicatorOverlay();
                mOverlay.SyncTo(targetWin);
                mOverlay.Show();
            }
        }

        // Move every state from the source window's context into
        // <paramref name="target"/>, then close the source window. The last
        // state moved becomes the active tab on the target so the merged
        // window's user focus lands somewhere meaningful (typically the
        // tab the user was just looking at in the source).
        void ExecuteMerge(WindowContext target)
        {
            var sourceCtx = mWindow.WindowContext;
            if (sourceCtx == null) return;
            if (ReferenceEquals(sourceCtx, target)) return;

            var states = new List<Data.Sessions.TrackerState>(sourceCtx.OpenStates);
            if (states.Count == 0)
            {
                // Empty window dropped onto a target — just close.
                try { mWindow.Close(); } catch { }
                return;
            }

            foreach (var state in states)
            {
                sourceCtx.RemoveState(state);
                target.AddState(state, makeActive: false);
            }
            target.ActiveState = states[states.Count - 1];

            // Critical: if mWindow IS the desktop's MainWindow, closing
            // it would shut down the entire app (App.axaml.cs sets
            // ShutdownMode.OnMainWindowClose). The shared promotion
            // helper swaps in another live window — typically the merge
            // target — so the source's close is a regular window close.
            ApplicationModel.Instance.PromoteAlternativeMainWindowIfNeeded(mWindow);

            // Bring the target to the foreground so the merged tabs are
            // visible (the source window briefly held focus during the
            // drag).
            try
            {
                if (target.OwnerWindow is Window targetWin)
                    targetWin.Activate();
            }
            catch { /* defensive */ }

            try { mWindow.Close(); } catch { /* defensive */ }
        }

        void ClearTarget()
        {
            mPollTimer.Stop();
            mPendingTarget = null;
            HideOverlay();
        }

        void HideOverlay()
        {
            if (mOverlay != null)
            {
                try { mOverlay.Close(); } catch { /* defensive */ }
                mOverlay = null;
            }
        }

        static bool IsLeftMouseDown()
        {
            // Windows-only signal. On non-Windows platforms this returns
            // 0 and the merge feature is effectively disabled — acceptable
            // since EmoTracker primarily targets Windows.
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            }
            catch { /* defensive — P/Invoke missing on some platforms */ }
            return false;
        }

        public void Dispose()
        {
            try { mPollTimer.Stop(); } catch { }
            try { mWindow.PositionChanged -= OnPositionChanged; } catch { }
            HideOverlay();
        }
    }

    /// <summary>
    /// Translucent topmost overlay window that mirrors the target window's
    /// screen rectangle. Provides visual feedback during a window-merge
    /// drag — the user sees an accent-bordered glow on the target window
    /// while the source window is hovering inside it.
    /// </summary>
    internal sealed class MergeIndicatorOverlay : Window
    {
        public MergeIndicatorOverlay()
        {
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            Focusable = false;
            Background = Brushes.Transparent;

            // A translucent fill + accent border. Same accent colour as
            // the active tab in StateTabStripControl so the visual link
            // is obvious — "this is where your tab will land".
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4F, 0xC1, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF)),
                BorderThickness = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
        }

        public void SyncTo(Window target)
        {
            try
            {
                Position = target.Position;
                Width = target.Bounds.Width;
                Height = target.Bounds.Height;
            }
            catch { /* defensive */ }
        }
    }
}
