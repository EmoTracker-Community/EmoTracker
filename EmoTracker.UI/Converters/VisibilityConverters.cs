#nullable enable annotations
using EmoTracker.Core;
using System;
using System.Globalization;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

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

}
