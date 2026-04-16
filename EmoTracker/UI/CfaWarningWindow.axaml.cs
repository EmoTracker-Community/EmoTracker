using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EmoTracker.UI
{
    public partial class CfaWarningWindow : Window
    {
        public CfaWarningWindow()
        {
            InitializeComponent();
        }

        public CfaWarningWindow(string installPath) : this()
        {
            SubtitleText.Text = $"Install location: {installPath}";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
