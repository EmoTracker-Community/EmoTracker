using EmoTracker.Core;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EmoTracker.UI.Converters
{
    public class InverseTransformConverter : Singleton<InverseTransformConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            Transform transform = value as Transform;
            if (transform == null)
                return Transform.Identity;
            return transform.Inverse;
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
