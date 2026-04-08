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
    /// </summary>
    public partial class HiddenBroadcastWindow : Window
    {
        // Far enough off-screen that no conceivable monitor layout would bring
        // this window back into view, but well within int32 range so Win32
        // clamping behaviour stays predictable.
        private static readonly PixelPoint OffScreenPosition = new(-32000, -32000);

        private TopLevel _renderDriver;
        private bool _renderLoopActive;

        public HiddenBroadcastWindow()
        {
            InitializeComponent();

            NDIHost.PropertyChanged += OnNdiHostPropertyChanged;

            // Reposition off-screen and hook the render driver once the platform
            // window is fully created.  Setting Position in the constructor (before
            // Show) is ignored by some backends; the Opened event is the reliable
            // hook for both operations.
            Opened += OnOpened;
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
        /// never fires.  We need a TopLevel that IS actively rendering — the main
        /// app window is the obvious candidate since the broadcast layout mirrors
        /// data that the main window is already displaying and re-rendering on
        /// every state change.
        /// </summary>
        private static TopLevel ResolveRenderDriverTopLevel()
        {
            var lifetime = Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;
            return lifetime?.MainWindow;
        }
    }
}
