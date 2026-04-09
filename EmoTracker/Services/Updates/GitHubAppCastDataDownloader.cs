using NetSparkleUpdater.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
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

        // Maps a substring found in the asset filename to a sparkle:os value.
        private static readonly Dictionary<string, string> PlatformMap = new()
        {
            { "win",   "windows" },
            { "osx",   "osx"     },
            { "linux", "linux"   },
        };

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
            try
            {
                string apiUrl = $"{ApiBase}/repos/{_repoOwner}/{_repoName}/releases/latest";
                string json   = await Http.GetStringAsync(apiUrl).ConfigureAwait(false);
                return BuildAppCastXml(json);
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
            string version  = tagName.TrimStart('v');
            string title    = release["name"]?.GetValue<string>() ?? $"EmoTracker {version}";
            string pubDate  = DateTime.TryParse(
                                  release["published_at"]?.GetValue<string>(),
                                  out var dt)
                              ? dt.ToString("R")
                              : DateTime.UtcNow.ToString("R");
            string releaseNotesUrl =
                $"https://github.com/{_repoOwner}/{_repoName}/releases/tag/{tagName}";

            var enclosures = new StringBuilder();
            var assets = release["assets"]?.AsArray();
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    string assetName = asset!["name"]!.GetValue<string>();
                    string assetUrl  = asset["browser_download_url"]!.GetValue<string>();
                    long   size      = asset["size"]?.GetValue<long>() ?? 0;

                    string os = DetectOs(assetName);
                    if (os == null)
                        continue;

                    enclosures.AppendLine(
                        $"""
                              <enclosure url="{assetUrl}"
                                         sparkle:version="{version}"
                                         sparkle:os="{os}"
                                         length="{size}"
                                         type="application/octet-stream" />
                        """);
                }
            }

            if (enclosures.Length == 0)
            {
                Log.Warning("[Update] No recognised assets found in GitHub release {Tag}.", tagName);
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
                {enclosures}    </item>
                  </channel>
                </rss>
                """;
        }

        private static string DetectOs(string assetName)
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
