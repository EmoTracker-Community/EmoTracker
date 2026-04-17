using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EmoTracker.UI
{
    public partial class AppTranslocationWarningWindow : Window
    {
        public AppTranslocationWarningWindow()
        {
            InitializeComponent();
        }

        public AppTranslocationWarningWindow(string translocatedPath) : this()
        {
            SubtitleText.Text = $"Running from: {translocatedPath}";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
