using NetSparkleUpdater.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace EmoTracker.Services.Updates
{
    /// <summary>
    /// Implements <see cref="IAppCastDataDownloader"/> by querying the GitHub Releases API
    /// at runtime and generating a NetSparkle appcast XML string on the fly.  No hosted
    /// appcast file is required — GitHub Releases is the single source of truth.
    ///
    /// Asset filenames are matched to platforms by the naming convention:
    ///   EmoTracker-{version}-win-x64.zip       → windows
    ///   EmoTracker-{version}-osx-universal.tar.gz → osx
    ///   EmoTracker-{version}-linux-x64.tar.xz  → linux
    /// </summary>
    public class GitHubAppCastDataDownloader : IAppCastDataDownloader
    {
        private const string ApiBase = "https://api.github.com";
        private readonly string _repoOwner;
        private readonly string _repoName;

        private static readonly HttpClient Http = new()
        {
            DefaultRequestHeaders =
            {
                UserAgent = { new ProductInfoHeaderValue("EmoTracker", "1.0") },
                Accept    = { new MediaTypeWithQualityHeaderValue("application/vnd.github+json") }
            }
        };

        // Maps a substring found in the asset filename to a platform key.
        // Order matters: "win" must come before "linux" to avoid false matches.
        private static readonly Dictionary<string, string> PlatformMap = new()
        {
            { "win",   "windows" },
            { "osx",   "osx"     },
            { "linux", "linux"   },
        };

        // Resolved once at startup — the platform key for the currently running OS.
        private static readonly string CurrentPlatformKey =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? "osx"     :
                                                                   "linux";

        public GitHubAppCastDataDownloader(string repoOwner, string repoName)
        {
            _repoOwner = repoOwner;
            _repoName  = repoName;
        }

        public string DownloadAndGetAppCastData(string url)
        {
            return DownloadAndGetAppCastDataAsync(url).GetAwaiter().GetResult();
        }

        public async Task<string> DownloadAndGetAppCastDataAsync(string url)
        {
            // url is whatever string was passed as the appcast URL to SparkleUpdater;
            // we ignore it and always talk directly to the GitHub API.
            //
            // We use /releases?per_page=1 (most-recent-first) rather than
            // /releases/latest because the latter only returns non-pre-release
            // entries and would 404 while the project is in pre-release.
            try
            {
                string apiUrl = $"{ApiBase}/repos/{_repoOwner}/{_repoName}/releases?per_page=1";
                string json   = await Http.GetStringAsync(apiUrl).ConfigureAwait(false);

                // The response is a JSON array; unwrap the first element.
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                    doc.RootElement.GetArrayLength() == 0)
                {
                    Log.Warning("[Update] No releases found in GitHub API response.");
                    return EmptyAppCast();
                }

                string releaseJson = doc.RootElement[0].GetRawText();
                string appcast = BuildAppCastXml(releaseJson);
                Log.Debug("[Update] Generated appcast XML:\n{Appcast}", appcast);
                return appcast;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Update] Failed to fetch latest release from GitHub: {Msg}", ex.Message);
                return EmptyAppCast();
            }
        }

        // Returns the encoding used by the app cast data (UTF-8).
        public Encoding GetAppCastEncoding() => Encoding.UTF8;

        // -----------------------------------------------------------------------

        private string BuildAppCastXml(string releaseJson)
        {
            var release = JsonNode.Parse(releaseJson)!;

            string tagName  = release["tag_name"]!.GetValue<string>();
            // Strip leading 'v' and any pre-release suffix (e.g. "3.0.1.1-preview" → "3.0.1.1")
            // so that System.Version.TryParse can compare it against the assembly version.
            string version  = tagName.TrimStart('v');
            int dashIdx = version.IndexOf('-');
            if (dashIdx >= 0) version = version[..dashIdx];
            Log.Information("[Update] Release tag: {Tag}, parsed version for appcast: {Version}", tagName, version);
            string title    = release["name"]?.GetValue<string>() ?? $"EmoTracker {version}";
            string pubDate  = DateTime.TryParse(
                                  release["published_at"]?.GetValue<string>(),
                                  out var dt)
                              ? dt.ToString("R")
                              : DateTime.UtcNow.ToString("R");
            string releaseNotesUrl =
                $"https://github.com/{_repoOwner}/{_repoName}/releases/tag/{tagName}";

            // Only emit the enclosure for the platform we're running on.
            // Omitting sparkle:os means NetSparkle's OS filter won't reject the item —
            // we are already doing the OS selection ourselves.
            string enclosure = null;
            var assets = release["assets"]?.AsArray();
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    string assetName = asset!["name"]!.GetValue<string>();
                    string assetUrl  = asset["browser_download_url"]!.GetValue<string>();
                    long   size      = asset["size"]?.GetValue<long>() ?? 0;

                    string platformKey = DetectPlatformKey(assetName);
                    if (platformKey != CurrentPlatformKey)
                        continue;

                    // sparkle:os must be present — omitting it defaults to "windows",
                    // which causes NetSparkle to filter the item out on macOS/Linux.
                    // Use the same key we matched on so the value is always consistent.
                    enclosure =
                        $"""
                              <enclosure url="{assetUrl}"
                                         sparkle:version="{version}"
                                         sparkle:os="{platformKey}"
                                         length="{size}"
                                         type="application/octet-stream" />
                        """;
                    break; // first match wins
                }
            }

            if (enclosure == null)
            {
                Log.Warning("[Update] No asset found for current platform ({Platform}) in GitHub release {Tag}.",
                    CurrentPlatformKey, tagName);
                return EmptyAppCast();
            }

            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
                  <channel>
                    <title>EmoTracker</title>
                    <item>
                      <title>{EscapeXml(title)}</title>
                      <sparkle:releaseNotesLink>{releaseNotesUrl}</sparkle:releaseNotesLink>
                      <pubDate>{pubDate}</pubDate>
                {enclosure}
                    </item>
                  </channel>
                </rss>
                """;
        }

        private static string DetectPlatformKey(string assetName)
        {
            string lower = assetName.ToLowerInvariant();
            foreach (var kv in PlatformMap)
                if (lower.Contains(kv.Key))
                    return kv.Value;
            return null;
        }

        private static string EmptyAppCast() =>
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
              <channel><title>EmoTracker</title></channel>
            </rss>
            """;

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
