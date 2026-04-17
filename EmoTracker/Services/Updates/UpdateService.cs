using EmoTracker.Data;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using Serilog;
using System;
using System.Threading.Tasks;

namespace EmoTracker.Services.Updates
{
    /// <summary>
    /// A <see cref="SparkleUpdater"/> subclass that overrides
    /// <see cref="RunDownloadedInstaller"/> to use the cross-platform
    /// zip/tar extraction and swap-script strategy.
    /// </summary>
    internal class EmoTrackerSparkleUpdater : SparkleUpdater
    {
        public EmoTrackerSparkleUpdater(string appcastUrl, Ed25519Checker signatureVerifier)
            : base(appcastUrl, signatureVerifier)
        {
        }

        protected override async Task RunDownloadedInstaller(string downloadFilePath)
        {
            await ZipInstallAndRelaunch.RunDownloadedInstallerAsync(downloadFilePath);
        }
    }

    /// <summary>
    /// Owns and configures the <see cref="SparkleUpdater"/> instance and exposes a
    /// simple API for the rest of the app to trigger update checks.
    ///
    /// The appcast URL is a placeholder — <see cref="GitHubAppCastDataDownloader"/>
    /// ignores it and talks directly to the GitHub Releases API.
    /// </summary>
    public class UpdateService : IDisposable
    {
        private const string RepoOwner  = "EmoTracker-Community";
        private const string RepoName   = "EmoTracker";
        private const string AppCastUrl = $"https://github.com/{RepoOwner}/{RepoName}";

        private readonly EmoTrackerSparkleUpdater _sparkle;
        private bool _disposed;

        public static UpdateService Instance { get; } = new();

        private UpdateService()
        {
            // Signature verification is disabled initially (Unsafe mode).
            // Enable once Ed25519 keys are configured in the release workflow.
            var signatureVerifier = new Ed25519Checker(SecurityMode.Unsafe, null);

            var uiFactory = new UpdateUIFactory();

            _sparkle = new EmoTrackerSparkleUpdater(AppCastUrl, signatureVerifier)
            {
                UIFactory             = uiFactory,
                AppCastDataDownloader = new GitHubAppCastDataDownloader(RepoOwner, RepoName),
                RelaunchAfterUpdate   = true,
            };

            Log.Debug("[Update] AppCastUrl: {Url}", _sparkle.AppCastUrl);

            _sparkle.UpdateDetected      += (_, info) =>
                Log.Information("[Update] Update available: {Version}", info.LatestVersion);
            _sparkle.UpdateCheckFinished += (_, status) =>
                Log.Debug("[Update] Update check finished. Status: {Status}", status);
            _sparkle.DownloadStarted     += (_, _) =>
                Log.Information("[Update] Download started.");
            _sparkle.DownloadFinished    += (_, _) =>
                Log.Information("[Update] Download finished.");
            _sparkle.DownloadHadError    += (item, path, ex) =>
                Log.Warning("[Update] Download error for {Item}: {Err}", item?.DownloadLink, ex?.Message);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Runs a silent background update check on startup.  Shows the update
        /// dialog only if a new version is found and the user has not previously
        /// skipped it.
        /// </summary>
        public void StartBackgroundCheck()
        {
            if (!ApplicationSettings.Instance.EnableAutoUpdateCheck)
            {
                Log.Debug("[Update] Auto update check disabled by user setting.");
                return;
            }

            Log.Debug("[Update] Starting background update check.");
            _sparkle.StartLoop(doInitialCheck: true, forceInitialCheck: false,
                checkFrequency: TimeSpan.FromHours(24));
        }

        /// <summary>
        /// Performs an immediate check and shows the update window regardless of
        /// whether the user has previously skipped the version.  For use from
        /// the "Check for Updates" menu item.
        /// </summary>
        public async Task CheckAndShowUpdateWindowAsync()
        {
            Log.Information("[Update] Manual update check triggered.");
            await _sparkle.CheckForUpdatesAtUserRequest();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sparkle.Dispose();
        }
    }
}
