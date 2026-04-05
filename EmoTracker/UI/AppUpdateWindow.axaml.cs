using Avalonia.Controls;
using System;

namespace EmoTracker.UI
{
    public partial class AppUpdateWindow : Window
    {
        private readonly bool _autoClose;

        public AppUpdateWindow(bool autoClose)
        {
            _autoClose = autoClose;
            InitializeComponent();

            Opened += (_, _) =>
            {
                // Update checking not available in cross-platform target yet (Phase 8)
                if (_autoClose)
                    Close();
            };
        }
    }
}
