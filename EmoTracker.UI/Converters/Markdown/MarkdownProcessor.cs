using Markdig;
using System;


namespace EmoTracker.UI.Converters.Markdown
{
    public class MarkdownProcessor
    {

        public static string AsHtml(string markdown)
        {
            if (!string.IsNullOrWhiteSpace(markdown))
                return Markdig.Markdown.ToHtml(markdown);
            else
                return null;
        }
    }
}
