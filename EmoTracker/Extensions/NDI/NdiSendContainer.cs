using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using NewTek;      // NDIlib + nested types
using NewTek.NDI;  // UTF
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
        }

        // -------------------------------------------------------------------------
        // Visual tree attachment — start / stop NDI with the window lifetime
        // -------------------------------------------------------------------------

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;

            NdiLibrary.EnsureRuntimeOnPath();

            if (!NDIlib.initialize())
                return; // CPU too old or NDI runtime not installed — stay paused.

            InitializeNdi();

            _exitThread = false;
            _sendThread = new Thread(SendThreadProc) { IsBackground = true, Name = "AvaloniaNdiSendThread" };
            _sendThread.Start();

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

        private void StartCaptureTimer()
        {
            double fps = NdiFrameRateNumerator / (double)NdiFrameRateDenominator;
            _captureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(fps, 1.0))
            };
            _captureTimer.Tick += OnCaptureTick;
            _captureTimer.Start();
        }

        private async void OnCaptureTick(object sender, EventArgs e)
        {
            if (_captureInProgress || _sendInstancePtr == IntPtr.Zero)
                return;

            // Update send-paused state based on live receiver count.
            bool hasReceivers = NDIlib.send_get_no_connections(_sendInstancePtr, 0) > 0;
            IsSendPaused = !hasReceivers;

            if (!hasReceivers)
                return;

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
                    ctx.DrawImage(snapshot, new Rect(0, 0, width, height));
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
                });
            }
            catch
            {
                // Snapshot/draw/copy can throw if the visual tree is torn down
                // mid-capture.  Skip the frame rather than crashing the UI thread.
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
            while (!_exitThread)
            {
                if (!_pendingFrames.TryTake(out PendingFrame frame, 250))
                    continue;

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
                Marshal.Copy(pixels, 0, bufferPtr, bufferSize);

                NDIlib.video_frame_v2_t videoFrame = new NDIlib.video_frame_v2_t
                {
                    xres                 = scaledWidth,
                    yres                 = scaledHeight,
                    FourCC               = NDIlib.FourCC_type_e.FourCC_type_BGRA,
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

                if (Monitor.TryEnter(_sendInstanceLock, 250))
                {
                    try
                    {
                        if (_sendInstancePtr != IntPtr.Zero)
                            NDIlib.send_send_video_v2(_sendInstancePtr, ref videoFrame);
                    }
                    finally
                    {
                        Monitor.Exit(_sendInstanceLock);
                    }
                }

                Marshal.FreeHGlobal(bufferPtr);
            }
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

            NDIlib.destroy();
        }
    }
}
