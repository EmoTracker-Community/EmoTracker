using EmoTracker.Core;
using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EmoTracker.UI.Converters
{
    public class InverseTransformConverter : Singleton<InverseTransformConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ITransform avTransform)
            {
                var matrix = avTransform.Value;
                if (matrix.TryInvert(out var inverse))
                    return new MatrixTransform(inverse);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
