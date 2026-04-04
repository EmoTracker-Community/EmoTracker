using EmoTracker.Core;
using System;
using System.Globalization;

#if WINDOWS
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
#else
using Avalonia.Data.Converters;
using Avalonia.Media;
#endif

namespace EmoTracker.UI.Converters
{
    public class InverseTransformConverter : Singleton<InverseTransformConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
#if WINDOWS
            Transform transform = value as Transform;
            if (transform == null)
                return Transform.Identity;
            return transform.Inverse;
#else
            if (value is ITransform avTransform)
            {
                var matrix = avTransform.Value;
                if (matrix.TryInvert(out var inverse))
                    return new MatrixTransform(inverse);
            }
            return null;
#endif
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
#if WINDOWS
            return DependencyProperty.UnsetValue;
#else
            throw new NotImplementedException();
#endif
        }
    }
}
