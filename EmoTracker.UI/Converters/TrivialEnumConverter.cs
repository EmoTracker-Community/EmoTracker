using EmoTracker.Core;
using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
    public class TrivialEnumConverter : Singleton<TrivialEnumConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return Enum.Parse(targetType, value.ToString());
            }
            catch { }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
