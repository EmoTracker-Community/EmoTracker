using EmoTracker.Core;
using System;
using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
    /// <summary>
    /// Converts a <see cref="Data.Media.Thickness"/> model value to the platform Thickness type.
    /// Used by LocationMapControl and similar controls that bind a structured Thickness model.
    /// </summary>
    public class ThicknessConverter : Singleton<ThicknessConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                Data.Media.Thickness thickness = (Data.Media.Thickness)value;
                return new Thickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
            }
            catch { }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a margin string (e.g. "5", "5,10", "5,10,5,10") to an Avalonia
    /// <see cref="Thickness"/>.
    /// <para>
    /// Avalonia's TypeConverter is only applied during XAML literal parsing, not at binding
    /// resolution time, so a <c>{Binding Margin}</c> where the source is a <c>string</c>
    /// silently falls back to <c>Thickness(0)</c> without this explicit converter.
    /// </para>
    /// </summary>
    public class StringToThicknessConverter : Singleton<StringToThicknessConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try { return Thickness.Parse(s); }
                catch { }
            }
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
