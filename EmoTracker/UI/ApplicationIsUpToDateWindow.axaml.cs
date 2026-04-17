using Avalonia.Controls;
using Avalonia.Interactivity;
using EmoTracker.Core;

namespace EmoTracker.UI
{
    public partial class ApplicationIsUpToDateWindow : Window
    {
        public ApplicationIsUpToDateWindow()
        {
            InitializeComponent();
            SubtitleText.Text = $"You are running the latest version of EmoTracker ({ApplicationVersion.Current}).";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
