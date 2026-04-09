// Avalonia code-only implementation of MarkdownViewer (net8.0 target).
// Uses Markdig to convert markdown → HTML, then renders via HtmlLabel
// (Avalonia.HtmlRenderer). Replaces the Markdown.Avalonia implementation
// which is incompatible with Avalonia 11.3.3.
// The WPF version lives in MarkdownViewer.xaml / MarkdownViewer.xaml.cs (net8.0-windows target).
using Avalonia;
using Avalonia.Controls;
using Markdig;
using TheArtOfDev.HtmlRenderer.Avalonia;

namespace EmoTracker.UI.Controls
{
    public class MarkdownViewer : UserControl
    {
        public static readonly StyledProperty<string> MarkdownProperty =
            AvaloniaProperty.Register<MarkdownViewer, string>(nameof(Markdown));

        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        private readonly HtmlLabel _label;

        public MarkdownViewer()
        {
            _label = new HtmlLabel
            {
                Background = Avalonia.Media.Brushes.Transparent,
                IsHitTestVisible = false,
                AutoSizeHeightOnly = true,
            };
            Content = _label;
        }

        public string Markdown
        {
            get => GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == MarkdownProperty)
            {
                string md = change.NewValue as string ?? string.Empty;
                _label.Text = string.IsNullOrWhiteSpace(md)
                    ? string.Empty
                    : WrapWithCss(Markdig.Markdown.ToHtml(md, Pipeline));
            }
        }

        private static string WrapWithCss(string html) =>
            "<html><head><style>" +
            "body { background-color: transparent; color: #c8c8c8; font-family: -apple-system, BlinkMacSystemFont, sans-serif; font-size: 12px; line-height: 1.55; margin: 2px 0; word-wrap: break-word; overflow-wrap: break-word; }" +
            "h1, h2 { color: #f0f0f0; font-size: 14px; font-weight: 600; margin: 10px 0 4px 0; }" +
            "h3, h4 { color: #e0e0e0; font-size: 12px; font-weight: 600; margin: 7px 0 3px 0; }" +
            "h1:first-child, h2:first-child, h3:first-child, h4:first-child { margin-top: 0; }" +
            "a { color: #7eb5d6; word-break: break-all; }" +
            "p { margin: 0 0 6px 0; }" +
            "code { background-color: #2d2d2d; color: #e0e0e0; padding: 1px 4px; border-radius: 3px; font-size: 11px; }" +
            "pre { background-color: #111111; padding: 8px; border-radius: 3px; overflow: hidden; margin: 6px 0; }" +
            "pre code { background-color: transparent; padding: 0; }" +
            "ul, ol { margin: 2px 0 6px 0; padding-left: 16px; }" +
            "li { margin-bottom: 2px; }" +
            "strong { color: #e8e8e8; }" +
            "blockquote { border-left: 2px solid #444; margin: 4px 0; padding: 2px 8px; color: #909090; }" +
            "</style></head><body>" + html + "</body></html>";
    }
}
