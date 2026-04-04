#if WINDOWS
using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace EmoTracker.UI.Controls
{
    public class MouseOnlyButton : Button
    {
        protected override void OnInitialized(EventArgs e)
        {
            IsTabStop = false;
            Focusable = false;
            base.OnInitialized(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
        }
    }

    public class MouseOnlyToggleButton : ToggleButton
    {
        protected override void OnInitialized(EventArgs e)
        {
            IsTabStop = false;
            Focusable = false;
            base.OnInitialized(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
        }
    }
}
#else
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
#endif
