using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace EmoTracker.UI.Converters.Markdown
{
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
}
