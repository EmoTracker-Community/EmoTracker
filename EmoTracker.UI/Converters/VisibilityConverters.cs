using EmoTracker.Core;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Settings;
using System;
using System.Globalization;

#if WINDOWS
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
#else
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
#endif

namespace EmoTracker.UI.Converters
{
    /// <summary>
    /// Returns <c>true</c> when the value is null, <c>false</c> when non-null.
    /// Use for IsVisible bindings where the element should appear when no data is present.
    /// </summary>
    public class NullToTrueConverter : Singleton<NullToTrueConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the value is non-null, <c>false</c> when null.
    /// </summary>
    public class NullToFalseConverter : Singleton<NullToFalseConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the integer/uint value is non-zero.
    /// </summary>
    public class NonZeroToBoolConverter : Singleton<NonZeroToBoolConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i) return i != 0;
            if (value is uint u) return u != 0;
            if (value is long l) return l != 0;
            if (value is double d) return d != 0;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a dock location string ("left","right","top","bottom") to a <see cref="Dock"/> enum value.
    /// Returns <see cref="Dock.Left"/> for null/empty/unrecognised strings.
    /// </summary>
    public class StringToDockConverter : Singleton<StringToDockConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s) &&
                Enum.TryParse<Dock>(s, ignoreCase: true, out var result))
                return result;
            return Dock.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

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
    /// Converts a <see cref="double"/> to a uniform <c>Thickness</c>.
    /// Use for BorderThickness bindings where the data model stores a single double.
    /// </summary>
    public class DoubleToThicknessConverter : Singleton<DoubleToThicknessConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double d = value is double dv ? dv : 0.0;
#if WINDOWS
            return new System.Windows.Thickness(d);
#else
            return new Avalonia.Thickness(d);
#endif
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a colour name or hex string (e.g. "#ff3030", "DarkOrange") to an <see cref="IBrush"/>.
    /// Returns <c>null</c> on failure so that FallbackValue can kick in.
    /// </summary>
    public class StringToBrushConverter : Singleton<StringToBrushConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
#if WINDOWS
                    return (Brush)new BrushConverter().ConvertFromString(s);
#else
                    return Brush.Parse(s);
#endif
                }
                catch { }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts an <see cref="AccessibilityLevel"/> to the matching <see cref="IBrush"/> colour
    /// taken from <see cref="ApplicationColors.Instance"/>.
    /// </summary>
    public class AccessibilityLevelToBrushConverter : Singleton<AccessibilityLevelToBrushConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string colorStr = "#333333";
            if (value is AccessibilityLevel level)
            {
                var c = ApplicationColors.Instance;
                colorStr = level switch
                {
                    AccessibilityLevel.Normal       => c.AccessibilityColor_Normal,
                    AccessibilityLevel.Cleared      => c.AccessibilityColor_Cleared,
                    AccessibilityLevel.None         => c.AccessibilityColor_None,
                    AccessibilityLevel.Partial      => c.AccessibilityColor_Partial,
                    AccessibilityLevel.Inspect      => c.AccessibilityColor_Inspect,
                    AccessibilityLevel.SequenceBreak => c.AccessibilityColor_SequenceBreak,
                    AccessibilityLevel.Glitch       => c.AccessibilityColor_Glitch,
                    AccessibilityLevel.Unlockable   => c.AccessibilityColor_Unlockable,
                    _                               => "#333333"
                };
            }

            try
            {
#if WINDOWS
                return (Brush)new BrushConverter().ConvertFromString(colorStr);
#else
                return Brush.Parse(colorStr);
#endif
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Inverts a <see cref="bool"/> value.
    /// </summary>
    public class BoolInverseConverter : Singleton<BoolInverseConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>
    /// Alias for <see cref="BoolInverseConverter"/> — inverts a <see cref="bool"/> value.
    /// </summary>
    public class InverseBoolConverter : Singleton<InverseBoolConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

#if !WINDOWS
    /// <summary>
    /// Converts a <c>bool</c> to an Avalonia <see cref="Avalonia.Media.DropShadowDirectionEffect"/>
    /// when <c>true</c>, or <c>null</c> when <c>false</c>.
    /// Mirrors the WPF DropShadowEffect (BlurRadius=15, ShadowDepth=0, Opacity=0.8) which produces
    /// a centred glow with no directional offset.
    /// </summary>
    public class BoolToDropShadowEffectConverter : Singleton<BoolToDropShadowEffectConverter>, IValueConverter
    {
        private static readonly Avalonia.Media.DropShadowDirectionEffect s_effect =
            new Avalonia.Media.DropShadowDirectionEffect
            {
                BlurRadius  = 15,
                ShadowDepth = 0,
                Opacity     = 0.8,
                Color       = Colors.Black,
            };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? (object)s_effect : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
#endif
}
