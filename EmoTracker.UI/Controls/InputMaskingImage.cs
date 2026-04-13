using Avalonia.Controls;
using Avalonia.Rendering;
using EmoTracker.UI.Media.Utility;

namespace EmoTracker.UI.Controls
{
    // Avalonia version: per-pixel hit testing via ICustomHitTest so transparent pixels
    // are excluded from Avalonia's visual hit-test walk.  This prevents transparent
    // areas from blocking pointer events on elements below in z-order.
    public class InputMaskingImage : Image, ICustomHitTest
    {
        public bool HitTest(Avalonia.Point point)
        {
            return HitTestAlphaMask(point);
        }

        private bool HitTestAlphaMask(Avalonia.Point point)
        {
            try
            {
                if (Source == null) return false;

                var maskEntry = IconUtility.GetAlphaMask(Source);
                // No mask → treat as fully transparent (click-through).
                // This is critical for the async image pipeline: placeholder images
                // don't have alpha masks, and returning true here would make them
                // block ALL input until the real image loads.
                if (maskEntry == null) return false;

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
