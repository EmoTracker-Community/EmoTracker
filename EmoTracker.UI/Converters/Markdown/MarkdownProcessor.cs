using Markdig;
using Markdig.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;

namespace EmoTracker.UI.Converters.Markdown
{
    public class MarkdownProcessor
    {
        public static FlowDocument AsFlowDocument(string markdown)
        {
            FlowDocument doc = Markdig.Wpf.Markdown.ToFlowDocument(markdown ?? "", new MarkdownPipelineBuilder().UseSupportedExtensions().Build());
            doc.FontSize = 10;
            return doc;
        }

        public static string AsHtml(string markdown)
        {
            if (!string.IsNullOrWhiteSpace(markdown))
                return Markdig.Markdown.ToHtml(markdown);
            else
                return null;
        }
    }
}
