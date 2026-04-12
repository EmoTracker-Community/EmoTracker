using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.ComponentModel;

namespace EmoTracker.Extensions.McpServer
{
    public partial class McpStatusIndicator : UserControl
    {
        static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
        static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#888888"));

        public McpStatusIndicator()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private McpServerExtension _extension;

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (_extension != null)
                _extension.PropertyChanged -= Extension_PropertyChanged;

            _extension = DataContext as McpServerExtension;

            if (_extension != null)
                _extension.PropertyChanged += Extension_PropertyChanged;

            UpdateIndicator();
        }

        private void Extension_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(McpServerExtension.Active))
                UpdateIndicator();
        }

        private void UpdateIndicator()
        {
            var ellipse = this.FindControl<Ellipse>("PART_Indicator");
            if (ellipse == null)
            {
                // Fall back: find the first Ellipse in the visual tree
                ellipse = this.GetVisualDescendant<Ellipse>();
            }
            if (ellipse != null && _extension != null)
                ellipse.Fill = _extension.Active ? ActiveBrush : InactiveBrush;
        }
    }

    internal static class VisualExtensions
    {
        public static T GetVisualDescendant<T>(this Avalonia.Visual visual) where T : class
        {
            if (visual is Avalonia.Controls.Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is T match)
                        return match;
                    if (child is Avalonia.Visual v)
                    {
                        var result = GetVisualDescendant<T>(v);
                        if (result != null) return result;
                    }
                }
            }
            else if (visual is Avalonia.Controls.Decorator decorator && decorator.Child != null)
            {
                if (decorator.Child is T match)
                    return match;
                if (decorator.Child is Avalonia.Visual v)
                    return GetVisualDescendant<T>(v);
            }
            else if (visual is ContentControl cc && cc.Content is Avalonia.Visual cv)
            {
                if (cv is T match)
                    return match;
                return GetVisualDescendant<T>(cv);
            }
            return null;
        }
    }
}
