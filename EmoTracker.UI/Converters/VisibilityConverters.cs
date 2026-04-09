#nullable enable annotations
using EmoTracker.Core;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace EmoTracker.UI.Converters
{
    /// <summary>
    /// Returns <c>true</c> when the value is null or unset, <c>false</c> when non-null.
    /// Use for IsVisible bindings where the element should appear when no data is present.
    /// </summary>
    public class NullToTrueConverter : Singleton<NullToTrueConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null || value == AvaloniaProperty.UnsetValue || value == BindingOperations.DoNothing;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the value is non-null and non-unset, <c>false</c> otherwise.
    /// </summary>
    public class NullToFalseConverter : Singleton<NullToFalseConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && value != AvaloniaProperty.UnsetValue && value != BindingOperations.DoNothing;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the value is a non-null, non-empty string.
    /// Treats UnsetValue and DoNothing as empty on Avalonia.
    /// </summary>
    public class NonEmptyStringToBoolConverter : Singleton<NonEmptyStringToBoolConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && s.Length > 0;

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
            return new Avalonia.Thickness(d);
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
                    return Brush.Parse(s);
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
                return Brush.Parse(colorStr);
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

    /// <summary>
    /// Returns <see cref="Avalonia.AvaloniaProperty.UnsetValue"/> when the value is negative,
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
            return Avalonia.AvaloniaProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns <c>true</c> when the value's <c>ToString()</c> matches the <c>ConverterParameter</c>
    /// string (case-insensitive).  Useful for controlling IsVisible based on an enum property.
    /// <example><c>IsVisible="{Binding Style, Converter={x:Static converters:ObjectEqualsConverter.Instance}, ConverterParameter=Settings}"</c></example>
    /// </summary>
    public class ObjectEqualsConverter : Singleton<ObjectEqualsConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Maps <c>EmoTracker.Data.Layout.Orientation.Horizontal</c> → <see cref="EmoTracker.UI.PreserveDimension.Height"/>
    /// and <c>Vertical</c> → <see cref="EmoTracker.UI.PreserveDimension.Width"/>.
    /// When locations wrap horizontally their height should stay uniform; when they wrap
    /// vertically their width should stay uniform.
    /// </summary>
    public class OrientationToPreserveDimensionConverter : Singleton<OrientationToPreserveDimensionConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EmoTracker.Data.Layout.Orientation orientation)
            {
                return orientation == EmoTracker.Data.Layout.Orientation.Horizontal
                    ? PreserveDimension.Height
                    : PreserveDimension.Width;
            }
            return PreserveDimension.None;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

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

    /// <summary>
    /// Multi-value converter for icon dimensions. Returns the explicit dimension when it is
    /// a valid positive number; otherwise falls back to the pixel dimensions of the bound
    /// <see cref="Bitmap"/> source image. Use <c>ConverterParameter="Width"</c> or
    /// <c>"Height"</c> to select which pixel dimension to read.
    /// <para>values[0] = dimension (double, e.g. IconWidth), values[1] = Image.Source (IImage).</para>
    /// </summary>
    public class IconDimensionMultiConverter : Singleton<IconDimensionMultiConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            double dimension = values.Count > 0 && values[0] is double d ? d : double.NaN;

            // If the layout specifies a valid positive size, use it directly.
            if (dimension > 0 && !double.IsNaN(dimension))
                return dimension;

            // Fall back to the source image's pixel dimensions.
            if (values.Count > 1 && values[1] is Bitmap bitmap)
            {
                bool useWidth = string.Equals(parameter?.ToString(), "Width", StringComparison.OrdinalIgnoreCase);
                return (double)(useWidth ? bitmap.PixelSize.Width : bitmap.PixelSize.Height);
            }

            // Last resort default.
            return 32.0;
        }
    }

    /// <summary>
    /// Multi-value converter that replicates WPF's MultiDataTrigger-based map location
    /// visibility logic. Evaluates (in priority order): ForceInvisible, ForceVisible,
    /// HasVisibleSections, Cleared+DisplayAll+Shift, Empty+DisplayAll+Shift.
    /// <para>Binding order:
    /// [0] ForceVisible (bool), [1] ForceInvisible (bool),
    /// [2] Location.HasVisibleSections (bool), [3] Location.AccessibilityLevel (enum),
    /// [4] Location.HasAvailableItems (bool), [5] Location.Badges.Count (int),
    /// [6] Location.NoteTakingSite.Empty (bool),
    /// [7] DisplayAllLocations (bool), [8] IsShiftPressed (bool).</para>
    /// </summary>
    public class MapLocationVisibilityConverter : Singleton<MapLocationVisibilityConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count < 9) return true;

            bool forceVisible       = values[0] is true;
            bool forceInvisible     = values[1] is true;
            bool hasVisibleSections = values[2] is true;
            bool isCleared          = values[3] is AccessibilityLevel level
                                      && level == AccessibilityLevel.Cleared;
            bool hasAvailableItems  = values[4] is true;
            int  badgeCount         = values[5] is int bc ? bc : 0;
            bool notesEmpty         = values[6] is not false; // default true when null/unset
            bool displayAll         = values[7] is true;
            bool shiftPressed       = values[8] is true;

            // Highest priority: script-driven force rules (last WPF triggers win)
            if (forceInvisible) return false;
            if (forceVisible)   return true;

            // No visible sections at all → hide
            if (!hasVisibleSections) return false;

            // Cleared location hidden unless DisplayAll or Shift
            if (isCleared && !displayAll && !shiftPressed) return false;

            // Empty location (no items, badges, or notes) hidden unless DisplayAll or Shift
            if (!hasAvailableItems && badgeCount == 0 && notesEmpty && !displayAll && !shiftPressed)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Multi-value converter that selects the correct <see cref="ITemplate{Panel}"/>
    /// based on <see cref="PanelStyle"/> and <see cref="EmoTracker.Data.Layout.Orientation"/>.
    /// <para>values[0] = PanelStyle, values[1] = EmoTracker.Data.Layout.Orientation.</para>
    /// Returns a StackPanel template for Stack, WrapPanel template for Wrap.
    /// </summary>
    public class PanelStyleToTemplateConverter : Singleton<PanelStyleToTemplateConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            var style = values.Count > 0 && values[0] is PanelStyle ps ? ps : PanelStyle.Stack;
            var orientation = Avalonia.Layout.Orientation.Vertical;
            if (values.Count > 1 && values[1] is EmoTracker.Data.Layout.Orientation dataOrientation
                && dataOrientation == EmoTracker.Data.Layout.Orientation.Horizontal)
            {
                orientation = Avalonia.Layout.Orientation.Horizontal;
            }

            if (style == PanelStyle.Wrap)
                return new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = orientation });
            else
                return new FuncTemplate<Panel?>(() => new StackPanel { Orientation = orientation });
        }
    }

    /// <summary>
    /// Multi-value converter for the Package Manager button foreground color.
    /// Replicates WPF DataTrigger priority: !AnyPackagesInstalled → Active,
    /// CurrentPackageHasUpdateAvailable → Warning, UpdatesAvailable → Active, else default gray.
    /// <para>values[0] = UpdatesAvailable (bool), values[1] = CurrentPackageHasUpdateAvailable (bool),
    /// values[2] = AnyPackagesInstalled (bool).</para>
    /// </summary>
    public class PackageManagerForegroundConverter : Singleton<PackageManagerForegroundConverter>, IMultiValueConverter
    {
        private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#717171"));

        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            bool updatesAvailable = values.Count > 0 && values[0] is true;
            bool currentHasUpdate = values.Count > 1 && values[1] is true;
            bool anyInstalled     = values.Count > 2 && values[2] is true;

            // WPF trigger priority: last matching trigger wins.
            // Order: UpdatesAvailable, CurrentPackageHasUpdateAvailable, !AnyPackagesInstalled
            if (!anyInstalled)
                return new SolidColorBrush(Color.Parse(
                    EmoTracker.Data.Settings.ApplicationColors.Instance.Status_Generic_Active));
            if (currentHasUpdate)
                return new SolidColorBrush(Color.Parse(
                    EmoTracker.Data.Settings.ApplicationColors.Instance.Status_Generic_Warning));
            if (updatesAvailable)
                return new SolidColorBrush(Color.Parse(
                    EmoTracker.Data.Settings.ApplicationColors.Instance.Status_Generic_Active));

            return DefaultBrush;
        }
    }

    /// <summary>
    /// Multi-value converter for the Package Manager button tooltip.
    /// <para>values[0] = UpdatesAvailable, values[1] = CurrentPackageHasUpdateAvailable,
    /// values[2] = AnyPackagesInstalled.</para>
    /// </summary>
    public class PackageManagerTooltipConverter : Singleton<PackageManagerTooltipConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            bool updatesAvailable = values.Count > 0 && values[0] is true;
            bool currentHasUpdate = values.Count > 1 && values[1] is true;
            bool anyInstalled     = values.Count > 2 && values[2] is true;

            if (!anyInstalled) return "Install your first package!";
            if (currentHasUpdate || updatesAvailable) return "Package Updates Are Available";
            return "Package Manager";
        }
    }
}
