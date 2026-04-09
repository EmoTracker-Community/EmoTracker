using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace EmoTracker.UI.Controls
{
    public class MouseOnlyButton : Button
    {
        public MouseOnlyButton()
        {
            IsTabStop = false;
            Focusable = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
        }
    }

    public class MouseOnlyToggleButton : ToggleButton
    {
        public MouseOnlyToggleButton()
        {
            IsTabStop = false;
            Focusable = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
        }
    }
}
