using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
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
                var pack = Tracker.Instance.ActiveGamePackage;
                if (pack == null)
                    return JsonSerializer.Serialize(new { loaded = false });

                var variant = Tracker.Instance.ActiveGamePackageVariant;
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
                var items = ItemDatabase.Instance.Items;
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
                var locations = LocationDatabase.Instance.AllLocations;
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
                var items = ItemDatabase.Instance.Items;
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
    }
}
