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
        /// <summary>
        /// Returns the cached image for the given <see cref="ImageReference"/>,
        /// or the placeholder if the image is still being resolved in the
        /// background.  Boosts the reference to immediate priority so it is
        /// resolved as soon as possible.
        ///
        /// Note: This converter is used by bindings that have not yet been
        /// migrated to the path-through pattern (e.g. <c>{Binding Icon.ResolvedImage}</c>).
        /// Bindings using the path-through pattern do not need a converter.
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ImageReferenceService.Instance.RequestImage(value as ImageReference);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
