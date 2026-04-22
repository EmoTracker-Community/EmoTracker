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
            return OnUIThread(() =>
            {
                var window = new EmoTracker.UI.CheckingForUpdatesWindow();
                window.Show(MainWindow);
                return window;
            });
        }

        public override void ShowVersionIsUpToDate()
        {
            Dispatcher.UIThread.Post(() => new ApplicationIsUpToDateWindow().Show(MainWindow));
        }

        public override IUpdateAvailable CreateUpdateAvailableWindow(
            List<AppCastItem> updates,
            ISignatureVerifier signatureVerifier,
            string currentVersion,
            string appName,
            bool isUpdateAlreadyDownloaded)
        {
            return OnUIThread(() => new EmoTracker.UI.UpdateAvailableWindow(updates, currentVersion, appName));
        }

        public override IDownloadProgress CreateProgressWindow(string downloadTitle, string actionButtonTitleAfterDownload)
        {
            return OnUIThread(() => new EmoTracker.UI.UpdateDownloadWindow(downloadTitle, actionButtonTitleAfterDownload));
        }

        internal static Window MainWindow =>
            (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

        // NetSparkle calls UIFactory methods from a background thread. On Windows,
        // Avalonia Window objects must be created on the UI thread or the Win32 backend
        // throws. On macOS the AppKit backend is more lenient, which is why updates
        // appeared to work there but not on Windows.
        private static T OnUIThread<T>(System.Func<T> func)
        {
            if (Dispatcher.UIThread.CheckAccess())
                return func();
            return Dispatcher.UIThread.InvokeAsync(func).GetAwaiter().GetResult();
        }
    }
}
