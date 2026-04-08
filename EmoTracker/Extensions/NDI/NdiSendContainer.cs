using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using NewTek;      // NDIlib + nested types
using NewTek.NDI;  // UTF
using Serilog;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.NDI
{
    /// <summary>
    /// Avalonia ContentControl that broadcasts its rendered content as an NDI source.
    /// Equivalent to NewTek.NDI.WPF.NdiSendContainer for the cross-platform Avalonia build.
    ///
    /// Requires the NDI Tools runtime to be installed on the host machine.
    /// On Windows, NdiLibrary.EnsureRuntimeOnPath() is called automatically to locate
    /// the native DLL via the NDI_RUNTIME_DIR_Vx environment variable.
    ///
    /// Capture path: uses <see cref="Compositor.CreateCompositionVisualSnapshot"/>
    /// rather than <see cref="RenderTargetBitmap"/>.  RTB routes through
    /// ImmediateRenderer, which walks the visual tree applying RenderTransform,
    /// Opacity, Clip, and OpacityMask — but deliberately ignores <see cref="IEffect"/>
    /// (DropShadowDirectionEffect etc.).  Effects in Avalonia 11 are a
    /// composition-layer concept: only the composition render path calls
    /// <c>canvas.PushEffect(Effect)</c>, so drop shadows applied to layout panels
    /// via BoolToDropShadowEffectConverter only appear in the compositor output.
    /// CreateCompositionVisualSnapshot runs the same server-side render pipeline
    /// the live compositor uses, so the resulting bitmap includes all effects.
    /// </summary>
    public class NdiSendContainer : ContentControl, INotifyPropertyChanged, IDisposable
    {
        // -------------------------------------------------------------------------
        // Avalonia properties
        // -------------------------------------------------------------------------

        public static readonly StyledProperty<string> NdiNameProperty =
            AvaloniaProperty.Register<NdiSendContainer, string>(nameof(NdiName), "Unnamed - Fix Me.");

        public string NdiName
        {
            get => GetValue(NdiNameProperty);
            set => SetValue(NdiNameProperty, value);
        }

        public static readonly StyledProperty<int> NdiFrameRateNumeratorProperty =
            AvaloniaProperty.Register<NdiSendContainer, int>(nameof(NdiFrameRateNumerator), 30000);

        public int NdiFrameRateNumerator
        {
            get => GetValue(NdiFrameRateNumeratorProperty);
            set => SetValue(NdiFrameRateNumeratorProperty, value);
        }

        public static readonly StyledProperty<int> NdiFrameRateDenominatorProperty =
            AvaloniaProperty.Register<NdiSendContainer, int>(nameof(NdiFrameRateDenominator), 1000);

        public int NdiFrameRateDenominator
        {
            get => GetValue(NdiFrameRateDenominatorProperty);
            set => SetValue(NdiFrameRateDenominatorProperty, value);
        }

        public static readonly StyledProperty<int> NdiIntegerScaleProperty =
            AvaloniaProperty.Register<NdiSendContainer, int>(nameof(NdiIntegerScale), 1);

        public int NdiIntegerScale
        {
            get => GetValue(NdiIntegerScaleProperty);
            set => SetValue(NdiIntegerScaleProperty, value);
        }

        public static readonly StyledProperty<bool> NdiDropShadowProperty =
            AvaloniaProperty.Register<NdiSendContainer, bool>(nameof(NdiDropShadow), false);

        public bool NdiDropShadow
        {
            get => GetValue(NdiDropShadowProperty);
            set => SetValue(NdiDropShadowProperty, value);
        }

        public static readonly StyledProperty<bool> NdiClockToVideoProperty =
            AvaloniaProperty.Register<NdiSendContainer, bool>(nameof(NdiClockToVideo), true);

        public bool NdiClockToVideo
        {
            get => GetValue(NdiClockToVideoProperty);
            set => SetValue(NdiClockToVideoProperty, value);
        }

        /// <summary>
        /// Gates all NDI initialisation.  When <c>false</c>, attaching this control
        /// to the visual tree does NOT create an NDI sender, start the capture timer,
        /// or spin up the send thread — the control becomes a plain ContentControl.
        /// Used by the visible <see cref="UI.BroadcastView"/> to yield NDI responsibility
        /// to the hidden background window when that path is active, avoiding the
        /// double-source conflict that would otherwise occur.
        ///
        /// Must be set before the control is attached to the visual tree — runtime
        /// changes are not honoured for an already-attached container.
        /// </summary>
        public static readonly StyledProperty<bool> NdiEnabledProperty =
            AvaloniaProperty.Register<NdiSendContainer, bool>(nameof(NdiEnabled), true);

        public bool NdiEnabled
        {
            get => GetValue(NdiEnabledProperty);
            set => SetValue(NdiEnabledProperty, value);
        }

        /// <summary>
        /// When <c>true</c>, the container does NOT start its own DispatcherTimer-based
        /// capture loop on attach.  An external driver is responsible for calling
        /// <see cref="TriggerCaptureAsync"/> whenever a new frame should be captured.
        ///
        /// This exists for the <see cref="HiddenBroadcastWindow"/> case: an off-screen
        /// window doesn't participate in Avalonia's render loop, so its own
        /// DispatcherTimer ticks can't produce valid composition snapshots (no render
        /// pulse has ever updated the visual tree).  The hidden window instead hooks
        /// into the visible main window's <c>RequestAnimationFrame</c>, which fires
        /// whenever the compositor is actively rendering it (i.e. whenever tracker
        /// state changes).
        ///
        /// Must be set before the control is attached to the visual tree.
        /// </summary>
        public static readonly StyledProperty<bool> UseExternalCaptureDriverProperty =
            AvaloniaProperty.Register<NdiSendContainer, bool>(nameof(UseExternalCaptureDriver), false);

        public bool UseExternalCaptureDriver
        {
            get => GetValue(UseExternalCaptureDriverProperty);
            set => SetValue(UseExternalCaptureDriverProperty, value);
        }

        // Re-create the NDI sender when name or clock mode changes.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (_attached && (change.Property == NdiNameProperty || change.Property == NdiClockToVideoProperty))
                InitializeNdi();
        }

        // IsSendPaused is driven by live connection-count polling and exposed
        // so BroadcastView can update the NDI status indicator.
        private bool _isSendPaused = true;
        public bool IsSendPaused
        {
            get => _isSendPaused;
            private set
            {
                if (_isSendPaused == value) return;
                _isSendPaused = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSendPaused)));
            }
        }

        public new event PropertyChangedEventHandler PropertyChanged;

        // -------------------------------------------------------------------------
        // Private state
        // -------------------------------------------------------------------------

        private readonly object _sendInstanceLock = new();
        private IntPtr _sendInstancePtr = IntPtr.Zero;

        private DispatcherTimer _captureTimer;
        private Thread _sendThread;
        private bool _exitThread;
        private bool _attached;
        private bool _disposed;

        // True only when this instance successfully called NDIlib.initialize().
        // Must be checked in Dispose to avoid calling NDIlib.destroy() unbalanced
        // — the NDI library is reference-counted, and extra destroy calls from
        // dormant containers (NdiEnabled=false) would tear down the shared library
        // state while another container is still using it.
        private bool _ndiInitialized;

        // Prevents re-entry if a snapshot takes longer than the capture interval.
        // CreateCompositionVisualSnapshot is async; without this guard, slow frames
        // would stack up and eventually exhaust memory.
        private bool _captureInProgress;

        // Reusable intermediate for pixel extraction.  The Bitmap returned by
        // CreateCompositionVisualSnapshot is immutable and its base-class CopyPixels
        // throws NotSupportedException.  We draw the snapshot onto this RTB (which
        // is writable and supports CopyPixels) to get at the pixels.
        private RenderTargetBitmap _rtb;

        private readonly BlockingCollection<PendingFrame> _pendingFrames = new();

        private struct PendingFrame
        {
            public byte[] Pixels;
            public int Width, Height, Stride;
            public int FrameRateNum, FrameRateDen;
            public int Scale;
            public bool DropShadow;
            public NDIlib.FourCC_type_e FourCC;
        }

        // -------------------------------------------------------------------------
        // Visual tree attachment — start / stop NDI with the window lifetime
        // -------------------------------------------------------------------------

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;

            Log.Debug(
                "[NDI] NdiSendContainer attached (Enabled={Enabled}, ExternalDriver={External}, Name={Name})",
                NdiEnabled, UseExternalCaptureDriver, NdiName);

            // Opt-out gate: another component (the hidden background window) is
            // handling NDI for this app, so this container stays dormant to avoid
            // advertising a duplicate source.
            if (!NdiEnabled)
            {
                Log.Debug("[NDI] Container dormant (NdiEnabled=false); skipping init.");
                return;
            }

            NdiLibrary.EnsureRuntimeOnPath();

            if (!NDIlib.initialize())
            {
                Log.Warning("[NDI] NDIlib.initialize() returned false. CPU unsupported or runtime not installed.");
                return;
            }
            _ndiInitialized = true;

            InitializeNdi();
            if (_sendInstancePtr == IntPtr.Zero)
            {
                Log.Warning("[NDI] NDIlib.send_create returned IntPtr.Zero for {Name}", NdiName);
            }
            else
            {
                Log.Information("[NDI] Sender created for {Name} (ptr={Ptr:X})", NdiName, _sendInstancePtr.ToInt64());
            }

            _exitThread = false;
            _sendThread = new Thread(SendThreadProc) { IsBackground = true, Name = "AvaloniaNdiSendThread" };
            _sendThread.Start();

            // Always start a DispatcherTimer, but at different rates depending
            // on whether an external driver is also feeding us captures:
            //   - Internal driver only: timer fires at the configured NDI frame
            //     rate (primary capture source).
            //   - External driver + slow poll: timer fires at a slow interval
            //     (~250ms) purely to poll receiver count and catch transitions
            //     when the external driver is idle (e.g. main window not
            //     rendering because user isn't interacting).
            StartCaptureTimer();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            Dispose();
        }

        // -------------------------------------------------------------------------
        // NDI sender (re-)initialisation
        // -------------------------------------------------------------------------

        private void InitializeNdi()
        {
            if (string.IsNullOrEmpty(NdiName))
                return;

            lock (_sendInstanceLock)
            {
                if (_sendInstancePtr != IntPtr.Zero)
                {
                    NDIlib.send_destroy(_sendInstancePtr);
                    _sendInstancePtr = IntPtr.Zero;
                }

                IntPtr namePtr = UTF.StringToUtf8(NdiName);

                NDIlib.send_create_t desc = new NDIlib.send_create_t
                {
                    p_ndi_name  = namePtr,
                    p_groups    = IntPtr.Zero,
                    clock_video = NdiClockToVideo,
                    clock_audio = false,
                };

                _sendInstancePtr = NDIlib.send_create(ref desc);

                Marshal.FreeHGlobal(namePtr);
            }
        }

        // -------------------------------------------------------------------------
        // Capture timer — fires on the UI thread at the target NDI frame rate
        // -------------------------------------------------------------------------

        // Slow-poll interval used when an external driver is the primary capture
        // source.  This exists solely to detect receiver-count transitions while
        // the external driver is idle — too fast wastes CPU, too slow adds
        // perceptible latency to the "OBS just connected" state change.
        private const double ExternalDriverPollIntervalMs = 250.0;

        // Throttle for "_sendInstancePtr was zero when TriggerCaptureAsync ran"
        // warnings so a persistent bug doesn't flood the log at 4 Hz.
        private DateTime _lastMissingSenderLogUtc = DateTime.MinValue;
        private void LogSenderMissingWarning()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastMissingSenderLogUtc).TotalSeconds < 5) return;
            _lastMissingSenderLogUtc = now;
            Log.Warning(
                "[NDI] TriggerCaptureAsync called but _sendInstancePtr is zero "
                + "(Attached={Attached}, Enabled={Enabled}, NdiInitialized={Init}, Name={Name})",
                _attached, NdiEnabled, _ndiInitialized, NdiName);
        }

        // Throttled debug heartbeat showing that captures are being produced.
        // One line every ~5s keeps the log readable while still confirming flow.
        private DateTime _lastCaptureHeartbeatUtc = DateTime.MinValue;
        private int _capturesSinceLastHeartbeat;
        private void LogCaptureHeartbeat(int width, int height)
        {
            _capturesSinceLastHeartbeat++;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastCaptureHeartbeatUtc).TotalSeconds < 5) return;
            Log.Debug("[NDI] Captured {Count} frames in last interval ({W}x{H})",
                _capturesSinceLastHeartbeat, width, height);
            _lastCaptureHeartbeatUtc = now;
            _capturesSinceLastHeartbeat = 0;
        }

        private void StartCaptureTimer()
        {
            double intervalMs;
            if (UseExternalCaptureDriver)
            {
                intervalMs = ExternalDriverPollIntervalMs;
            }
            else
            {
                double fps = NdiFrameRateNumerator / (double)NdiFrameRateDenominator;
                intervalMs = 1000.0 / Math.Max(fps, 1.0);
            }

            _captureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };
            _captureTimer.Tick += OnCaptureTick;
            _captureTimer.Start();
        }

        private async void OnCaptureTick(object sender, EventArgs e)
        {
            await TriggerCaptureAsync();
        }

        /// <summary>
        /// Captures one frame from the control's visual tree and queues it for NDI
        /// transmission.  Safe to call from the UI thread; reentry is guarded so
        /// overlapping calls collapse to a single capture.
        ///
        /// For the internally-timer-driven case this is called automatically by the
        /// DispatcherTimer.  For the <see cref="UseExternalCaptureDriver"/> case
        /// (HiddenBroadcastWindow), an external driver calls this from the main
        /// window's RequestAnimationFrame so captures align with the compositor's
        /// actual render cycle rather than a dead off-screen tree.
        /// </summary>
        public async Task TriggerCaptureAsync()
        {
            if (_captureInProgress || _disposed)
                return;

            if (_sendInstancePtr == IntPtr.Zero)
            {
                // Warn once per ~5s so we don't flood the log if this is a
                // persistent bug; ticks fire at 250ms in the external-driver
                // path, which would be 20 log entries per second otherwise.
                LogSenderMissingWarning();
                return;
            }

            // Update send-paused state based on live receiver count.
            bool hasReceivers = NDIlib.send_get_no_connections(_sendInstancePtr, 0) > 0;
            IsSendPaused = !hasReceivers;

            if (!hasReceivers)
                return;

            // Force a synchronous layout pass on this subtree so Bounds reflect the
            // current content.  An off-screen window's layout system may not run on
            // its own (the compositor skips rendering it), which would leave Bounds
            // at 0x0 and the snapshot empty.  Measuring with Infinity gives the
            // control the same unconstrained sizing it would get from the window's
            // SizeToContent pass.
            if (!IsMeasureValid)
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (!IsArrangeValid)
                Arrange(new Rect(DesiredSize));

            if (Bounds.Width < 8 || Bounds.Height < 8)
                return;

            // Get the composition visual backing this control.  The control must be
            // attached to a live visual tree (ensured by _attached / OnAttachedToVisualTree)
            // and connected to a compositor target.
            CompositionVisual compositionVisual = ElementComposition.GetElementVisual(this);
            if (compositionVisual == null)
                return;

            // Use the display's physical pixel density so text is captured at the
            // same sharpness it appears on screen.  RenderScaling is read every tick
            // so window-moves to a different-DPI monitor are handled automatically.
            double renderScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

            _captureInProgress = true;
            try
            {
                // Runs the full composition render pipeline, including IEffect
                // (DropShadowDirectionEffect) that ImmediateRenderer would skip.
                using Bitmap snapshot = await compositionVisual.Compositor
                    .CreateCompositionVisualSnapshot(compositionVisual, renderScale);

                if (snapshot == null || _disposed)
                    return;

                // Derive the NDI pixel format from the snapshot's reported format rather
                // than inferring it from the OS.  The compositor backend decides channel
                // order (BGRA on Windows/D3D, RGBA on macOS/Metal), and reading it here
                // handles any future backend changes automatically.
                NDIlib.FourCC_type_e ndiFormat;
                switch (snapshot.Format)
                {
                    case PixelFormat f when f == PixelFormat.Bgra8888:
                        ndiFormat = NDIlib.FourCC_type_e.FourCC_type_BGRA;
                        break;
                    case PixelFormat f when f == PixelFormat.Rgba8888:
                        ndiFormat = NDIlib.FourCC_type_e.FourCC_type_RGBA;
                        break;
                    default:
                        Log.Warning("[NDI] Unsupported snapshot pixel format {Format}; defaulting to BGRA.", snapshot.Format);
                        ndiFormat = NDIlib.FourCC_type_e.FourCC_type_BGRA;
                        break;
                }

                int width  = snapshot.PixelSize.Width;
                int height = snapshot.PixelSize.Height;
                int stride     = width * 4;
                int bufferSize = height * stride;

                // Draw the (immutable) snapshot into a writable RTB so we can read
                // back its pixels; the base Bitmap.CopyPixels throws for the type
                // the compositor returns.
                if (_rtb == null ||
                    _rtb.PixelSize.Width  != width ||
                    _rtb.PixelSize.Height != height)
                {
                    _rtb?.Dispose();
                    _rtb = new RenderTargetBitmap(
                        new PixelSize(width, height),
                        new Vector(96 * renderScale, 96 * renderScale));
                }

                using (var ctx = _rtb.CreateDrawingContext())
                {
                    // DrawImage destination is in logical coordinates, but width/height
                    // are physical pixels from the snapshot.  Divide by renderScale so the
                    // snapshot fills the RTB at 1:1 physical pixels rather than being
                    // stretched (which would zoom in and cut off content on Retina/HiDPI).
                    ctx.DrawImage(snapshot, new Rect(0, 0, width / renderScale, height / renderScale));
                }

                byte[] pixels = new byte[bufferSize];

                // Pin the managed array so its address is stable during the native copy.
                GCHandle gch = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    _rtb.CopyPixels(
                        new PixelRect(0, 0, width, height),
                        gch.AddrOfPinnedObject(),
                        bufferSize,
                        stride);
                }
                finally
                {
                    gch.Free();
                }

                _pendingFrames.Add(new PendingFrame
                {
                    Pixels       = pixels,
                    Width        = width,
                    Height       = height,
                    Stride       = stride,
                    FrameRateNum = NdiFrameRateNumerator,
                    FrameRateDen = NdiFrameRateDenominator,
                    Scale        = Math.Max(NdiIntegerScale, 1),
                    DropShadow   = NdiDropShadow,
                    FourCC       = ndiFormat,
                });

                LogCaptureHeartbeat(width, height);
            }
            catch (Exception ex)
            {
                // Snapshot/draw/copy can throw if the visual tree is torn down
                // mid-capture.  Skip the frame rather than crashing the UI thread.
                Log.Warning(ex, "[NDI] Capture failed: {Msg}", ex.Message);
            }
            finally
            {
                _captureInProgress = false;
            }
        }

        // -------------------------------------------------------------------------
        // Send thread — drains the frame queue and calls NDI
        // -------------------------------------------------------------------------

        private void SendThreadProc()
        {
            Log.Debug("[NDI] Send thread started.");
            try
            {
                while (!_exitThread)
                {
                    try
                    {
                        ProcessOneFrame();
                    }
                    catch (OperationCanceledException)
                    {
                        // BlockingCollection completed (normal shutdown).
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Per-frame failures must NOT kill the send thread —
                        // a single bad frame (e.g. bogus dimensions) should
                        // log and move on.  Throttled so a persistent failure
                        // can't flood the log.
                        LogSendFailure(ex);
                    }
                }
            }
            finally
            {
                Log.Debug("[NDI] Send thread exiting (exitRequested={Exit}).", _exitThread);
            }
        }

        private void ProcessOneFrame()
        {
            if (!_pendingFrames.TryTake(out PendingFrame frame, 250))
                return;

            // Drop stale frames to prevent lag accumulation.
            while (_pendingFrames.Count > 1)
                _pendingFrames.TryTake(out _);

            // Avalonia's RenderTargetBitmap outputs premultiplied BGRA.
            // Convert to straight alpha before shadow compositing and NDI send;
            // NDI's FourCC_type_BGRA and the drop-shadow algorithm both expect
            // straight (un-premultiplied) values.
            NdiImageProcessing.UnPremultiply(frame.Pixels);

            byte[] pixels = frame.DropShadow
                ? NdiImageProcessing.FastDropShadow(frame.Pixels, frame.Width, frame.Height, frame.Stride)
                : frame.Pixels;

            pixels = NdiImageProcessing.ScaleBuffer(pixels, frame.Width, frame.Height, frame.Stride, frame.Scale);

            int scaledWidth  = frame.Width  * frame.Scale;
            int scaledHeight = frame.Height * frame.Scale;
            int stride       = scaledWidth  * 4;
            int bufferSize   = scaledHeight * stride;

            IntPtr bufferPtr = Marshal.AllocHGlobal(bufferSize);
            try
            {
                Marshal.Copy(pixels, 0, bufferPtr, bufferSize);

                NDIlib.video_frame_v2_t videoFrame = new NDIlib.video_frame_v2_t
                {
                    xres                 = scaledWidth,
                    yres                 = scaledHeight,
                    // FourCC is derived from the snapshot's reported pixel format at
                    // capture time — see TriggerCaptureAsync — so it matches whatever
                    // the compositor backend actually produced.
                    FourCC               = frame.FourCC,
                    frame_rate_N         = frame.FrameRateNum,
                    frame_rate_D         = frame.FrameRateDen,
                    picture_aspect_ratio = (float)frame.Width / frame.Height,
                    frame_format_type    = NDIlib.frame_format_type_e.frame_format_type_progressive,
                    timecode             = NDIlib.send_timecode_synthesize,
                    p_data               = bufferPtr,
                    line_stride_in_bytes = stride,
                    p_metadata           = IntPtr.Zero,
                    timestamp            = 0,
                };

                bool sent = false;
                if (Monitor.TryEnter(_sendInstanceLock, 250))
                {
                    try
                    {
                        if (_sendInstancePtr != IntPtr.Zero)
                        {
                            NDIlib.send_send_video_v2(_sendInstancePtr, ref videoFrame);
                            sent = true;
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_sendInstanceLock);
                    }
                }

                if (sent)
                    LogSendHeartbeat();
            }
            finally
            {
                Marshal.FreeHGlobal(bufferPtr);
            }
        }

        // Throttled heartbeat showing that frames are actually being sent.
        private DateTime _lastSendHeartbeatUtc = DateTime.MinValue;
        private int _sendsSinceLastHeartbeat;
        private void LogSendHeartbeat()
        {
            _sendsSinceLastHeartbeat++;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastSendHeartbeatUtc).TotalSeconds < 5) return;
            Log.Debug("[NDI] Sent {Count} frames in last interval.", _sendsSinceLastHeartbeat);
            _lastSendHeartbeatUtc = now;
            _sendsSinceLastHeartbeat = 0;
        }

        // Throttled send-failure logging so a persistent exception doesn't flood.
        private DateTime _lastSendFailureLogUtc = DateTime.MinValue;
        private void LogSendFailure(Exception ex)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastSendFailureLogUtc).TotalSeconds < 5) return;
            _lastSendFailureLogUtc = now;
            Log.Error(ex, "[NDI] Send thread frame processing failed: {Msg}", ex.Message);
        }

        // -------------------------------------------------------------------------
        // Dispose
        // -------------------------------------------------------------------------

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~NdiSendContainer() => Dispose(false);

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _captureTimer?.Stop();
                _captureTimer = null;

                _exitThread = true;
                _sendThread?.Join(500);
                _sendThread = null;

                _pendingFrames.CompleteAdding();
                _rtb?.Dispose();
                _rtb = null;
            }

            lock (_sendInstanceLock)
            {
                if (_sendInstancePtr != IntPtr.Zero)
                {
                    NDIlib.send_destroy(_sendInstancePtr);
                    _sendInstancePtr = IntPtr.Zero;
                }
            }

            // Only balance NDIlib.destroy() against our own successful initialize().
            // A dormant container (NdiEnabled=false) never called initialize, so it
            // must not call destroy — otherwise it would tear down the shared NDI
            // library state while another container (e.g. HiddenBroadcastWindow) is
            // still actively using it.
            if (_ndiInitialized)
            {
                NDIlib.destroy();
                _ndiInitialized = false;
                Log.Debug("[NDI] NdiSendContainer disposed; NDIlib.destroy() called.");
            }
            else
            {
                Log.Debug("[NDI] NdiSendContainer disposed; skipping NDIlib.destroy() (was dormant).");
            }
        }
    }
}
