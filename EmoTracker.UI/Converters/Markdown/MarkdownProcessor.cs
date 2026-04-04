using Markdig;
using System;

#if WINDOWS
using Markdig.Wpf;
using System.Windows.Documents;
#endif

namespace EmoTracker.UI.Converters.Markdown
{
    public class MarkdownProcessor
    {
#if WINDOWS
        public static FlowDocument AsFlowDocument(string markdown)
        {
            FlowDocument doc = Markdig.Wpf.Markdown.ToFlowDocument(markdown ?? "", new MarkdownPipelineBuilder().UseSupportedExtensions().Build());
            doc.FontSize = 10;
            return doc;
        }
#endif

        public static string AsHtml(string markdown)
        {
            if (!string.IsNullOrWhiteSpace(markdown))
                return Markdig.Markdown.ToHtml(markdown);
            else
                return null;
        }
    }
}
