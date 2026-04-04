// Avalonia code-only implementation of MarkdownViewer (net8.0 target).
// The WPF version lives in MarkdownViewer.xaml / MarkdownViewer.xaml.cs (net8.0-windows target).
using Avalonia;
using Avalonia.Controls;
using Markdown.Avalonia;

namespace EmoTracker.UI.Controls
{
    public class MarkdownViewer : UserControl
    {
        public static readonly StyledProperty<string> MarkdownProperty =
            AvaloniaProperty.Register<MarkdownViewer, string>(nameof(Markdown));

        private readonly MarkdownScrollViewer _viewer;

        public MarkdownViewer()
        {
            _viewer = new MarkdownScrollViewer
            {
                IsHitTestVisible = false
            };
            Content = _viewer;
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
                _viewer.Markdown = change.NewValue as string;
        }
    }
}
