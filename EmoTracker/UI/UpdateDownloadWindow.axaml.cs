using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace EmoTracker.UI
{
    public partial class UpdateDownloadWindow : Window, IDownloadProgress
    {
        public event DownloadInstallEventHandler DownloadProcessCompleted;

        private readonly string _actionButtonTitle;
        private bool _isDownloadComplete;

        public UpdateDownloadWindow() : this(string.Empty, string.Empty) { }

        public UpdateDownloadWindow(string downloadTitle, string actionButtonTitle)
        {
            InitializeComponent();
            _actionButtonTitle = actionButtonTitle;
            StatusText.Text = downloadTitle;
            ActionButton.Content = actionButtonTitle;
        }

        public new void Show()
        {
            base.Show(Services.Updates.UpdateUIFactory.MainWindow);
        }

        public void OnDownloadProgressChanged(object sender, ItemDownloadProgressEventArgs args)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DownloadProgress.Value = args.ProgressPercentage;

                if (args.TotalBytesToReceive > 0)
                {
                    double mb = args.BytesReceived / 1_048_576.0;
                    double totalMb = args.TotalBytesToReceive / 1_048_576.0;
                    StatusText.Text = $"Downloading update… {mb:F1} / {totalMb:F1} MB";
                }
                else
                {
                    StatusText.Text = "Downloading update…";
                }
            });
        }

        public void FinishedDownloadingFile(bool isInstallFileValid)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isDownloadComplete = true;
                DownloadProgress.Value = 100;

                if (isInstallFileValid)
                {
                    StatusText.Text = "Download complete.";
                    ActionButton.IsVisible = true;
                    CancelButton.Content = "Later";
                }
                else
                {
                    StatusText.Text = "Download failed — the file could not be verified.";
                    CancelButton.Content = "Close";
                }
            });
        }

        public bool DisplayErrorMessage(string errorMessage)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = errorMessage;
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 0;
                CancelButton.Content = "Close";
            });
            return true;
        }

        public void SetDownloadAndInstallButtonEnabled(bool shouldBeEnabled)
        {
            Dispatcher.UIThread.Post(() => ActionButton.IsEnabled = shouldBeEnabled);
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            ActionButton.IsEnabled = false;
            DownloadProcessCompleted?.Invoke(this, new DownloadInstallEventArgs(true));
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadComplete)
            {
                DownloadProcessCompleted?.Invoke(this, new DownloadInstallEventArgs(false));
            }
            else
            {
                DownloadProcessCompleted?.Invoke(this, new DownloadInstallEventArgs(false));
            }
            Close();
        }
    }
}
