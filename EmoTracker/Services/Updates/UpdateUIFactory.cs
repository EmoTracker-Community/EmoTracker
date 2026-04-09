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
    /// the "Checking for updates" and "Update available" dialogs while inheriting the
    /// built-in download-progress and error windows from the base class.
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
    }
}
