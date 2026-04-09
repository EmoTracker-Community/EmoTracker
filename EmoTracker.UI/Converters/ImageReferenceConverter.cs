using EmoTracker.Core;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media;
using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace EmoTracker.UI.Converters
{
    public class ImageReferenceConverter : Singleton<ImageReferenceConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ImageReferenceService.Instance.ResolveImageReference(value as ImageReference);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
