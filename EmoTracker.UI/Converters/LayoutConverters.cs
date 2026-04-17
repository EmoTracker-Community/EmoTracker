#nullable enable annotations
using EmoTracker.Core;
using EmoTracker.Data.Layout;
using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
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
    /// Maps <c>EmoTracker.Data.Layout.Orientation.Horizontal</c> → <see cref="PreserveDimension.Height"/>
    /// and <c>Vertical</c> → <see cref="PreserveDimension.Width"/>.
    /// When locations wrap horizontally their height should stay uniform; when they wrap
    /// vertically their width should stay uniform.
    /// </summary>
    public class OrientationToPreserveDimensionConverter : Singleton<OrientationToPreserveDimensionConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Orientation orientation)
            {
                return orientation == Orientation.Horizontal
                    ? PreserveDimension.Height
                    : PreserveDimension.Width;
            }
            return PreserveDimension.None;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Multi-value converter that selects the correct <see cref="ITemplate{Panel}"/>
    /// based on <see cref="PanelStyle"/> and <see cref="Orientation"/>.
    /// <para>values[0] = PanelStyle, values[1] = EmoTracker.Data.Layout.Orientation.</para>
    /// Returns a StackPanel template for Stack, WrapPanel template for Wrap.
    /// </summary>
    public class PanelStyleToTemplateConverter : Singleton<PanelStyleToTemplateConverter>, IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            var style = values.Count > 0 && values[0] is PanelStyle ps ? ps : PanelStyle.Stack;
            var orientation = Avalonia.Layout.Orientation.Vertical;
            if (values.Count > 1 && values[1] is Orientation dataOrientation
                && dataOrientation == Orientation.Horizontal)
            {
                orientation = Avalonia.Layout.Orientation.Horizontal;
            }

            if (style == PanelStyle.Wrap)
                return new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = orientation });
            else
                return new FuncTemplate<Panel?>(() => new StackPanel { Orientation = orientation });
        }
    }
}
