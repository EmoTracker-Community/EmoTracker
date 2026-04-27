using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using EmoTracker.Extensions;
using Serilog;
using System;
using System.ComponentModel;

namespace EmoTracker.Extensions.NDI
{
    /// <summary>
    /// Off-screen host for <see cref="NdiSendContainer"/> used to keep the NDI
    /// broadcast source advertised on the network whether or not the user has
    /// opened the visible <see cref="UI.BroadcastView"/>.  Lifecycle is managed
    /// by <see cref="NDIExtension"/>.
    ///
    /// The window is technically "shown" so Avalonia's compositor keeps its
    /// CompositionVisual root alive AND rasterizes its content on each render
    /// — a hard requirement for <c>Compositor.CreateCompositionVisualSnapshot</c>
    /// to return non-empty pixels.  We rely on <c>Opacity=0.01</c> (NOT 0;
    /// the compositor skips fully transparent windows, producing blank
    /// snapshots), <c>ShowInTaskbar=false</c>, <c>ShowActivated=false</c>,
    /// <c>SystemDecorations=None</c>, and an off-screen <see cref="Window.Position"/>
    /// set after the window opens to keep it invisible to the user.
    ///
    /// Capture driver: an off-screen window does NOT participate in Avalonia's
    /// render loop on its own, so its DispatcherTimer ticks can never produce a
    /// valid composition snapshot.  Instead, we drive captures from the main
    /// window's <see cref="TopLevel.RequestAnimationFrame"/>: every time the
    /// compositor renders the main window (which happens whenever tracker state
    /// changes), we trigger a capture on our NdiSendContainer.  This naturally
    /// aligns NDI frames with real state changes.
    ///
    /// Cross-platform notes:
    ///  * Windows (tested): off-screen positioning + Opacity=0.01 works
    ///    reliably.  Opacity=0 is the failure mode — DWM short-circuits the
    ///    rasterizer for fully transparent windows, leaving the composition
    ///    visual's backing store empty.
    ///  * Linux X11 (untested): absolute positioning is supported by most
    ///    window managers; -32000/-32000 should place the window well outside
    ///    any conceivable monitor layout.  If a compositor clamps the window
    ///    back to a monitor edge, Opacity=0.01 still keeps it imperceptible.
    ///  * Linux Wayland (untested): Wayland protocols do NOT permit clients
    ///    to set absolute window positions — the off-screen coordinates will
    ///    be ignored and the compositor will place the window somewhere
    ///    visible.  Opacity=0.01 is the primary fallback for invisibility
    ///    here; users may observe a near-transparent ghost window.  A future
    ///    improvement could set the window state to Minimized or use a
    ///    compositor-specific layer-shell protocol.
    ///  * macOS (untested): Cocoa generally honours absolute positioning
    ///    including off-screen coordinates, but some window server versions
    ///    will clamp the window back to a screen if detected as fully
    ///    off-screen.  Opacity=0.01 is again the fallback.  ShowInTaskbar
    ///    maps to the Dock on macOS; Avalonia's macOS backend honours it.
    /// </summary>
    public partial class HiddenBroadcastWindow : Window
    {
        // Far enough off-screen that no conceivable monitor layout would bring
        // this window back into view, but well within int32 range so Win32
        // clamping behaviour stays predictable.
        private static readonly PixelPoint OffScreenPosition = new(-32000, -32000);

        private TopLevel _renderDriver;
        private bool _renderLoopActive;
        private readonly EmoTracker.WindowContext _hostContext;
        private readonly Avalonia.Controls.Window _hostWindow;

        // XAML-designer / legacy ctor — no per-host wiring; the window will
        // bind to the app-wide BroadcastLayout fallback so it still has
        // something to render.
        public HiddenBroadcastWindow() : this(null, null) { }

        /// <summary>
        /// Constructs a hidden broadcast window bound to
        /// <paramref name="hostContext"/>'s active tab.
        /// <paramref name="hostWindow"/> is the visible window whose
        /// render loop drives capture ticks (a hidden window with
        /// <c>Opacity=0.01</c> doesn't get its own render frames).
        /// </summary>
        public HiddenBroadcastWindow(EmoTracker.WindowContext hostContext, Avalonia.Controls.Window hostWindow)
        {
            InitializeComponent();
            _hostContext = hostContext;
            _hostWindow = hostWindow;

            // Per-host data context: each window's hidden broadcaster
            // mirrors its own active tab's broadcast layout, so multiple
            // visible windows can each have an independent NDI feed.
            if (hostContext != null)
            {
                DataContext = hostContext.BroadcastLayout;
                hostContext.PropertyChanged += OnHostPropertyChanged;
                NDIHost.NdiName = ResolveNdiName(hostContext);
            }
            else
            {
                DataContext = ApplicationModel.Instance.BroadcastLayout;
                NDIHost.NdiName = "EmoTracker Broadcast View";
            }

            NDIHost.PropertyChanged += OnNdiHostPropertyChanged;

            // Reposition off-screen and hook the render driver once the platform
            // window is fully created.  Setting Position in the constructor (before
            // Show) is ignored by some backends; the Opened event is the reliable
            // hook for both operations.
            Opened += OnOpened;
        }

        private void OnHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EmoTracker.WindowContext.BroadcastLayout))
                DataContext = _hostContext?.BroadcastLayout;
        }

        // Per-host NDI source naming. Multiple HiddenBroadcastWindows
        // running simultaneously must advertise distinct names or
        // receivers see only the first. Uses WindowContext.Sequence —
        // a unique, sequential per-process integer — for guaranteed
        // collision-free names. Note: BroadcastView uses the same naming
        // scheme so the visible/hidden broadcasters for one host share
        // the source name — that's by design, since at most one of the
        // pair is active at a time (toggled by EnableBackgroundNdi).
        static string ResolveNdiName(EmoTracker.WindowContext host)
        {
            if (host == null) return "EmoTracker Broadcast";
            return $"EmoTracker Broadcast {host.Sequence}";
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Defensive: the window should never receive input, but if it
            // somehow does we swallow keystrokes so they can't escape.
            e.Handled = true;
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _renderLoopActive = false;
            _renderDriver = null;
            Opened -= OnOpened;
            if (_hostContext != null)
                _hostContext.PropertyChanged -= OnHostPropertyChanged;
            NDIHost.PropertyChanged -= OnNdiHostPropertyChanged;
            NDIHost.Dispose();
            UpdateExtensionStatus(active: false);
            base.OnClosing(e);
        }

        private void OnOpened(object sender, System.EventArgs e)
        {
            Position = OffScreenPosition;
            StartRenderTickLoop();
        }

        private void OnNdiHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NdiSendContainer.IsSendPaused))
                UpdateExtensionStatus(active: !NDIHost.IsSendPaused);
        }

        private static void UpdateExtensionStatus(bool active)
        {
            var extension = ExtensionManager.Instance?.FindExtension<NDIExtension>();
            if (extension != null)
                extension.Active = active;
        }

        // ----------------------------------------------------------------------
        // Render-driven capture loop
        // ----------------------------------------------------------------------

        private void StartRenderTickLoop()
        {
            _renderDriver = ResolveRenderDriverTopLevel();
            if (_renderDriver == null)
            {
                Log.Warning("[NDI] HiddenBroadcastWindow: no main window TopLevel to drive captures.");
                return;
            }

            Log.Information("[NDI] HiddenBroadcastWindow: driving captures from main window render loop.");
            _renderLoopActive = true;
            ScheduleNextRenderTick();
        }

        private void ScheduleNextRenderTick()
        {
            if (!_renderLoopActive || _renderDriver == null)
                return;

            _renderDriver.RequestAnimationFrame(_ => OnRenderTick());
        }

        private async void OnRenderTick()
        {
            if (!_renderLoopActive)
                return;

            try
            {
                await NDIHost.TriggerCaptureAsync();
            }
            catch
            {
                // Swallow — the capture method has its own try/catch but we don't
                // want an unhandled exception in the RAF callback to kill the loop.
            }
            finally
            {
                // Re-register for the next render frame.  Even on failure we want
                // the loop to keep going so a transient glitch doesn't stop NDI.
                ScheduleNextRenderTick();
            }
        }

        /// <summary>
        /// The hidden window doesn't render itself, so its own RequestAnimationFrame
        /// never fires.  We need a TopLevel that IS actively rendering — the
        /// host app window the user is interacting with, since the broadcast
        /// layout mirrors what that window is already displaying and
        /// re-rendering on every state change. Falls back to the desktop's
        /// MainWindow when no host was supplied (legacy / designer path).
        /// </summary>
        private TopLevel ResolveRenderDriverTopLevel()
        {
            if (_hostWindow != null) return _hostWindow;
            var lifetime = Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;
            return lifetime?.MainWindow;
        }
    }
}
