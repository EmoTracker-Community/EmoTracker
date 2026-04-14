using EmoTracker.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer
{
    public class McpServerExtension : ObservableObject, Extension
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

        private string mStatusText = "Stopped";
        public string StatusText
        {
            get => mStatusText;
            private set => SetProperty(ref mStatusText, value);
        }

        private WebApplication mApp;
        private McpStatusIndicator mStatusBarControl;

        public object StatusBarControl
        {
            get
            {
                if (!UserDirectory.IsDevMode)
                    return null;

                if (mStatusBarControl == null)
                    mStatusBarControl = new McpStatusIndicator { DataContext = this };
                return mStatusBarControl;
            }
        }

        public void Start()
        {
            if (!UserDirectory.IsDevMode)
                return;

            _ = StartServerAsync();
        }

        public void Stop()
        {
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
                .WithTools<Tools.ProgressionTools>()
                .WithHttpTransport();

                mApp = builder.Build();
                mApp.MapMcp();

                await mApp.StartAsync();

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

        public void OnPackageUnloaded() { }
        public void OnPackageLoaded() { }

        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;
    }
}
