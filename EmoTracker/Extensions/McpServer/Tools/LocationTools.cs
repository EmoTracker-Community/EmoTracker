using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
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
    public class LocationTools
    {
        [McpServerTool(Name = "get_location")]
        [Description("Get full details for a location by name, including sections, children, pinned state, badges, and notes")]
        public static async Task<string> GetLocation([Description("The location name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var loc = LocationDatabase.Instance.FindLocation(name);
                if (loc == null)
                    return JsonSerializer.Serialize(new { found = false });

                var sections = new List<object>();
                foreach (var section in loc.Sections)
                {
                    sections.Add(new
                    {
                        name = section.Name,
                        shortName = section.ShortName,
                        chestCount = section.ChestCount,
                        availableChestCount = section.AvailableChestCount,
                        accessibility = section.AccessibilityLevel.ToString(),
                        gateAccessibility = section.GateAccessibilityLevel.ToString(),
                        visible = section.Visible,
                        hasUnclaimedItems = section.HasUnclaimedItems,
                        clearAsGroup = section.ClearAsGroup,
                        captureItem = section.CaptureItem,
                        captureBadge = section.CaptureBadge,
                        captureBadgeOffsetX = section.CaptureBadgeOffsetX,
                        captureBadgeOffsetY = section.CaptureBadgeOffsetY,
                        clearOnCapture = section.ClearOnCapture,
                        hostedItemCode = section.HostedItemCode,
                        capturedItem = section.CapturedItem?.Name
                    });
                }

                var children = new List<object>();
                foreach (var child in loc.Children)
                {
                    children.Add(new
                    {
                        name = child.Name,
                        accessibility = child.AccessibilityLevel.ToString(),
                        availableItemCount = child.AvailableItemCount
                    });
                }

                var notes = new List<object>();
                if (loc.NoteTakingSite != null)
                {
                    foreach (var note in loc.NoteTakingSite.Notes)
                    {
                        if (note is Data.Notes.MarkdownTextNote mdNote)
                        {
                            notes.Add(new
                            {
                                type = note.GetType().Name,
                                text = mdNote.MarkdownSource
                            });
                        }
                        else
                        {
                            notes.Add(new { type = note.GetType().Name });
                        }
                    }
                }

                var badges = new List<object>();
                foreach (var kvp in loc.Badges)
                {
                    badges.Add(new
                    {
                        key = kvp.Key,
                        offsetX = kvp.Value.OffsetX,
                        offsetY = kvp.Value.OffsetY,
                        imageUri = kvp.Value.Image?.ToString()
                    });
                }

                var mapLocations = new List<object>();
                foreach (var map in MapDatabase.Instance.Maps)
                {
                    foreach (var ml in map.Locations)
                    {
                        if (ml.Location == loc)
                        {
                            mapLocations.Add(new
                            {
                                map = map.Name,
                                x = ml.X,
                                y = ml.Y,
                                size = ml.Size,
                                badgeSize = ml.BadgeSize,
                                badgeAlignment = ml.BadgeAlignment.ToString(),
                                badgeOffsetX = ml.BadgeOffsetX,
                                badgeOffsetY = ml.BadgeOffsetY
                            });
                        }
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    found = true,
                    name = loc.Name,
                    shortName = loc.ShortName,
                    accessibility = loc.AccessibilityLevel.ToString(),
                    baseAccessibility = loc.BaseAccessibilityLevel.ToString(),
                    pinned = loc.Pinned,
                    color = loc.Color,
                    hasLocalItems = loc.HasLocalItems,
                    hasAvailableItems = loc.HasAvailableItems,
                    hasBadges = loc.HasBadges,
                    availableItemCount = loc.AvailableItemCount,
                    modifiedByUser = loc.ModifiedByUser,
                    parent = loc.Parent?.Name,
                    sections,
                    badges,
                    mapLocations,
                    children,
                    notes
                });
            });
        }

        [McpServerTool(Name = "pin_location")]
        [Description("Pin a location by name to the pinned locations list")]
        public static async Task<string> PinLocation([Description("The location name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var loc = LocationDatabase.Instance.FindLocation(name);
                    if (loc == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Location '{name}' not found" });

                    LocationDatabase.Instance.PinLocation(loc);
                    return JsonSerializer.Serialize(new { success = true, name = loc.Name, pinned = loc.Pinned });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "unpin_location")]
        [Description("Unpin a location by name from the pinned locations list")]
        public static async Task<string> UnpinLocation([Description("The location name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var loc = LocationDatabase.Instance.FindLocation(name);
                    if (loc == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Location '{name}' not found" });

                    LocationDatabase.Instance.UnpinLocation(loc);
                    return JsonSerializer.Serialize(new { success = true, name = loc.Name, pinned = loc.Pinned });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "list_pinned_locations")]
        [Description("List all currently pinned locations")]
        public static async Task<string> ListPinnedLocations()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var pinned = LocationDatabase.Instance.PinnedLocations;
                var result = new List<object>();
                foreach (var loc in pinned)
                {
                    result.Add(new
                    {
                        name = loc.Name,
                        accessibility = loc.AccessibilityLevel.ToString(),
                        availableItemCount = loc.AvailableItemCount
                    });
                }
                return JsonSerializer.Serialize(result);
            });
        }

        [McpServerTool(Name = "clear_section")]
        [Description("Clear a specific section within a location by decrementing its available chest count")]
        public static async Task<string> ClearSection(
            [Description("The location name")] string locationName,
            [Description("The section name")] string sectionName)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var loc = LocationDatabase.Instance.FindLocation(locationName);
                    if (loc == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Location '{locationName}' not found" });

                    var section = loc.FindSection(sectionName);
                    if (section == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Section '{sectionName}' not found in '{locationName}'" });

                    using (TransactionProcessor.Current.OpenTransaction())
                    {
                        if (section.AvailableChestCount > 0)
                            section.AvailableChestCount = 0;
                        loc.ModifiedByUser = true;
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        location = loc.Name,
                        section = section.Name,
                        chestCount = section.ChestCount,
                        availableChestCount = section.AvailableChestCount,
                        accessibility = section.AccessibilityLevel.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "unclear_location")]
        [Description("Undo a location clear by restoring all section chest counts to their maximums")]
        public static async Task<string> UnclearLocation([Description("The location name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var loc = LocationDatabase.Instance.FindLocation(name);
                    if (loc == null)
                        return JsonSerializer.Serialize(new { success = false, error = $"Location '{name}' not found" });

                    using (TransactionProcessor.Current.OpenTransaction())
                    {
                        foreach (var section in loc.Sections)
                        {
                            section.AvailableChestCount = section.ChestCount;
                        }
                        loc.ModifiedByUser = true;
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        name = loc.Name,
                        accessibility = loc.AccessibilityLevel.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "check_accessibility")]
        [Description("Check a location's computed accessibility level and per-section breakdown")]
        public static async Task<string> CheckAccessibility([Description("The location name")] string name)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var loc = LocationDatabase.Instance.FindLocation(name);
                if (loc == null)
                    return JsonSerializer.Serialize(new { found = false, error = $"Location '{name}' not found" });

                var sections = new List<object>();
                foreach (var section in loc.Sections)
                {
                    sections.Add(new
                    {
                        name = section.Name,
                        accessibility = section.AccessibilityLevel.ToString(),
                        gateAccessibility = section.GateAccessibilityLevel.ToString(),
                        visible = section.Visible,
                        chestCount = section.ChestCount,
                        availableChestCount = section.AvailableChestCount
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    found = true,
                    name = loc.Name,
                    accessibility = loc.AccessibilityLevel.ToString(),
                    baseAccessibility = loc.BaseAccessibilityLevel.ToString(),
                    hasAvailableItems = loc.HasAvailableItems,
                    availableItemCount = loc.AvailableItemCount,
                    sections
                });
            });
        }

        [McpServerTool(Name = "list_accessible_locations")]
        [Description("List locations filtered by accessibility level. Valid levels: None, Partial, Inspect, SequenceBreak, Normal, Cleared")]
        public static async Task<string> ListAccessibleLocations(
            [Description("Filter to this accessibility level (e.g. 'Normal', 'Partial'). If omitted, lists all non-None locations.")] string level = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var locations = LocationDatabase.Instance.AllLocations;
                if (locations == null)
                    return JsonSerializer.Serialize(Array.Empty<object>());

                AccessibilityLevel? filterLevel = null;
                if (!string.IsNullOrEmpty(level))
                {
                    if (Enum.TryParse<AccessibilityLevel>(level, true, out var parsed))
                        filterLevel = parsed;
                    else
                        return JsonSerializer.Serialize(new { error = $"Unknown accessibility level: {level}" });
                }

                var result = new List<object>();
                foreach (var loc in locations)
                {
                    if (loc == null) continue;

                    if (filterLevel.HasValue)
                    {
                        if (loc.AccessibilityLevel != filterLevel.Value)
                            continue;
                    }
                    else
                    {
                        if (loc.AccessibilityLevel == AccessibilityLevel.None)
                            continue;
                    }

                    result.Add(new
                    {
                        name = loc.Name,
                        accessibility = loc.AccessibilityLevel.ToString(),
                        availableItemCount = loc.AvailableItemCount
                    });
                }

                return JsonSerializer.Serialize(result);
            });
        }

        [McpServerTool(Name = "check_code")]
        [Description("Query how many providers satisfy a given code and at what accessibility level. Use '@location/section' for location codes.")]
        public static async Task<string> CheckCode([Description("The code to check (e.g. 'bow', 'hookshot', '@Eastern Palace/Dungeon')")] string code)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var count = Tracker.Instance.ProviderCountForCode(code, out AccessibilityLevel maxAccessibility);
                    return JsonSerializer.Serialize(new
                    {
                        code,
                        providerCount = count,
                        maxAccessibility = maxAccessibility.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }
    }
}
