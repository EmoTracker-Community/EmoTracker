using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NewTek.NDI.WPF
{    
    public class NdiSendContainer : ContentPresenter, INotifyPropertyChanged, IDisposable
    {
        [Category("NewTek NDI"),
        Description("Whether or not to apply a global dropshadow to the render result.")]
        public bool NdiDropShadow
        {
            get { return (bool)GetValue(NdiDropShadowProperty); }
            set { SetValue(NdiDropShadowProperty, value); }
        }
        public static readonly DependencyProperty NdiDropShadowProperty =
            DependencyProperty.Register("NdiDropShadow", typeof(bool), typeof(NdiSendContainer), new PropertyMetadata(false));

        [Category("NewTek NDI"),
        Description("NDI output width in pixels. Required.")]
        public int NdiWidth
        {
            get { return (int)GetValue(NdiWidthProperty); }
            set { SetValue(NdiWidthProperty, value); }
        }
        public static readonly DependencyProperty NdiWidthProperty =
            DependencyProperty.Register("NdiWidth", typeof(int), typeof(NdiSendContainer), new PropertyMetadata(1280));

        [Category("NewTek NDI"),
        Description("NDI output height in pixels. Required.")]
        public int NdiHeight
        {
            get { return (int)GetValue(NdiHeightProperty); }
            set { SetValue(NdiHeightProperty, value); }
        }
        public static readonly DependencyProperty NdiHeightProperty =
            DependencyProperty.Register("NdiHeight", typeof(int), typeof(NdiSendContainer), new PropertyMetadata(720));


        [Category("NewTek NDI"),
        Description("NDI output frame rate numerator. Required.")]
        public int NdiFrameRateNumerator
        {
            get { return (int)GetValue(NdiFrameRateNumeratorProperty); }
            set { SetValue(NdiFrameRateNumeratorProperty, value); }
        }
        public static readonly DependencyProperty NdiFrameRateNumeratorProperty =
            DependencyProperty.Register("NdiFrameRateNumerator", typeof(int), typeof(NdiSendContainer), new PropertyMetadata(30000));
        
        [Category("NewTek NDI"),
        Description("NDI output frame rate denominator. Required.")]
        public int NdiFrameRateDenominator
        {
            get { return (int)GetValue(NdiFrameRateDenominatorProperty); }
            set { SetValue(NdiFrameRateDenominatorProperty, value); }
        }
        public static readonly DependencyProperty NdiFrameRateDenominatorProperty =
            DependencyProperty.Register("NdiFrameRateDenominator", typeof(int), typeof(NdiSendContainer), new PropertyMetadata(1000));

        [Category("NewTek NDI"),
        Description("Integer scale applied to frame output prior to broadcast.")]
        public int NdiIntegerScale
        {
            get { return (int)GetValue(NdiIntegerScaleProperty); }
            set { SetValue(NdiIntegerScaleProperty, value); }
        }
        public static readonly DependencyProperty NdiIntegerScaleProperty =
            DependencyProperty.Register("NdiIntegerScale", typeof(int), typeof(NdiSendContainer), new PropertyMetadata(1));

        [Category("NewTek NDI"),
        Description("NDI output name as displayed to receivers. Required.")]
        public String NdiName
        {
            get { return (String)GetValue(NdiNameProperty); }
            set { SetValue(NdiNameProperty, value); }
        }
        public static readonly DependencyProperty NdiNameProperty =
            DependencyProperty.Register("NdiName", typeof(String), typeof(NdiSendContainer), new PropertyMetadata("Unnamed - Fix Me.", OnNdiSenderPropertyChanged));


        [Category("NewTek NDI"),
        Description("NDI groups this sender will belong to. Optional.")]
        public List<String> NdiGroups
        {
            get { return (List<String>)GetValue(NdiGroupsProperty); }
            set { SetValue(NdiGroupsProperty, value); }
        }
        public static readonly DependencyProperty NdiGroupsProperty =
            DependencyProperty.Register("NdiGroups", typeof(List<String>), typeof(NdiSendContainer), new PropertyMetadata(new List<String>(), OnNdiSenderPropertyChanged));


        [Category("NewTek NDI"),
        Description("If clocked to video, NDI will rate limit drawing to the specified frame rate. Defaults to true.")]
        public bool NdiClockToVideo
        {
            get { return (bool)GetValue(NdiClockToVideoProperty); }
            set { SetValue(NdiClockToVideoProperty, value); }
        }
        public static readonly DependencyProperty NdiClockToVideoProperty =
            DependencyProperty.Register("NdiClockToVideo", typeof(bool), typeof(NdiSendContainer), new PropertyMetadata(true, OnNdiSenderPropertyChanged));

        [Category("NewTek NDI"),
        Description("True if some receiver has this source on program out.")]
        public bool IsOnProgram
        {
            get { return (bool)GetValue(IsOnProgramProperty); }
            set { SetValue(IsOnProgramProperty, value); }
        }
        public static readonly DependencyProperty IsOnProgramProperty =
            DependencyProperty.Register("IsOnProgram", typeof(bool), typeof(NdiSendContainer), new PropertyMetadata(false));

        [Category("NewTek NDI"),
        Description("True if some receiver has this source on preview out.")]
        public bool IsOnPreview
        {
            get { return (bool)GetValue(IsOnPreviewProperty); }
            set { SetValue(IsOnPreviewProperty, value); }
        }
        public static readonly DependencyProperty IsOnPreviewProperty =
            DependencyProperty.Register("IsOnPreview", typeof(bool), typeof(NdiSendContainer), new PropertyMetadata(false));


        [Category("NewTek NDI"),
        Description("If True, the send thread does not send, taking no CPU time.")]
        public bool IsSendPaused
        {
            get { return isPausedValue; }
            set
            {
                if (value != isPausedValue)
                {
                    isPausedValue = value;
                    NotifyPropertyChanged("IsSendPaused");
                } 
            }
        }

        [Category("NewTek NDI"),
        Description("If you need partial transparency, set this to true. If not, set to false and save some CPU cycles.")]
        public bool UnPremultiply
        {
            get { return unPremultiply; }
            set
            {
                if (value != unPremultiply)
                {
                    unPremultiply = value;
                    NotifyPropertyChanged("UnPremultiply");
                }
            }
        }

        static readonly TimeSpan LockTimeout = new TimeSpan(0, 0, 0, 0, 250);

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        DispatcherTimer mConnectionCheckTimer;

        public NdiSendContainer()
        {
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
                return;

            mConnectionCheckTimer = new DispatcherTimer(new TimeSpan(0, 0, 2), DispatcherPriority.Normal, OnConnectionCheck, Application.Current.Dispatcher);

            Start();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~NdiSendContainer() 
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        // tell the thread to exit
                        exitThread = true;

                        // wait for it to exit
                        if (sendThread != null)
                        {
                            sendThread.Join(250);
                            sendThread = null;
                        }

                        // cause the pulling of frames to fail
                        pendingFrames.CompleteAdding();

                        // clear any pending frames
                        while (pendingFrames.Count > 0)
                        {
                            FrameImage discardFrame = pendingFrames.Take();
                        }

                        pendingFrames.Dispose();
                    }

                    // Destroy the NDI sender
                    if (sendInstancePtr != IntPtr.Zero)
                    {
                        NDIlib.send_destroy(sendInstancePtr);

                        sendInstancePtr = IntPtr.Zero;
                    }

                    // Not required, but "correct". (see the SDK documentation)
                    NDIlib.destroy();
                }
            }
            catch
            {
            }
            finally
            {
                _disposed = true;
            }
        }

        public void Start()
        {
            try
            {
                // Not required, but "correct". (see the SDK documentation)
                if (!NDIlib.initialize())
                {
                    // Cannot run NDI. Most likely because the CPU is not sufficient (see SDK documentation).
                    // you can check this directly with a call to NDIlib.is_supported_CPU()
                    MessageBox.Show("Cannot run NDI");
                }
                else
                {
                    // start up a thread to receive on
                    sendThread = new Thread(SendThreadProc) { IsBackground = true, Name = "WpfNdiSendThread" };
                    sendThread.Start();

                    CompositionTarget.Rendering += OnCompositionTargetRendering;
                }
            }
            catch
            {
            }
        }

        public void Stop()
        {
            IsSendPaused = true;
        }


        private bool _disposed = false;

        private void OnConnectionCheck(object sender, EventArgs e)
        {
            if (IsOnProgram || IsOnPreview)
            {
                IsSendPaused = false;
            }
            else
            {
                IsSendPaused = true;
            }
        }

        static double xScalar = 1;
        static double yScalar = 1;

        private void GenerateFrame()
        {
            int xres = (int)(NdiWidth * xScalar);
            int yres = (int)(NdiHeight * yScalar);

            int xdpi = (int)(96 * xScalar);
            int ydpi = (int)(96 * yScalar);

            // sanity
            if (sendInstancePtr == IntPtr.Zero || xres < 8 || yres < 8)
                return;

            if (targetBitmap == null || targetBitmap.PixelWidth != xres || targetBitmap.PixelHeight != yres)
            {
                // Create a properly sized RenderTargetBitmap
                targetBitmap = new RenderTargetBitmap(xres, yres, xdpi, ydpi, PixelFormats.Pbgra32);
            }

            // clear to prevent trails
            targetBitmap.Clear();

            // render the content into the bitmap
            targetBitmap.Render(this.Content as Visual);

            pendingFrames.Add(new FrameImage(targetBitmap, NdiFrameRateNumerator, NdiFrameRateDenominator, NdiIntegerScale, NdiDropShadow));
        }


        TimeSpan mLastRenderTime;

        private void OnCompositionTargetRendering(object sender, EventArgs e)
        {
            RenderingEventArgs args = e as RenderingEventArgs;
            if (args != null)
            {
                if (mLastRenderTime == null || mLastRenderTime.Ticks < args.RenderingTime.Ticks)
                {
                    TimeSpan delta = args.RenderingTime - mLastRenderTime;
                    if (!IsSendPaused && delta.TotalMilliseconds >= ( 1000 / (NdiFrameRateNumerator / NdiFrameRateDenominator) ))
                    {
                        GenerateFrame();
                        mLastRenderTime = args.RenderingTime;
                    }
                }
            }
        }
        
        private static void OnNdiSenderPropertyChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            NdiSendContainer s = sender as NdiSendContainer;
            if (s != null)
                s.InitializeNdi();
        }
        
        private void InitializeNdi()
        {
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
                return;

            Monitor.Enter(sendInstanceLock);
            try
            {
                // we need a name
                if (String.IsNullOrEmpty(NdiName))
                    return;

                // re-initialize?
                if (sendInstancePtr != IntPtr.Zero)
                {
                    NDIlib.send_destroy(sendInstancePtr);
                    sendInstancePtr = IntPtr.Zero;
                }

                // .Net interop doesn't handle UTF-8 strings, so do it manually
                // These must be freed later
                IntPtr sourceNamePtr = UTF.StringToUtf8(NdiName);

                IntPtr groupsNamePtr = IntPtr.Zero;

                // build a comma separated list of groups?
                if (NdiGroups.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < NdiGroups.Count(); i++)
                    {
                        sb.Append(NdiGroups[i]);

                        if (i < NdiGroups.Count - 1)
                            sb.Append(',');
                    }

                    groupsNamePtr = UTF.StringToUtf8(sb.ToString());
                }

                // Create an NDI source description using sourceNamePtr and it's clocked to the video.
                NDIlib.send_create_t createDesc = new NDIlib.send_create_t()
                {
                    p_ndi_name = sourceNamePtr,
                    p_groups = groupsNamePtr,
                    clock_video = NdiClockToVideo,
                    clock_audio = false
                };

                // We create the NDI finder instance
                sendInstancePtr = NDIlib.send_create(ref createDesc);

                // free the strings we allocated
                Marshal.FreeHGlobal(sourceNamePtr);
                Marshal.FreeHGlobal(groupsNamePtr);
            }
            catch { }
            finally
            {
                // unlock
                Monitor.Exit(sendInstanceLock);
            }
        }

        // the receive thread runs though this loop until told to exit
        private void SendThreadProc()
        {
            // look for changes in tally
            bool lastProg = false;
            bool lastPrev = false;

            NDIlib.tally_t tally = new NDIlib.tally_t();
            tally.on_program = lastProg;
            tally.on_preview = lastPrev;

            while (!exitThread)
            {
                if (Monitor.TryEnter(sendInstanceLock, 250))
                {
                    // if this is not here, then we must be being reconfigured
                    if (sendInstancePtr == null)
                    {
                        // unlock
                        Monitor.Exit(sendInstanceLock);
                        
                        // give up some time
                        Thread.Sleep(20);
                        
                        // loop again
                        continue;
                    }

                    try
                    {
                        // get the next available frame
                        FrameImage frame;
                        if (pendingFrames.TryTake(out frame, 250))
                        {
                            // this dropps frames if the UI is rendernig ahead of the specified NDI frame rate
                            while (pendingFrames.Count > 1)
                            {
                                FrameImage discardFrame = pendingFrames.Take();
                            }

                            // We now perform image processing on this frame
                            byte[] processedFrame = frame.dropshadow ? ImageProcessing.FastDropShadow(frame.buffer, frame.width, frame.height, frame.stride) : frame.buffer;
                            processedFrame = ImageProcessing.ScaleBuffer(frame.buffer, frame.width, frame.height, frame.stride, frame.scalar);

                            // Now we build the NDI Frame
                            stride = (frame.width * frame.scalar * 32/*BGRA bpp*/ + 7) / 8;
                            bufferSize = (frame.height * frame.scalar) * stride;
                            aspectRatio = (float)frame.width / (float)frame.height;

                            // allocate some memory for a video buffer
                            IntPtr bufferPtr = Marshal.AllocHGlobal(bufferSize);

                            // We are going to create a progressive frame at 60Hz.
                            NDIlib.video_frame_v2_t ndiFrame = new NDIlib.video_frame_v2_t()
                            {
                                // Resolution
                                xres = frame.width * frame.scalar,
                                yres = frame.height * frame.scalar,
                                // Use BGRA video
                                FourCC = NDIlib.FourCC_type_e.FourCC_type_BGRA,
                                // The frame-rate
                                frame_rate_N = frame.frNum,
                                frame_rate_D = frame.frDen,
                                // The aspect ratio
                                picture_aspect_ratio = aspectRatio,
                                // This is a progressive frame
                                frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
                                // Timecode
                                timecode = NDIlib.send_timecode_synthesize,
                                // The video memory used for this frame
                                p_data = bufferPtr,
                                // The line to line stride of this image
                                line_stride_in_bytes = stride,
                                // no metadata
                                p_metadata = IntPtr.Zero,
                                // only valid on received frames
                                timestamp = 0
                            };

                            // copy the pixels into the buffer
                            Marshal.Copy(processedFrame, 0, bufferPtr, bufferSize);

                            // We now submit the frame. Note that this call will be clocked so that we end up submitting 
                            // at exactly the requested rate.
                            // If WPF can't keep up with what you requested of NDI, then it will be sent at the rate WPF is rendering.
                            if (!IsSendPaused)
                            {
                                NDIlib.send_send_video_v2(sendInstancePtr, ref ndiFrame);
                            }

                            // free the memory from this frame
                            Marshal.FreeHGlobal(bufferPtr);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        pendingFrames.CompleteAdding();
                    }
                    catch
                    {
                    }

                    // unlock
                    Monitor.Exit(sendInstanceLock);
                }
                else
                {
                    Thread.Sleep(20);
                }

                // check tally
                NDIlib.send_get_tally(sendInstancePtr, ref tally, 0);

                // if tally changed trigger an update
                if (lastProg != tally.on_program || lastPrev != tally.on_preview)
                {
                    // save the last values
                    lastProg = tally.on_program;
                    lastPrev = tally.on_preview;

                    // set these on the UI thread
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsOnProgram = lastProg;
                        IsOnPreview = lastPrev;
                    }));
                }
            }
        }

        private Object sendInstanceLock = new Object();
        private IntPtr sendInstancePtr = IntPtr.Zero;

        RenderTargetBitmap targetBitmap = null;

        private int stride;
        private int bufferSize;
        private float aspectRatio;

        // a thread to send frames on so that the UI isn't dragged down
        Thread sendThread = null;

        // a way to exit the thread safely
        bool exitThread = false;

        class FrameImage
        {
            public int width;
            public int height;
            public int stride;
            public int offset;
            public int frNum;
            public int frDen;
            public int scalar;
            public bool dropshadow;

            public byte[] buffer;

            public FrameImage(BitmapSource image, int srcfrNum, int srcfrDen, int scalar, bool dropshadow)
            {
                frNum = srcfrNum;
                frDen = srcfrDen;
                width = image.PixelWidth;
                height = image.PixelHeight;
                stride = width * 4;
                offset = 0;
                this.scalar = scalar;
                this.dropshadow = dropshadow;

                buffer = new byte[image.PixelHeight * image.PixelWidth * 4];
                image.CopyPixels(buffer, stride, offset);
            }

            public WriteableBitmap AsWriteableBitmap()
            {
                WriteableBitmap image = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
                image.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, offset);

                return image;
            }
        }

        // a thread safe collection to store pending frames
        BlockingCollection<FrameImage> pendingFrames = new BlockingCollection<FrameImage>();

        // used for pausing the send thread
        bool isPausedValue = true;

        // a safe value at the expense of CPU cycles
        bool unPremultiply = false;
    }
}
