using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
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
    public class PackDataTools
    {
        [McpServerTool(Name = "get_loaded_pack")]
        [Description("Get information about the currently loaded game pack")]
        public static async Task<string> GetLoadedPack()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var pack = TrackerSession.Current.Tracker.ActiveGamePackage;
                if (pack == null)
                    return JsonSerializer.Serialize(new { loaded = false });

                var variant = TrackerSession.Current.Tracker.ActiveGamePackageVariant;
                var variants = pack.AvailableVariants?.Select(v => new
                {
                    uniqueId = v.UniqueID,
                    displayName = v.DisplayName
                }).ToArray();

                return JsonSerializer.Serialize(new
                {
                    loaded = true,
                    displayName = pack.DisplayName,
                    uniqueId = pack.UniqueID,
                    game = pack.Game,
                    gameVariant = pack.GameVariant,
                    author = pack.Author,
                    version = pack.Version?.ToString(),
                    platform = pack.Platform.ToString(),
                    activeVariant = variant != null ? new
                    {
                        uniqueId = variant.UniqueID,
                        displayName = variant.DisplayName
                    } : null,
                    availableVariants = variants
                });
            });
        }

        [McpServerTool(Name = "list_items")]
        [Description("List all tracked items with their current state. Optionally filter by name substring.")]
        public static async Task<string> ListItems([Description("Optional name substring filter")] string filter = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var items = TrackerSession.Current.Items.Items;
                if (items == null)
                    return JsonSerializer.Serialize(Array.Empty<object>());

                var result = new List<object>();
                foreach (var item in items)
                {
                    if (item == null) continue;
                    if (!string.IsNullOrEmpty(filter) &&
                        (item.Name == null || !item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var entry = new Dictionary<string, object>
                    {
                        ["name"] = item.Name,
                        ["badgeText"] = item.BadgeText,
                        ["type"] = item.GetType().Name
                    };

                    if (item is ToggleItem toggle)
                    {
                        entry["active"] = toggle.Active;
                    }
                    else if (item is ConsumableItem consumable)
                    {
                        entry["acquiredCount"] = consumable.AcquiredCount;
                        entry["maxCount"] = consumable.MaxCount;
                    }
                    else if (item is ProgressiveItem progressive)
                    {
                        entry["currentStage"] = progressive.CurrentStage;
                    }

                    result.Add(entry);
                }

                return JsonSerializer.Serialize(result);
            });
        }

        [McpServerTool(Name = "list_locations")]
        [Description("List all locations with accessibility status. Optionally filter by name substring.")]
        public static async Task<string> ListLocations([Description("Optional name substring filter")] string filter = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var locations = TrackerSession.Current.Locations.AllLocations;
                if (locations == null)
                    return JsonSerializer.Serialize(Array.Empty<object>());

                var result = new List<object>();
                foreach (var loc in locations)
                {
                    if (loc == null) continue;
                    if (!string.IsNullOrEmpty(filter) &&
                        (loc.Name == null || !loc.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var sections = new List<object>();
                    foreach (var section in loc.Sections)
                    {
                        sections.Add(new
                        {
                            name = section.Name,
                            chestCount = section.ChestCount,
                            availableChestCount = section.AvailableChestCount,
                            accessibility = section.AccessibilityLevel.ToString()
                        });
                    }

                    result.Add(new
                    {
                        name = loc.Name,
                        accessibility = loc.AccessibilityLevel.ToString(),
                        sections,
                        childCount = loc.Children.Count()
                    });
                }

                return JsonSerializer.Serialize(result);
            });
        }

        [McpServerTool(Name = "get_item_by_name")]
        [Description("Find a tracked item by exact name and return its details")]
        public static async Task<string> GetItemByName([Description("The exact item name to search for")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var items = TrackerSession.Current.Items.Items;
                if (items == null)
                    return JsonSerializer.Serialize(new { found = false });

                foreach (var item in items)
                {
                    if (item?.Name != null &&
                        item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        var entry = new Dictionary<string, object>
                        {
                            ["found"] = true,
                            ["name"] = item.Name,
                            ["type"] = item.GetType().Name,
                            ["badgeText"] = item.BadgeText,
                            ["capturable"] = item.Capturable
                        };

                        if (item is ToggleItem toggle)
                            entry["active"] = toggle.Active;
                        else if (item is ConsumableItem consumable)
                        {
                            entry["acquiredCount"] = consumable.AcquiredCount;
                            entry["maxCount"] = consumable.MaxCount;
                        }
                        else if (item is ProgressiveItem progressive)
                        {
                            entry["currentStage"] = progressive.CurrentStage;
                        }

                        return JsonSerializer.Serialize(entry);
                    }
                }

                return JsonSerializer.Serialize(new { found = false });
            });
        }

        [McpServerTool(Name = "find_item_by_code")]
        [Description("Find a tracked item by its internal code (e.g. 'bow', 'hookshot') rather than display name")]
        public static async Task<string> FindItemByCode([Description("The item code to search for")] string code)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var item = TrackerSession.Current.Items.FindObjectForCode(code) as ITrackableItem;
                if (item == null)
                    return JsonSerializer.Serialize(new { found = false });

                return JsonSerializer.Serialize(SerializeItemDetails(item, true));
            });
        }

        [McpServerTool(Name = "get_item_details")]
        [Description("Get extended details for a tracked item by name, including codes, type info, and capturable status")]
        public static async Task<string> GetItemDetails([Description("The exact item name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var items = TrackerSession.Current.Items.Items;
                if (items == null)
                    return JsonSerializer.Serialize(new { found = false });

                foreach (var item in items)
                {
                    if (item?.Name != null &&
                        item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return JsonSerializer.Serialize(SerializeItemDetails(item, true));
                    }
                }

                return JsonSerializer.Serialize(new { found = false });
            });
        }

        [McpServerTool(Name = "set_item_state")]
        [Description("Directly set an item's state: active flag for toggles, stage index for progressive, count for consumable")]
        public static async Task<string> SetItemState(
            [Description("The item name")] string name,
            [Description("For toggle items: 'true' or 'false'. For progressive: stage index (0-based). For consumable: acquired count.")] string value)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    ITrackableItem item = null;
                    foreach (var i in TrackerSession.Current.Items.Items)
                    {
                        if (i?.Name != null && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            item = i;
                            break;
                        }
                    }

                    if (item == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Item not found" });

                    using (TransactionProcessor.Current.OpenTransaction())
                    {
                        if (item is ToggleItem toggle)
                        {
                            if (!bool.TryParse(value, out var boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false for toggle item" });
                            toggle.Active = boolVal;
                        }
                        else if (item is ProgressiveItem progressive)
                        {
                            if (!int.TryParse(value, out var stage))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected integer stage index" });
                            progressive.CurrentStage = stage;
                        }
                        else if (item is ConsumableItem consumable)
                        {
                            if (!int.TryParse(value, out var count))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected integer count" });
                            consumable.AcquiredCount = count;
                        }
                        else
                        {
                            return JsonSerializer.Serialize(new { success = false, error = $"Cannot set state on item type: {item.GetType().Name}" });
                        }
                    }

                    return JsonSerializer.Serialize(SerializeItemDetails(item, false));
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "batch_toggle_items")]
        [Description("Toggle multiple items by name in a single transaction (simulates left-click on each)")]
        public static async Task<string> BatchToggleItems(
            [Description("Comma-separated list of item names to toggle")] string names)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var nameList = names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var results = new List<object>();

                    using (TransactionProcessor.Current.OpenTransaction())
                    {
                        foreach (var name in nameList)
                        {
                            ITrackableItem found = null;
                            foreach (var item in TrackerSession.Current.Items.Items)
                            {
                                if (item?.Name != null && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                {
                                    found = item;
                                    break;
                                }
                            }

                            if (found != null)
                            {
                                found.OnLeftClick();
                                results.Add(new { name = found.Name, success = true, badgeText = found.BadgeText });
                            }
                            else
                            {
                                results.Add(new { name, success = false, error = "Item not found" });
                            }
                        }
                    }

                    return JsonSerializer.Serialize(results);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

        private static Dictionary<string, object> SerializeItemDetails(ITrackableItem item, bool includeFound)
        {
            var entry = new Dictionary<string, object>();

            if (includeFound)
                entry["found"] = true;
            else
                entry["success"] = true;

            entry["name"] = item.Name;
            entry["type"] = item.GetType().Name;
            entry["badgeText"] = item.BadgeText;
            entry["capturable"] = item.Capturable;
            entry["ignoreUserInput"] = item.IgnoreUserInput;

            if (item is ToggleItem toggle)
            {
                entry["active"] = toggle.Active;
                entry["loop"] = toggle.Loop;
            }
            else if (item is ConsumableItem consumable)
            {
                entry["acquiredCount"] = consumable.AcquiredCount;
                entry["consumedCount"] = consumable.ConsumedCount;
                entry["availableCount"] = consumable.AvailableCount;
                entry["minCount"] = consumable.MinCount;
                entry["maxCount"] = consumable.MaxCount;
                entry["countIncrement"] = consumable.CountIncrement;
            }
            else if (item is ProgressiveItem progressive)
            {
                entry["currentStage"] = progressive.CurrentStage;
                entry["loop"] = progressive.Loop;
            }

            return entry;
        }
    }
}
