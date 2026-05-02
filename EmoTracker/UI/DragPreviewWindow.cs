using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EmoTracker.UI
{
    /// <summary>
    /// Tiny borderless window used as a "draggable tab preview" while the
    /// user is mid-drag of a tab from <see cref="StateTabStripControl"/>.
    /// Follows the cursor in screen space; closed when the drag ends
    /// (either dock or tear-off completes).
    ///
    /// <para>
    /// Visual: a small dark pill matching the tab strip's active-tab
    /// styling so the preview is recognizable as the thing the user
    /// grabbed. Opacity is dialed down a touch so the underlying drop
    /// targets (other windows, tab strips) remain visible through it.
    /// </para>
    ///
    /// <para>
    /// Constraints: <c>ShowInTaskbar=false</c> (this is transient UI),
    /// <c>SystemDecorations=None</c> (no chrome), <c>Topmost=true</c>
    /// so it floats above the source/target windows during the drag,
    /// <c>CanResize=false</c>.
    /// </para>
    /// </summary>
    internal sealed class DragPreviewWindow : Window
    {
        public DragPreviewWindow(string text)
        {
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            Opacity = 0.85;
            Focusable = false;

            var border = new Border
            {
                Padding = new Thickness(12, 4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            Content = border;
        }

        /// <summary>
        /// Position the preview just below-right of the cursor so it
        /// doesn't occlude the actual drop target the user is hovering.
        /// </summary>
        public void FollowCursor(PixelPoint screenPoint)
        {
            Position = new PixelPoint(screenPoint.X + 12, screenPoint.Y + 12);
        }
    }
}
