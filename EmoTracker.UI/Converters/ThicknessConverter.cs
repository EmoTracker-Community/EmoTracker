using EmoTracker.Core;
using System;
using System.Globalization;
using System.Windows.Data;

namespace EmoTracker.UI.Converters
{
    public class ThicknessConverter : Singleton<ThicknessConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                Data.Media.Thickness thickness = (Data.Media.Thickness)value;
                return new System.Windows.Thickness(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);
            }
            catch
            {
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
