using EmoTracker.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer
{
    public class McpServerExtension : ObservableObject, IApplicationExtension
    {
        const int DefaultPort = 27125;

        public string Name => "MCP Dev Server";
        public string UID => "emotracker_mcp_server";
        public int Priority => -500;

        private bool mbActive;
        public bool Active
        {
            get => mbActive;
            private set => SetProperty(ref mbActive, value);
        }

        // True iff at least one MCP client is currently connected (has
        // made an MCP HTTP request within the last <see cref="SessionTtl"/>).
        // Bound by McpStatusIndicator's `Classes.connected` to drive the
        // green-when-connected indicator. Distinct from <see cref="Active"/>,
        // which only reports server-listening status.
        private bool mbClientConnected;
        public bool ClientConnected
        {
            get => mbClientConnected;
            private set => SetProperty(ref mbClientConnected, value);
        }

        // Active client sessions keyed by Mcp-Session-Id, value = last-seen
        // UTC timestamp. The HTTP middleware updates this on every request;
        // a background timer expires entries older than SessionTtl so a
        // crashed / disappeared client doesn't leave the indicator green
        // forever.
        readonly ConcurrentDictionary<string, DateTime> mActiveSessions = new();
        static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(60);
        Timer mSessionSweepTimer;

        private string mStatusText = "Stopped";
        public string StatusText
        {
            get => mStatusText;
            private set => SetProperty(ref mStatusText, value);
        }

        private WebApplication mApp;

        // Avalonia visuals are single-parent: each MainWindow that binds
        // the status bar needs its own control instance. Return fresh per
        // getter call; DataContext binds back to the singleton extension
        // so all windows reflect the same server status.
        public object StatusBarControl
        {
            get
            {
                if (!UserDirectory.IsDevMode)
                    return null;
                return new McpStatusIndicator { DataContext = this };
            }
        }

        public void Start(IApplicationContext app)
        {
            if (!UserDirectory.IsDevMode)
                return;

            _ = StartServerAsync();
        }

        public void Stop()
        {
            if (mSessionSweepTimer != null)
            {
                try { mSessionSweepTimer.Dispose(); } catch { }
                mSessionSweepTimer = null;
            }
            mActiveSessions.Clear();
            UpdateClientConnectedFlag();

            if (mApp != null)
            {
                try
                {
                    // Run async shutdown on a ThreadPool thread to avoid deadlocking
                    // the main thread's synchronization context.
                    Task.Run(async () =>
                    {
                        await mApp.StopAsync();
                        await mApp.DisposeAsync();
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[MCP] Error stopping server");
                }
                mApp = null;
                Active = false;
                StatusText = "Stopped";
            }
        }

        // Drop session entries whose last-seen timestamp predates the
        // TTL cutoff and refresh ClientConnected. Called by the sweep
        // timer.
        private void SweepStaleSessions()
        {
            var cutoff = DateTime.UtcNow - SessionTtl;
            foreach (var kvp in mActiveSessions)
            {
                if (kvp.Value < cutoff)
                    mActiveSessions.TryRemove(kvp.Key, out _);
            }
            UpdateClientConnectedFlag();
        }

        private void UpdateClientConnectedFlag()
        {
            bool nowConnected = mActiveSessions.Count > 0;
            if (nowConnected != mbClientConnected)
            {
                // Marshal property update to the UI thread so XAML bindings
                // observe the change correctly.
                EmoTracker.Core.Services.Dispatch.BeginInvoke(() => ClientConnected = nowConnected);
            }
        }

        private async Task StartServerAsync()
        {
            try
            {
                int port = DefaultPort;
                string portEnv = Environment.GetEnvironmentVariable("EMOTRACKER_MCP_PORT");
                if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out int envPort))
                    port = envPort;

                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls($"http://localhost:{port}");

                builder.Logging.ClearProviders();

                builder.Services.AddMcpServer(options =>
                {
                    options.ServerInfo = new()
                    {
                        Name = "EmoTracker Dev Server",
                        Version = ApplicationVersion.Current.ToString()
                    };
                })
                .WithTools<Tools.PackDataTools>()
                .WithTools<Tools.ApplicationControlTools>()
                .WithTools<Tools.LuaTools>()
                .WithTools<Tools.ScreenshotTools>()
                .WithTools<Tools.UiAutomationTools>()
                .WithTools<Tools.SettingsTools>()
                .WithTools<Tools.LocationTools>()
                .WithTools<Tools.SaveLoadTools>()
                .WithTools<Tools.WindowTools>()
                .WithTools<Tools.NoteTools>()
                .WithTools<Tools.ExtensionTools>()
                .WithTools<Tools.PackageTools>()
                .WithTools<Tools.ImageCacheTools>()
                .WithTools<Tools.NotificationTools>()
                .WithHttpTransport();

                mApp = builder.Build();

                // Session-tracking middleware: every MCP request carries a
                // Mcp-Session-Id header (initialize establishes the id;
                // subsequent requests echo it). Touch the dictionary on
                // each request so ClientConnected reflects "at least one
                // client has talked to us in the last SessionTtl seconds".
                mApp.Use(async (HttpContext ctx, RequestDelegate next) =>
                {
                    string sid = ctx.Request.Headers.TryGetValue("Mcp-Session-Id", out var v)
                        ? v.ToString() : null;

                    if (!string.IsNullOrEmpty(sid))
                    {
                        // DELETE on a session id means the client closed the
                        // session — drop it explicitly so the indicator
                        // turns grey immediately.
                        if (HttpMethods.IsDelete(ctx.Request.Method))
                            mActiveSessions.TryRemove(sid, out _);
                        else
                            mActiveSessions[sid] = DateTime.UtcNow;
                        UpdateClientConnectedFlag();
                    }

                    await next(ctx);
                });

                mApp.MapMcp();

                await mApp.StartAsync();

                // Start the TTL sweeper after the server is up. Period of
                // half the TTL keeps stale sessions from lingering more
                // than ~SessionTtl + 30s.
                mSessionSweepTimer = new Timer(_ => SweepStaleSessions(), null,
                    TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

                Active = true;
                StatusText = $"Listening on port {port}";
                Log.Information("[MCP] Server listening on port {Port}", port);
            }
            catch (Exception ex)
            {
                Active = false;
                StatusText = $"Error: {ex.Message}";
                Log.Error(ex, "[MCP] Failed to start server");
            }
        }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
