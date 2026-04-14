using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Session;
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
    public class ApplicationControlTools
    {
        [McpServerTool(Name = "list_packs")]
        [Description("List all available game packs that can be loaded")]
        public static async Task<string> ListPacks()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var packages = PackageManager.Instance.AvailablePackages;
                var result = new List<object>();

                foreach (var entry in packages)
                {
                    result.Add(new
                    {
                        name = entry.Name,
                        uniqueId = entry.UID,
                        game = entry.Game,
                        author = entry.Author,
                        version = entry.Version?.ToString(),
                        installed = entry.ExistingPackage != null
                    });
                }

                return JsonSerializer.Serialize(result);
            });
        }

        [McpServerTool(Name = "load_pack")]
        [Description("Load a game pack by its unique ID. Optionally specify a variant.")]
        public static async Task<string> LoadPack(
            [Description("The unique ID of the pack to load")] string uniqueId,
            [Description("Optional variant unique ID")] string variant = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var packages = PackageManager.Instance.AvailablePackages;
                    PackageRepositoryEntry found = null;

                    foreach (var entry in packages)
                    {
                        if (entry.UID != null &&
                            entry.UID.Equals(uniqueId, StringComparison.OrdinalIgnoreCase))
                        {
                            found = entry;
                            break;
                        }
                    }

                    if (found?.ExistingPackage == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Pack not found or not installed" });

                    var pack = found.ExistingPackage;

                    if (!string.IsNullOrEmpty(variant))
                    {
                        var v = pack.FindVariant(variant);
                        if (v != null)
                        {
                            TrackerSession.Current.Tracker.ActiveGamePackageVariant = v;
                            return JsonSerializer.Serialize(new { success = true, variant = v.DisplayName });
                        }
                        return JsonSerializer.Serialize(new { success = false, error = "Variant not found" });
                    }

                    TrackerSession.Current.Tracker.ActiveGamePackage = pack;
                    return JsonSerializer.Serialize(new { success = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "toggle_item")]
        [Description("Toggle a tracker item by name (simulates left-click)")]
        public static async Task<string> ToggleItem([Description("The item name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var item = FindItemByName(name);
                    if (item == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Item not found" });

                    item.OnLeftClick();
                    return SerializeItemState(item);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "right_click_item")]
        [Description("Simulate right-click on a tracker item by name")]
        public static async Task<string> RightClickItem([Description("The item name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var item = FindItemByName(name);
                    if (item == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Item not found" });

                    item.OnRightClick();
                    return SerializeItemState(item);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "reset_tracker")]
        [Description("Reset all tracker state to initial values")]
        public static async Task<string> ResetTracker()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    ApplicationModel.Instance.ResetUserDataCommand?.Execute(null);
                    return JsonSerializer.Serialize(new { success = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "clear_location")]
        [Description("Clear a location by name, simulating a right-click on its map square. Calls FullClearAllPossible() within a transaction.")]
        public static async Task<string> ClearLocation([Description("The location name to clear")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var locations = TrackerSession.Current.Locations.AllLocations;
                    if (locations == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No locations loaded" });

                    EmoTracker.Data.Locations.Location found = null;
                    foreach (var loc in locations)
                    {
                        if (loc?.Name != null &&
                            loc.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            found = loc;
                            break;
                        }
                    }

                    if (found == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Location '{name}' not found" });

                    using (TransactionProcessor.Current.OpenTransaction())
                    {
                        found.FullClearAllPossible();
                        found.ModifiedByUser = true;
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        name = found.Name,
                        accessibility = found.AccessibilityLevel.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "shutdown")]
        [Description("Trigger a normal shutdown of the application by closing the main window")]
        public static async Task<string> Shutdown()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    var window = lifetime?.MainWindow;
                    if (window == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Main window not found" });

                    window.Close();
                    return JsonSerializer.Serialize(new { success = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "undo")]
        [Description("Undo the last tracker action (equivalent to Ctrl+Z)")]
        public static async Task<string> Undo()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (TransactionProcessor.Current is IUndoableTransactionProcessor undo)
                    {
                        undo.Undo();
                        return JsonSerializer.Serialize(new { success = true });
                    }

                    return JsonSerializer.Serialize(new { success = false, error = "Undo not available" });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        private static ITrackableItem FindItemByName(string name)
        {
            foreach (var item in TrackerSession.Current.Items.Items)
            {
                if (item?.Name != null &&
                    item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private static string SerializeItemState(ITrackableItem item)
        {
            var state = new Dictionary<string, object>
            {
                ["success"] = true,
                ["name"] = item.Name,
                ["badgeText"] = item.BadgeText
            };

            if (item is ToggleItem toggle)
                state["active"] = toggle.Active;
            else if (item is ConsumableItem consumable)
                state["acquiredCount"] = consumable.AcquiredCount;
            else if (item is ProgressiveItem progressive)
                state["currentStage"] = progressive.CurrentStage;
            return JsonSerializer.Serialize(state);
        }
    }
}
