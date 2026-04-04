using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EmoTracker.UI.Controls
{
    public class InputMaskingImage : Image
    {
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            try
            {
                var source = (BitmapSource)Source;

                // Get the pixel of the source that was hit
                var x = Math.Min((int)(hitTestParameters.HitPoint.X / ActualWidth * source.PixelWidth), source.PixelWidth - 1);
                var y = Math.Min((int)(hitTestParameters.HitPoint.Y / ActualHeight * source.PixelHeight), source.PixelHeight - 1);

                // Copy the single pixel into a new byte array representing RGBA
                var pixel = new byte[4];
                source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);

                // Check the alpha (transparency) of the pixel
                // - threshold can be adjusted from 0 to 255
                if (pixel[3] < 10)
                    return null;

                return new PointHitTestResult(this, hitTestParameters.HitPoint);
            }
            catch
            {
                return null;
            }
        }
    }
}
