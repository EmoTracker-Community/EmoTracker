#if WINDOWS
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

                var x = Math.Min((int)(hitTestParameters.HitPoint.X / ActualWidth * source.PixelWidth), source.PixelWidth - 1);
                var y = Math.Min((int)(hitTestParameters.HitPoint.Y / ActualHeight * source.PixelHeight), source.PixelHeight - 1);

                var pixel = new byte[4];
                source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);

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
#else
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EmoTracker.UI.Media.Utility;

namespace EmoTracker.UI.Controls
{
    // Avalonia version: per-pixel hit testing uses the precomputed alpha mask from IconUtility.
    // The correct HitTestCore override will be wired up in Phase 6 once the Avalonia control
    // hierarchy is fully understood. For now, pointer event filtering handles transparent areas.
    public class InputMaskingImage : Image
    {
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!HitTestAlphaMask(e.GetPosition(this)))
                return;
            base.OnPointerMoved(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!HitTestAlphaMask(e.GetPosition(this)))
                return;
            base.OnPointerPressed(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!HitTestAlphaMask(e.GetPosition(this)))
                return;
            base.OnPointerReleased(e);
        }

        private bool HitTestAlphaMask(Avalonia.Point point)
        {
            try
            {
                if (Source == null) return false;

                var maskEntry = IconUtility.GetAlphaMask(Source);
                if (maskEntry == null) return true;

                var (mask, maskW, maskH) = maskEntry.Value;

                int px = System.Math.Min((int)(point.X / Bounds.Width  * maskW), maskW - 1);
                int py = System.Math.Min((int)(point.Y / Bounds.Height * maskH), maskH - 1);

                if (px < 0 || py < 0) return false;
                return mask[py * maskW + px];
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
