using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Session;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.McpServer.Tools
{
    [McpServerToolType]
    public class SettingsTools
    {
        [McpServerTool(Name = "get_settings")]
        [Description("Get all current application settings")]
        public static async Task<string> GetSettings()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var s = TrackerSession.Current.Global;
                return JsonSerializer.Serialize(new
                {
                    ignoreAllLogic = s.IgnoreAllLogic,
                    displayAllLocations = s.DisplayAllLocations,
                    alwaysAllowClearing = s.AlwaysAllowClearing,
                    autoUnpinLocationsOnClear = s.AutoUnpinLocationsOnClear,
                    pinLocationsOnItemCapture = s.PinLocationsOnItemCapture,
                    fastToolTips = s.FastToolTips,
                    promptOnRefreshClose = s.PromptOnRefreshClose,
                    alwaysOnTop = s.AlwaysOnTop,
                    enableVoiceControl = s.EnableVoiceControl,
                    enableNoteTaking = s.EnableNoteTaking,
                    enableVariantSwitcher = s.EnableVariantSwitcher,
                    enableDiscordRichPresence = s.EnableDiscordRichPresence,
                    enableAutoUpdateCheck = s.EnableAutoUpdateCheck,
                    enableBackgroundNdi = s.EnableBackgroundNdi,
                    ndiFrameRate = s.NdiFrameRate,
                    ndiOutputScale = s.NdiOutputScale,
                    initialWidth = s.InitialWidth,
                    initialHeight = s.InitialHeight,
                    lastActivePackage = s.LastActivePackage,
                    lastActivePackageVariant = s.LastActivePackageVariant,
                    mapEnabled = TrackerSession.Current.Tracker.MapEnabled,
                    swapLeftRight = TrackerSession.Current.Tracker.SwapLeftRight
                });
            });
        }

        [McpServerTool(Name = "set_setting")]
        [Description("Set an application setting by key. Valid keys: ignoreAllLogic, displayAllLocations, alwaysAllowClearing, autoUnpinLocationsOnClear, pinLocationsOnItemCapture, fastToolTips, promptOnRefreshClose, alwaysOnTop, enableVoiceControl, enableNoteTaking, enableVariantSwitcher, enableDiscordRichPresence, enableAutoUpdateCheck, enableBackgroundNdi, mapEnabled, swapLeftRight")]
        public static async Task<string> SetSetting(
            [Description("The setting key")] string key,
            [Description("The value to set (true/false for booleans)")] string value)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var s = TrackerSession.Current.Global;
                    bool boolVal;

                    switch (key)
                    {
                        case "ignoreAllLogic":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.IgnoreAllLogic = boolVal;
                            break;
                        case "displayAllLocations":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.DisplayAllLocations = boolVal;
                            break;
                        case "alwaysAllowClearing":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.AlwaysAllowClearing = boolVal;
                            break;
                        case "autoUnpinLocationsOnClear":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.AutoUnpinLocationsOnClear = boolVal;
                            break;
                        case "pinLocationsOnItemCapture":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.PinLocationsOnItemCapture = boolVal;
                            break;
                        case "fastToolTips":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.FastToolTips = boolVal;
                            break;
                        case "promptOnRefreshClose":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.PromptOnRefreshClose = boolVal;
                            break;
                        case "alwaysOnTop":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.AlwaysOnTop = boolVal;
                            break;
                        case "enableVoiceControl":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.EnableVoiceControl = boolVal;
                            break;
                        case "enableNoteTaking":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.EnableNoteTaking = boolVal;
                            break;
                        case "enableVariantSwitcher":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.EnableVariantSwitcher = boolVal;
                            break;
                        case "enableDiscordRichPresence":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.EnableDiscordRichPresence = boolVal;
                            break;
                        case "enableAutoUpdateCheck":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.EnableAutoUpdateCheck = boolVal;
                            break;
                        case "enableBackgroundNdi":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            s.EnableBackgroundNdi = boolVal;
                            break;
                        case "mapEnabled":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            TrackerSession.Current.Tracker.MapEnabled = boolVal;
                            break;
                        case "swapLeftRight":
                            if (!bool.TryParse(value, out boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false" });
                            TrackerSession.Current.Tracker.SwapLeftRight = boolVal;
                            break;
                        default:
                            return JsonSerializer.Serialize(new { success = false, error = $"Unknown setting key: {key}" });
                    }

                    return JsonSerializer.Serialize(new { success = true, key, value });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }
    }
}
