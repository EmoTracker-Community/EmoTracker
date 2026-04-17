using Avalonia.Threading;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Extensions;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class ExtensionTools
    {
        [McpServerTool(Name = "list_extensions")]
        [Description("List all registered extensions with their status")]
        public static async Task<string> ListExtensions()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var extensions = ExtensionManager.Instance.Extensions;
                var result = new List<object>();

                foreach (var ext in extensions)
                {
                    var entry = new Dictionary<string, object>
                    {
                        ["name"] = ext.Name,
                        ["uid"] = ext.UID,
                        ["priority"] = ext.Priority
                    };

                    if (ext is McpServer.McpServerExtension mcpExt)
                    {
                        entry["active"] = mcpExt.Active;
                        entry["statusText"] = mcpExt.StatusText;
                    }

                    result.Add(entry);
                }

                return JsonSerializer.Serialize(result);
            });
        }

        [McpServerTool(Name = "get_autotracker_status")]
        [Description("Get auto-tracker connection status, available providers, and devices")]
        public static async Task<string> GetAutoTrackerStatus()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var registry = AutoTrackingProviderRegistry.Instance;
                    var providers = new List<object>();

                    foreach (var provider in registry.Providers)
                    {
                        var devices = new List<object>();
                        foreach (var device in provider.AvailableDevices)
                        {
                            devices.Add(new
                            {
                                name = device.ToString()
                            });
                        }

                        providers.Add(new
                        {
                            uid = provider.UID,
                            displayName = provider.DisplayName,
                            isConnected = provider.IsConnected,
                            supportedPlatforms = provider.SupportedPlatforms?.Select(p => p.ToString()).ToArray(),
                            defaultDevice = provider.DefaultDevice?.ToString(),
                            availableDevices = devices
                        });
                    }

                    return JsonSerializer.Serialize(new { providers });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }
    }
}
