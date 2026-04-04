using EmoTracker.Core;
using System;
using System.Globalization;

#if WINDOWS
using System.Windows.Data;
#else
using Avalonia;
using Avalonia.Data.Converters;
#endif

namespace EmoTracker.UI.Converters
{
    public class ThicknessConverter : Singleton<ThicknessConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                Data.Media.Thickness thickness = (Data.Media.Thickness)value;
#if WINDOWS
                return new System.Windows.Thickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
#else
                return new Thickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
#endif
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
