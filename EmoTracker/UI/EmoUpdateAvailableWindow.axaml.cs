using Avalonia.Controls;
using Avalonia.Interactivity;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.UI.Avalonia.Interfaces;
using System.Collections.Generic;

namespace EmoTracker.UI
{
    public partial class EmoUpdateAvailableWindow : Window, IUpdateAvailable, IReleaseNotesDisplayer
    {
        // ── IUpdateAvailable ───────────────────────────────────────────────────
        public event UserRespondedToUpdate UserResponded;

        public UpdateAvailableResult Result { get; private set; } = UpdateAvailableResult.None;

        public AppCastItem CurrentItem { get; set; }

        // ── Construction ───────────────────────────────────────────────────────
        public EmoUpdateAvailableWindow()
        {
            InitializeComponent();
        }

        public EmoUpdateAvailableWindow(List<AppCastItem> updates, string currentVersion, string appName)
            : this()
        {
            if (updates == null || updates.Count == 0) return;

            CurrentItem = updates[0];
            string newVersion = CurrentItem.Version ?? string.Empty;

            TitleText.Text    = $"A new version of {appName} is available.";
            SubtitleText.Text = $"{appName} {newVersion} is now available — you have {currentVersion}. Would you like to download it now?";

            // Populate release notes from the appcast Description field.
            // GitHubAppCastDataDownloader converts markdown → HTML via Markdig;
            // we wrap it in a CSS stylesheet and hand it to HtmlLabel.
            string html = CurrentItem.Description ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(html))
                ReleaseNotesLabel.Text = WrapWithCss(html);
        }

        // ── IReleaseNotesDisplayer ─────────────────────────────────────────────
        // NetSparkle calls this if it manages to go through its own ReleaseNotesGrabber
        // pipeline; we ignore it since we populate notes directly in the constructor.
        public void ShowReleaseNotes(string html) { }

        // ── IUpdateAvailable ───────────────────────────────────────────────────
        public new void Show()
        {
            base.Show();
        }

        public void HideReleaseNotes()
        {
            if (ReleaseNotesLabel?.Parent is Grid g && g.Parent is ScrollViewer sv && sv.Parent is Border b)
                b.IsVisible = false;
        }

        private static string WrapWithCss(string htmlBody) =>
            "<html><head><style>" +
            "body { background-color: #1a1a1a; color: #c8c8c8; font-family: -apple-system, BlinkMacSystemFont, sans-serif; font-size: 13px; line-height: 1.65; margin: 12px 8px; word-wrap: break-word; overflow-wrap: break-word; }" +
            "h1 { color: #f0f0f0; font-size: 17px; font-weight: 600; margin: 0 0 14px 0; padding-bottom: 8px; border-bottom: 1px solid #333339; }" +
            "h2 { color: #f0f0f0; font-size: 15px; font-weight: 600; margin: 20px 0 10px 0; padding-bottom: 6px; border-bottom: 1px solid #2a2a2a; }" +
            "h3, h4 { color: #e0e0e0; font-size: 13px; font-weight: 600; margin: 16px 0 8px 0; }" +
            "h1:first-child, h2:first-child, h3:first-child, h4:first-child { margin-top: 0; }" +
            "a { color: #7eb5d6; word-break: break-all; }" +
            "p { margin: 0 0 10px 0; }" +
            "code { background-color: #2d2d2d; color: #e0e0e0; padding: 1px 5px; border-radius: 3px; font-size: 12px; }" +
            "pre { background-color: #111111; padding: 10px 12px; border-radius: 4px; border-left: 3px solid #555560; overflow: hidden; margin: 10px 0 14px 0; }" +
            "pre code { background-color: transparent; padding: 0; }" +
            "ul { margin: 0 0 10px 0; padding-left: 20px; list-style-type: disc; }" +
            "ol { margin: 0 0 10px 0; padding-left: 20px; }" +
            "li { margin-bottom: 5px; }" +
            "hr { border: none; border-top: 1px solid #2e2e2e; margin: 18px 0; }" +
            "strong { color: #e8e8e8; }" +
            "blockquote { border-left: 3px solid #444444; margin: 10px 0; padding: 4px 12px; color: #909090; }" +
            "</style></head><body>" +
            htmlBody +
            "</body></html>";

        public void HideRemindMeLaterButton()  => RemindButton.IsVisible   = false;
        public void HideSkipButton()           => SkipButton.IsVisible     = false;

        public void BringToFront()
        {
            Activate();
            Topmost = true;
            Topmost = false;
        }

        public new void Close()
        {
            base.Close();
        }

        // ── Button handlers ────────────────────────────────────────────────────
        private void SkipButton_Click(object sender, RoutedEventArgs e)
            => Respond(UpdateAvailableResult.SkipUpdate);

        private void RemindButton_Click(object sender, RoutedEventArgs e)
            => Respond(UpdateAvailableResult.RemindMeLater);

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
            => Respond(UpdateAvailableResult.InstallUpdate);

        private void Respond(UpdateAvailableResult result)
        {
            Result = result;
            UserResponded?.Invoke(this, new UpdateResponseEventArgs(result, CurrentItem));
            base.Close();
        }
    }
}
