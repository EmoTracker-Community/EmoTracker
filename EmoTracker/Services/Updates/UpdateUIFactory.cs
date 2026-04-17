using Avalonia.Controls;
using Avalonia.Threading;
using EmoTracker.UI;
using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.UI.Avalonia;
using System.Collections.Generic;

namespace EmoTracker.Services.Updates
{
    /// <summary>
    /// Custom <see cref="UIFactory"/> that substitutes EmoTracker-styled windows for
    /// the "Checking for updates", "Update available", and download-progress dialogs.
    /// </summary>
    public class UpdateUIFactory : UIFactory
    {
        public UpdateUIFactory() : base(null) { }

        public override ICheckingForUpdates ShowCheckingForUpdates()
        {
            var window = new EmoTracker.UI.CheckingForUpdatesWindow();
            Dispatcher.UIThread.Post(() => window.Show());
            return window;
        }

        public override void ShowVersionIsUpToDate()
        {
            Dispatcher.UIThread.Post(() => new ApplicationIsUpToDateWindow().Show());
        }

        public override IUpdateAvailable CreateUpdateAvailableWindow(
            List<AppCastItem> updates,
            ISignatureVerifier signatureVerifier,
            string currentVersion,
            string appName,
            bool isUpdateAlreadyDownloaded)
        {
            var window = new EmoTracker.UI.UpdateAvailableWindow(updates, currentVersion, appName);
            return window;
        }

        public override IDownloadProgress CreateProgressWindow(string downloadTitle, string actionButtonTitleAfterDownload)
        {
            return new EmoTracker.UI.UpdateDownloadWindow(downloadTitle, actionButtonTitleAfterDownload);
        }
    }
}
