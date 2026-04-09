using Avalonia.Controls;
using Avalonia.Interactivity;
using NetSparkleUpdater.Interfaces;
using System;

namespace EmoTracker.UI
{
    public partial class EmoCheckingForUpdatesWindow : Window, ICheckingForUpdates
    {
        public event EventHandler UpdatesUIClosing;

        public EmoCheckingForUpdatesWindow()
        {
            InitializeComponent();
        }

        public new void Show()
        {
            base.Show();
        }

        public new void Close()
        {
            UpdatesUIClosing?.Invoke(this, EventArgs.Empty);
            base.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
