using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
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
        // Phase 6 step 11: app-level helpers resolving the active catalogs
        // through the primary state, with a singleton fallback for the
        // pre-pack-load window.
        static ItemDatabase ActiveItems
        {
            get
            {
                var primary = ApplicationModel.Instance?.PrimaryState?.Items;
                if (primary != null) return primary;
#pragma warning disable CS0618
                return ApplicationModel.Instance?.PrimaryState?.Items;
#pragma warning restore CS0618
            }
        }

        static LocationDatabase ActiveLocations
        {
            get
            {
                var primary = ApplicationModel.Instance?.PrimaryState?.Locations;
                if (primary != null) return primary;
#pragma warning disable CS0618
                return ApplicationModel.Instance?.PrimaryState?.Locations;
#pragma warning restore CS0618
            }
        }

        [McpServerTool(Name = "get_loaded_pack")]
        [Description("Get information about the currently loaded game pack")]
        public static async Task<string> GetLoadedPack()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var pack = ApplicationModel.Instance.ActiveGamePackage;
                if (pack == null)
                    return JsonSerializer.Serialize(new { loaded = false });

                var variant = ApplicationModel.Instance.ActiveGamePackageVariant;
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
                var items = ActiveItems.Items;
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

                    AddItemTypeState(entry, item);

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
                var locations = ActiveLocations.AllLocations;
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
                var items = ActiveItems.Items;
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

                        AddItemTypeState(entry, item);

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
                var item = ActiveItems.FindObjectForCode(code) as ITrackableItem;
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
                var items = ActiveItems.Items;
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
        [Description("Directly set an item's state: active flag for toggles, stage index for progressive, count for consumable. Options can set per-type state (active/stage/consumed).")]
        public static async Task<string> SetItemState(
            [Description("The item name")] string name,
            [Description("For toggle items: 'true' or 'false'. For progressive: stage index (0-based). For consumable: acquired count.")] string value,
            [Description("Set active flag for toggle-like types (progressive_toggle, toggle_badged).")] string active = null,
            [Description("Set stage index for progressive_toggle.")] string stage = null,
            [Description("Set consumed count for consumable items.")] string consumed = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    ITrackableItem item = null;
                    foreach (var i in ActiveItems.Items)
                    {
                        if (i?.Name != null && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            item = i;
                            break;
                        }
                    }

                    if (item == null)
                        return JsonSerializer.Serialize(new { success = false, error = "Item not found" });

                    var transactable = item as EmoTracker.Data.Core.DataModel.TransactableModelTypeBase;
                    if (transactable == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Item is not transactable: {item.GetType().Name}" });

                    using (transactable.OpenTransaction())
                    {
                        if (item is ToggleItem toggle)
                        {
                            if (!bool.TryParse(value, out var boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false for toggle item" });
                            toggle.Active = boolVal;
                        }
                        else if (item is ToggleBadgedItem badged)
                        {
                            if (!bool.TryParse(value, out var boolVal))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected true/false for badged toggle item" });
                            badged.Active = boolVal;
                        }
                        else if (item is ProgressiveItem progressive)
                        {
                            if (!int.TryParse(value, out var stg))
                                return JsonSerializer.Serialize(new { success = false, error = "Expected integer stage index" });
                            progressive.CurrentStage = stg;
                        }
                        else if (item is ProgressiveToggleItem pt)
                        {
                            if (!string.IsNullOrEmpty(active) && bool.TryParse(active, out var av))
                                pt.Active = av;
                            if (!string.IsNullOrEmpty(stage) && uint.TryParse(stage, out var sv))
                                pt.CurrentStage = sv;
                            if (string.IsNullOrEmpty(active) && string.IsNullOrEmpty(stage)
                                && int.TryParse(value, out var stg))
                                pt.CurrentStage = (uint)stg;
                        }
                        else if (item is ConsumableItem consumable)
                        {
                            if (!string.IsNullOrEmpty(consumed) && int.TryParse(consumed, out var consumedVal))
                                consumable.ConsumedCount = consumedVal;
                            if (!string.IsNullOrEmpty(value) && int.TryParse(value, out var count))
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

                    // Open the transaction scope on the primary state's processor —
                    // every item's OnLeftClick mutates its own transactable Active /
                    // CurrentStage via SetTransactableProperty which routes through
                    // the item's OwnerState.Transactions. As long as every item
                    // batched here belongs to the same primary state, opening on
                    // primary.Transactions matches.
                    var primaryProcessor = ApplicationModel.Instance.PrimaryState?.Transactions;
                    if (primaryProcessor == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No active TrackerState" });

                    using (primaryProcessor.OpenTransaction())
                    {
                        foreach (var name in nameList)
                        {
                            ITrackableItem found = null;
                            foreach (var item in ActiveItems.Items)
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

        [McpServerTool(Name = "get_item_codes")]
        [Description("Return the set of codes an item could provide (statically) and whether it currently provides each. Distinguishes static vs dynamic code items.")]
        public static async Task<string> GetItemCodes([Description("The exact item name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in ActiveItems.Items)
                {
                    if (item?.Name != null && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        var all = item.GetAllProvidedCodes();
                        var codes = all != null ? all.ToArray() : null;
                        var providedNow = new List<string>();
                        if (all != null)
                        {
                            foreach (var c in all)
                                if (item.ProvidesCode(c) > 0)
                                    providedNow.Add(c);
                        }
                        return JsonSerializer.Serialize(new
                        {
                            name = item.Name,
                            type = item.GetType().Name,
                            dynamic = all == null,
                            codes,
                            providedNow
                        });
                    }
                }
                return JsonSerializer.Serialize(new { found = false });
            });
        }

        [McpServerTool(Name = "advance_item_to_code")]
        [Description("Advance an item to the stage/state that provides the given code (wraps ItemBase.AdvanceToCode).")]
        public static async Task<string> AdvanceItemToCode(
            [Description("The exact item name")] string name,
            [Description("The code to advance toward")] string code)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in ActiveItems.Items)
                {
                    if (item?.Name != null && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        item.AdvanceToCode(code);
                        return JsonSerializer.Serialize(SerializeItemDetails(item, true));
                    }
                }
                return JsonSerializer.Serialize(new { found = false });
            });
        }

        [McpServerTool(Name = "get_layout_tree")]
        [Description("Walk the loaded pack's layout tree and return every layout node with its C# type, JSON tag, unique id, and key. Lets tests assert all layout element types are present and structurally valid.")]
        public static async Task<string> GetLayoutTree()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var layouts = ApplicationModel.Instance?.PrimaryState?.Layouts;
                    if (layouts == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No layouts manager" });

                    var result = new List<object>();
                    foreach (var kv in layouts.AllLayouts)
                    {
                        var layout = kv.Value;
                        var nodes = new List<object>();
                        WalkLayout(layout.Root, nodes);
                        result.Add(new
                        {
                            key = kv.Key,
                            rootType = layout.Root?.GetType().Name,
                            elementCount = nodes.Count,
                            elements = nodes
                        });
                    }
                    return JsonSerializer.Serialize(new { success = true, layouts = result });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        static void WalkLayout(EmoTracker.Data.Layout.LayoutItem node, List<object> outNodes)
        {
            if (node == null) return;
            outNodes.Add(new
            {
                type = node.GetType().Name,
                uid = node.UniqueID,
                childCount = node.EnumerateChildren()?.Count() ?? 0
            });
            foreach (var child in node.EnumerateChildren())
                WalkLayout(child, outNodes);
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

            AddItemTypeState(entry, item);

            return entry;
        }

        /// <summary>
        /// Adds per-concrete-type observable state so tests can assert on every
        /// item type via the MCP surface. Additive — never removes existing keys.
        /// </summary>
        private static void AddItemTypeState(Dictionary<string, object> entry, ITrackableItem item)
        {
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
            else if (item is ProgressiveToggleItem pt)
            {
                entry["active"] = pt.Active;
                entry["currentStage"] = pt.CurrentStage;
                entry["stageCount"] = pt.StageCount;
                entry["swapActions"] = pt.SwapActions;
            }
            else if (item is ToggleBadgedItem badged)
            {
                entry["active"] = badged.Active;
                entry["baseItem"] = badged.BaseItem?.Name;
            }
            else if (item is SectionChestsProxyItem proxy)
            {
                entry["count"] = proxy.Count;
                var section = proxy.Section;
                if (section != null)
                {
                    entry["section"] = section.Name;
                    entry["chestCount"] = section.ChestCount;
                    entry["availableChestCount"] = section.AvailableChestCount;
                }
            }
            // CompositeToggleItem, StaticItem, BlankItem, LuaItem expose no own
            // mutable state; composite children and lua state are asserted via the
            // sibling items and lua globals respectively.
        }
    }
}
