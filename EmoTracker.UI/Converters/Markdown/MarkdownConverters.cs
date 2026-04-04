using EmoTracker.Core;
using System;
using System.Globalization;

#if WINDOWS
using System.Windows.Data;
#else
using Avalonia.Data.Converters;
#endif

namespace EmoTracker.UI.Converters.Markdown
{
#if WINDOWS
    public class MarkdownToFlowDocumentConverter : Singleton<MarkdownToFlowDocumentConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
                return MarkdownProcessor.AsFlowDocument(value.ToString());

            return MarkdownProcessor.AsFlowDocument(null);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
#endif
}
