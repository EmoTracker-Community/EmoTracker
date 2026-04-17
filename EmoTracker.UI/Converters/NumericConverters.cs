#nullable enable annotations
using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace EmoTracker.UI.Converters
{
    /// <summary>
    /// Returns <see cref="double.NaN"/> when the value is negative; otherwise returns the value as-is.
    /// Use for Width/Height bindings where -1 means "unset / auto".
    /// </summary>
    public class NegativeToNaNDoubleConverter : Singleton<NegativeToNaNDoubleConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d < 0 ? double.NaN : d;
            return double.NaN;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>0.0</c> when the value is negative; otherwise returns the value as-is.
    /// Use for MinWidth/MinHeight bindings where -1 means "no minimum" (platform default is 0).
    /// </summary>
    public class NegativeToZeroDoubleConverter : Singleton<NegativeToZeroDoubleConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d < 0 ? 0.0 : d;
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <see cref="double.PositiveInfinity"/> when the value is negative; otherwise returns the value as-is.
    /// Use for MaxWidth/MaxHeight bindings where -1 means "no maximum" (platform default is ∞).
    /// </summary>
    public class NegativeToInfinityDoubleConverter : Singleton<NegativeToInfinityDoubleConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d < 0 ? double.PositiveInfinity : d;
            return double.PositiveInfinity;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <see cref="AvaloniaProperty.UnsetValue"/> when the value is negative,
    /// causing the binding to fall back to the target property's default value.
    /// If a <c>ConverterParameter</c> is supplied, returns that as a <c>double</c> instead
    /// of <c>UnsetValue</c>, allowing callers to specify a fallback size explicitly.
    /// Use for IconWidth/IconHeight bindings where -1 means "use a sensible default"
    /// rather than NaN (auto-size to source dimensions — can be huge for banner images).
    /// </summary>
    public class NegativeToUnsetConverter : Singleton<NegativeToUnsetConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && d >= 0) return d;
            if (parameter != null
                && double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double fallback))
                return fallback;
            return AvaloniaProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>0.0</c> when the value is negative or zero; otherwise returns the value as-is.
    /// Use for Canvas.Left / Canvas.Top bindings where -1 means "default / unset".
    /// </summary>
    public class CanvasPositionConverter : Singleton<CanvasPositionConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d <= 0 ? 0.0 : d;
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>0</c> when the value is negative or zero; otherwise returns <c>(int)value</c>.
    /// Use for Canvas.ZIndex bindings where -1 means "default".
    /// </summary>
    public class CanvasZIndexConverter : Singleton<CanvasZIndexConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return d <= 0 ? 0 : (int)d;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Multi-value converter for icon dimensions. Supports three behaviours:
    /// <list type="bullet">
    ///   <item>Both dimensions specified → honor both (image may be distorted).</item>
    ///   <item>Only this dimension specified → use it directly.</item>
    ///   <item>Only the other dimension specified → compute this one proportionally from
    ///     the source image's aspect ratio.</item>
    ///   <item>Neither specified → fall back to the source image's natural pixel size.</item>
    /// </list>
    /// Use <c>ConverterParameter="Width"</c> or <c>"Height"</c> to select which dimension
    /// is being resolved.
    /// <para>values[0] = this dimension (IconWidth or IconHeight),
    ///       values[1] = other dimension (IconHeight or IconWidth),
    ///       values[2] = Image.Source (IImage, for aspect-ratio and pixel-size fallback).</para>
    /// </summary>
    public class IconDimensionMultiConverter : Singleton<IconDimensionMultiConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            double myDim    = values.Count > 0 && values[0] is double d0 ? d0 : double.NaN;
            double otherDim = values.Count > 1 && values[1] is double d1 ? d1 : double.NaN;
            bool isWidth = string.Equals(parameter?.ToString(), "Width", StringComparison.OrdinalIgnoreCase);
            Bitmap bitmap = values.Count > 2 ? values[2] as Bitmap : null;

            // If this dimension is explicitly specified (including zero), use it directly.
            if (!double.IsNaN(myDim) && myDim >= 0)
                return myDim;

            // Only the other dimension was specified: scale proportionally from the image aspect ratio.
            if (otherDim > 0 && !double.IsNaN(otherDim) && bitmap != null)
            {
                double pixW = bitmap.PixelSize.Width;
                double pixH = bitmap.PixelSize.Height;
                if (pixW > 0 && pixH > 0)
                    return isWidth ? otherDim * pixW / pixH : otherDim * pixH / pixW;
            }

            // Neither dimension specified: fall back to the image's natural pixel size.
            if (bitmap != null)
                return isWidth ? (double)bitmap.PixelSize.Width : (double)bitmap.PixelSize.Height;

            // Last resort default.
            return 32.0;
        }
    }
}
