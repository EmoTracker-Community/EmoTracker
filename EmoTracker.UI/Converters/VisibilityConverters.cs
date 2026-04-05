using EmoTracker.Core;
using System;
using System.Globalization;

#if WINDOWS
using System.Windows.Data;
#else
using Avalonia.Data.Converters;
#endif

namespace EmoTracker.UI.Converters
{
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
}
