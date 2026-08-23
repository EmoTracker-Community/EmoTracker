using Avalonia.Threading;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Extensions.AutoTracker;
using EmoTracker.Extensions.McpServer.Tools;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    /// <summary>
    /// MCP tools that drive the auto-tracker headlessly. This closes the gap
    /// that <c>get_autotracker_status</c> (read-only) leaves open: a smoke test
    /// can select a provider/device, set provider options, start/stop tracking,
    /// and wait deterministically on tracker state.
    ///
    /// All control work is marshalled onto the primary state's
    /// <see cref="AutoTrackerExtension"/> so it uses the exact same driver path
    /// the UI does.
    /// </summary>
    [McpServerToolType]
    public class AutoTrackerControlTools
    {
        static AutoTrackerExtension ActiveAt
        {
            get
            {
                var state = EmoTracker.ApplicationModel.Instance?.PrimaryState;
                if (state == null) return null;
                return ExtensionManager.Instance?.GetTrackerExtension<AutoTrackerExtension>(state);
            }
        }

        static IAutoTrackingProvider FindProvider(AutoTrackerExtension at, string uid)
        {
            if (at == null) return null;
            if (string.IsNullOrEmpty(uid))
                return at.ApplicableProviders?.FirstOrDefault();
            foreach (var p in at.ApplicableProviders)
                if (p.UID?.Equals(uid, StringComparison.OrdinalIgnoreCase) == true)
                    return p;
            return null;
        }

        static IAutoTrackingDevice FindDevice(AutoTrackerExtension at, string providerUid, string deviceId)
        {
            var provider = FindProvider(at, providerUid);
            if (provider == null) return null;
            if (string.IsNullOrEmpty(deviceId))
                return provider.AvailableDevices?.FirstOrDefault();
            foreach (var d in provider.AvailableDevices)
                if (d.Id?.Equals(deviceId, StringComparison.OrdinalIgnoreCase) == true
                    || d.DisplayName?.Equals(deviceId, StringComparison.OrdinalIgnoreCase) == true)
                    return d;
            return null;
        }

        static string SerializeError(Exception ex)
            => JsonSerializer.Serialize(new { success = false, error = ex.Message });

        [McpServerTool(Name = "autotracker_select_provider")]
        [Description("Select an auto-tracking provider by UID (e.g. 'sni' or 'nwa') and refresh its device list. Auto-picks the first device if none is selected.")]
        public static async Task<string> SelectProvider(
            [Description("Provider UID (sni, nwa)")] string uid)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var at = ActiveAt;
                    var provider = FindProvider(at, uid);
                    if (at == null || provider == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Provider not found" });

                    await at.SelectProviderAsync(provider);
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        provider = provider.UID,
                        displayName = provider.DisplayName,
                        defaultDevice = provider.DefaultDevice?.ToString(),
                        availableDevices = provider.AvailableDevices.Select(d => d.ToString()).ToArray()
                    });
                }
                catch (Exception ex) { return SerializeError(ex); }
            });
        }

        [McpServerTool(Name = "autotracker_select_device")]
        [Description("Select the default device for the active provider by device ID or display name. Optionally pass providerUid.")]
        public static async Task<string> SelectDevice(
            [Description("Device ID or display name")] string deviceId,
            [Description("Optional provider UID")] string providerUid = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var at = ActiveAt;
                    var provider = FindProvider(at, providerUid ?? at?.ActiveProvider?.UID ?? at?.SelectedProvider?.UID);
                    if (at == null || provider == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No provider selected" });

                    var device = FindDevice(at, provider.UID, deviceId);
                    if (device == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Device not found" });

                    await at.SelectDeviceAsync(device);
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        provider = provider.UID,
                        defaultDevice = provider.DefaultDevice?.ToString(),
                        id = device.Id,
                        displayName = device.DisplayName
                    });
                }
                catch (Exception ex) { return SerializeError(ex); }
            });
        }

        [McpServerTool(Name = "autotracker_set_option")]
        [Description("Set a provider option by key (SNI: address_space, memory_mapping). Value must be a string.")]
        public static async Task<string> SetOption(
            [Description("Option key")] string key,
            [Description("Option value")] string value)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var at = ActiveAt;
                    if (at == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No auto-tracker" });

                    var ok = at.SetProviderOption(key, value);
                    return ok
                        ? JsonSerializer.Serialize(new { success = true, key, value })
                        : JsonSerializer.Serialize(new { success = false, error = "Option not found or no provider selected" });
                }
                catch (Exception ex) { return SerializeError(ex); }
            });
        }

        [McpServerTool(Name = "autotracker_start")]
        [Description("Start auto-tracking on the selected provider/device.")]
        public static async Task<string> Start()
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var at = ActiveAt;
                    if (at == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No auto-tracker" });
                    var started = await at.StartAutoTrackingAsync();
                    return JsonSerializer.Serialize(new
                    {
                        success = started,
                        active = at.Active,
                        connected = at.Connected,
                        provider = at.ActiveProvider?.UID,
                        device = at.ActiveProvider?.DefaultDevice?.ToString()
                    });
                }
                catch (Exception ex) { return SerializeError(ex); }
            });
        }

        [McpServerTool(Name = "autotracker_stop")]
        [Description("Stop auto-tracking.")]
        public static async Task<string> Stop()
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var at = ActiveAt;
                    if (at == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No auto-tracker" });
                    await at.StopAutoTrackingAsync();
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        connected = at.Connected,
                        provider = at.ActiveProvider?.UID
                    });
                }
                catch (Exception ex) { return SerializeError(ex); }
            });
        }

        [McpServerTool(Name = "autotracker_get")]
        [Description("Rich auto-tracker status: providers, devices, connection, active provider, and registered memory segments/timers.")]
        public static async Task<string> Get()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var at = ActiveAt;
                    if (at == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No auto-tracker" });

                    var providers = new List<object>();
                    foreach (var p in at.ApplicableProviders)
                    {
                        providers.Add(new
                        {
                            uid = p.UID,
                            displayName = p.DisplayName,
                            isConnected = p.IsConnected,
                            defaultDevice = p.DefaultDevice?.ToString(),
                            availableDevices = p.AvailableDevices.Select(d => new
                            {
                                id = d.Id,
                                displayName = d.DisplayName,
                                isConnected = d.IsConnected
                            }).ToArray(),
                            options = p.Options.Select(o => new
                            {
                                key = o.Key,
                                displayName = o.DisplayName,
                                value = o.Value?.ToString(),
                                availableValues = o.AvailableValues?.Select(v => v?.ToString()).ToArray()
                            }).ToArray()
                        });
                    }

                    var segments = new List<object>();
                    var scripts = at.State?.Scripts;
                    if (scripts != null)
                    {
                        foreach (var s in scripts.MemorySegments)
                            segments.Add(new { name = s.Name, address = s.StartAddress.ToString("X6"), size = s.Length });
                        foreach (var t in scripts.MemoryTimers)
                            segments.Add(new { name = t.Name, timer = true });
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        active = at.Active,
                        connected = at.Connected,
                        reconnecting = at.Reconnecting,
                        error = at.Error,
                        activeProvider = at.ActiveProvider?.UID,
                        selectedProvider = at.SelectedProvider?.UID,
                        providers,
                        memorySegmentsAndTimers = segments
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "autotracker_wait_connected")]
        [Description("Wait until the auto-tracker reports Connected (or until timeout). Returns whether connected within the timeout.")]
        public static async Task<string> WaitConnected(
            [Description("Timeout in milliseconds")] int timeoutMs = 10000)
        {
            var at = ActiveAt;
            if (at == null)
                return JsonSerializer.Serialize(new { success = false, error = "No auto-tracker" });

            var (ok, error) = await at.WaitUntilAsync(e => e.Connected && e.ActiveProvider != null, timeoutMs);
            return JsonSerializer.Serialize(new { success = ok, connected = ok, error = error?.Message });
        }

        [McpServerTool(Name = "autotracker_wait_item")]
        [Description("Wait until an item reaches a given activation state, or timeout. itemActive=true means the item is captured/toggled on.")]
        public static async Task<string> WaitItem(
            [Description("Item name (exact)")] string name,
            [Description("Expected active state (true = active/checked)")] bool itemActive,
            [Description("Timeout in milliseconds")] int timeoutMs = 10000)
        {
            var at = ActiveAt;
            if (at == null)
                return JsonSerializer.Serialize(new { success = false, error = "No auto-tracker" });

            var (ok, error) = await at.WaitUntilAsync(e =>
            {
                var item = FindItemByName(name);
                if (item == null) return false;
                return IsItemActive(item) == itemActive;
            }, timeoutMs);

            return JsonSerializer.Serialize(new { success = ok, reached = ok, item = name, error = error?.Message });
        }

        static EmoTracker.Data.ITrackableItem FindItemByName(string name)
        {
            var items = EmoTracker.ApplicationModel.Instance?.PrimaryState?.Items?.Items;
            if (items == null) return null;
            foreach (var item in items)
                if (item?.Name != null && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return item;
            return null;
        }

        static bool IsItemActive(EmoTracker.Data.ITrackableItem item)
        {
            switch (item)
            {
                case EmoTracker.Data.Items.ToggleItem t:
                    return t.Active;
                case EmoTracker.Data.Items.ToggleBadgedItem tb:
                    return tb.Active;
                case EmoTracker.Data.Items.ProgressiveToggleItem pt:
                    return pt.Active || pt.CurrentStage > 0;
                case EmoTracker.Data.Items.ConsumableItem c:
                    return c.AcquiredCount > 0;
                case EmoTracker.Data.Items.ProgressiveItem p:
                    return p.CurrentStage > 0;
                default:
                    return false;
            }
        }
    }
}
