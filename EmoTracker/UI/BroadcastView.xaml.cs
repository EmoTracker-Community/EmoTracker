using EmoTracker.Data;
using EmoTracker.Extensions;
using EmoTracker.Extensions.NDI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for Streamer_View.xaml
    /// </summary>
    public partial class BroadcastView : Window
    {
        public BroadcastView()
        {
            InitializeComponent();

            NDIHost.PropertyChanged += NDIHost_PropertyChanged;
            UpdateNDIExtensionStatus();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                ApplicationModel.Instance.RefreshCommand.Execute(null);
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        bool mbClosed = false;

        protected override void OnClosing(CancelEventArgs e)
        {
            mbClosed = true;

            NDIHost.Stop();
            NDIHost.Dispose();
            UpdateNDIExtensionStatus();
            base.OnClosing(e);
        }

        private void NDIHost_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateNDIExtensionStatus();
        }

        private void UpdateNDIExtensionStatus()
        {
            var extension = ExtensionManager.Instance.FindExtension<NDIExtension>();
            if (extension != null)
            {
                extension.Active = !mbClosed && !NDIHost.IsSendPaused;
            }
        }

        public void Flush()
        {
            TranslateTransform xform = new TranslateTransform();

            HostedLayout.RenderTransform = xform;
            HostedLayout.RenderTransformOrigin = new Point(0.5, 0.5);

            var easing = new PowerEase() { EasingMode = EasingMode.EaseInOut, Power = 2.0 }; // SineEase();

            DoubleAnimationUsingKeyFrames dx = new DoubleAnimationUsingKeyFrames();
            dx.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0), easing));
            dx.KeyFrames.Add(new EasingDoubleKeyFrame(-10, KeyTime.FromPercent(0.1), easing));
            dx.KeyFrames.Add(new EasingDoubleKeyFrame(Height + 50, KeyTime.FromPercent(0.2), easing));
            dx.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0), easing));
            dx.Duration = new Duration(new TimeSpan(0, 0, 3));

            xform.BeginAnimation(TranslateTransform.YProperty, dx, HandoffBehavior.SnapshotAndReplace);

            try
            {
                using (SoundPlayer player = new SoundPlayer(Application.GetResourceStream(new Uri("Resources/flush.wav", UriKind.Relative)).Stream))
                {
                    player.Play();
                }
            }
            catch
            {
            }

            //xform.BeginAnimation(SkewTransform.AngleYProperty, dy);
        }

        public void Rain(ImageSource image)
        {
            try
            {
                Random rng = new Random();

                List<Image> drops = new List<Image>();
                for (int i = 0; i < 100; ++i)
                {
                    Image drop = new Image() { Source = image };
                    drop.Width = drop.Height = 32;

                    double height = drop.Height;

                    Canvas.SetLeft(drop, rng.Next() % OverlayCanvas.ActualWidth);

                    TranslateTransform xform = new TranslateTransform();

                    drop.RenderTransform = xform;
                    drop.RenderTransformOrigin = new Point(0.5, 0.5);

                    var easing = new PowerEase() { EasingMode = EasingMode.EaseInOut, Power = 2.0 }; // SineEase();

                    int startTime = rng.Next() % 5000;
                    int endTime = startTime + 1800;

                    DoubleAnimationUsingKeyFrames dx = new DoubleAnimationUsingKeyFrames();
                    dx.KeyFrames.Add(new EasingDoubleKeyFrame(-height, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, 0)), easing));
                    dx.KeyFrames.Add(new EasingDoubleKeyFrame(-height, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, startTime)), easing));
                    dx.KeyFrames.Add(new EasingDoubleKeyFrame(Height + height, KeyTime.FromTimeSpan(new TimeSpan(0, 0, 0, 0, endTime)), easing));
                    dx.Duration = new Duration(new TimeSpan(0, 0, 0, 0, endTime));

                    OverlayCanvas.Children.Add(drop);

                    dx.Completed += (object sender, EventArgs e) =>
                    {
                        try
                        {
                            OverlayCanvas.Children.Remove(drop);
                        }
                        catch
                        {
                        }
                    };

                    xform.BeginAnimation(TranslateTransform.YProperty, dx, HandoffBehavior.SnapshotAndReplace);
                }

                using (SoundPlayer player = new SoundPlayer(Application.GetResourceStream(new Uri("Resources/rain.wav", UriKind.Relative)).Stream))
                {
                    player.Play();
                }
            }
            catch
            {
            }
        }
    }
}
