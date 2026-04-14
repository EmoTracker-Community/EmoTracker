using Avalonia.Threading;
using EmoTracker.Data;
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
    /// <summary>
    /// AI-assisted progression tools, backed by TrackerSession.Fork() for
    /// counterfactual "what if I had X?" simulations without disturbing the
    /// current session. See project_progression_tools.md.
    /// </summary>
    [McpServerToolType]
    public class ProgressionTools
    {
        // --- shared helpers ---------------------------------------------

        /// <summary>
        /// Attempt to bring an item to its "fully acquired" state. Returns
        /// true if the mutation was applied (item type recognized). Best-
        /// effort for LuaItem / custom types — relies on AdvanceToCode.
        /// </summary>
        static bool MaximizeItem(ITrackableItem item)
        {
            switch (item)
            {
                case ToggleItem toggle:
                    if (toggle.Active) return false;
                    toggle.Active = true;
                    return true;

                case ConsumableItem consumable:
                    if (consumable.AcquiredCount >= consumable.MaxCount) return false;
                    consumable.AcquiredCount = consumable.MaxCount;
                    return true;

                case ProgressiveItem progressive:
                    var stageCount = progressive.Stages?.Count() ?? 0;
                    if (stageCount == 0) return false;
                    var targetStage = stageCount - 1;
                    if (progressive.CurrentStage >= targetStage) return false;
                    progressive.CurrentStage = targetStage;
                    return true;

                case ItemBase itemBase:
                    // Best-effort fallback (LuaItem and other custom types).
                    // AdvanceToCode(null) typically advances one step; it may
                    // not fully maximize, but it's the only uniform primitive
                    // available on ITrackableItem.
                    try
                    {
                        itemBase.AdvanceToCode(null);
                        return true;
                    }
                    catch { return false; }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Apply a user-specified mutation string to an item. Mirrors the
        /// value semantics of set_item_state so callers of simulate_item_state
        /// can use the same value format.
        /// </summary>
        static bool ApplyItemMutation(ITrackableItem item, string value, out string error)
        {
            error = null;
            switch (item)
            {
                case ToggleItem toggle:
                    if (!bool.TryParse(value, out var b))
                    {
                        error = "Expected true/false for toggle item";
                        return false;
                    }
                    toggle.Active = b;
                    return true;

                case ConsumableItem consumable:
                    if (!int.TryParse(value, out var c))
                    {
                        error = "Expected integer for consumable item";
                        return false;
                    }
                    consumable.AcquiredCount = c;
                    return true;

                case ProgressiveItem progressive:
                    if (!int.TryParse(value, out var stage))
                    {
                        error = "Expected stage index for progressive item";
                        return false;
                    }
                    progressive.CurrentStage = stage;
                    return true;

                default:
                    error = $"Item type {item.GetType().Name} not mutable via simulate_item_state";
                    return false;
            }
        }

        /// <summary>
        /// Snapshot accessibility levels for every location in the current
        /// session. Keyed by location name for post-fork comparison.
        /// </summary>
        static Dictionary<string, AccessibilityLevel> SnapshotAccessibility()
        {
            var snap = new Dictionary<string, AccessibilityLevel>();
            foreach (var loc in TrackerSession.Current.Locations.AllLocations)
            {
                if (loc?.Name != null)
                    snap[loc.Name] = loc.AccessibilityLevel;
            }
            return snap;
        }

        static bool IsProgressable(AccessibilityLevel level)
        {
            return level == AccessibilityLevel.Normal
                || level == AccessibilityLevel.SequenceBreak
                || level == AccessibilityLevel.Unlockable
                || level == AccessibilityLevel.Partial;
        }

        static int StateDescriptor(ITrackableItem item)
        {
            return item switch
            {
                ToggleItem t => t.Active ? 1 : 0,
                ConsumableItem c => c.AcquiredCount,
                ProgressiveItem p => p.CurrentStage,
                _ => 0
            };
        }

        static bool IsAtMax(ITrackableItem item)
        {
            return item switch
            {
                ToggleItem t => t.Active,
                ConsumableItem c => c.AcquiredCount >= c.MaxCount,
                ProgressiveItem p => p.CurrentStage >= (p.Stages?.Count() ?? 0) - 1,
                _ => false
            };
        }

        // --- Tool 1: session overview -----------------------------------

        [McpServerTool(Name = "get_session_overview")]
        [Description("High-level snapshot of the current tracker session: pack info, item-type histogram, location accessibility histogram, pinned locations, total unclaimed items. Use this first to orient yourself before calling more targeted progression tools.")]
        public static async Task<string> GetSessionOverview()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var session = TrackerSession.Current;
                var pack = session.Tracker.ActiveGamePackage;
                if (pack == null)
                    return JsonSerializer.Serialize(new { loaded = false });

                int toggleCount = 0, toggleActive = 0;
                int consumableCount = 0, consumableAcquired = 0, consumableMax = 0;
                int progressiveCount = 0, progressiveStageSum = 0, progressiveMaxSum = 0;
                int luaCount = 0, otherCount = 0;

                foreach (var item in session.Items.Items)
                {
                    switch (item)
                    {
                        case ToggleItem t:
                            toggleCount++;
                            if (t.Active) toggleActive++;
                            break;
                        case ConsumableItem c:
                            consumableCount++;
                            consumableAcquired += c.AcquiredCount;
                            consumableMax += (c.MaxCount == int.MaxValue ? 0 : c.MaxCount);
                            break;
                        case ProgressiveItem p:
                            progressiveCount++;
                            progressiveStageSum += p.CurrentStage;
                            var stages = p.Stages?.Count() ?? 0;
                            progressiveMaxSum += Math.Max(0, stages - 1);
                            break;
                        case Data.Scripting.LuaItem _:
                            luaCount++;
                            break;
                        default:
                            otherCount++;
                            break;
                    }
                }

                var histogram = new Dictionary<string, int>
                {
                    ["None"] = 0,
                    ["Cleared"] = 0,
                    ["Inspect"] = 0,
                    ["Partial"] = 0,
                    ["SequenceBreak"] = 0,
                    ["Unlockable"] = 0,
                    ["Normal"] = 0,
                };
                int totalUnclaimed = 0;
                int locationCount = 0;
                foreach (var loc in session.Locations.AllLocations)
                {
                    if (loc == null) continue;
                    locationCount++;
                    var key = loc.AccessibilityLevel.ToString();
                    if (histogram.ContainsKey(key)) histogram[key]++;
                    totalUnclaimed += (int)loc.AvailableItemCount;
                }

                var pinned = new List<object>();
                foreach (var loc in session.Locations.PinnedLocations)
                {
                    pinned.Add(new
                    {
                        name = loc.Name,
                        accessibility = loc.AccessibilityLevel.ToString(),
                        availableItemCount = loc.AvailableItemCount
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    loaded = true,
                    pack = new
                    {
                        displayName = pack.DisplayName,
                        uniqueId = pack.UniqueID,
                        variant = session.Tracker.ActiveVariantUID
                    },
                    items = new
                    {
                        toggles = new { count = toggleCount, active = toggleActive },
                        consumables = new { count = consumableCount, acquired = consumableAcquired, max = consumableMax },
                        progressives = new { count = progressiveCount, currentStages = progressiveStageSum, maxStages = progressiveMaxSum },
                        luaItems = luaCount,
                        other = otherCount
                    },
                    locations = new
                    {
                        count = locationCount,
                        accessibilityHistogram = histogram,
                        totalUnclaimedItems = totalUnclaimed
                    },
                    pinned
                });
            });
        }

        // --- Tool 2: blocking items -------------------------------------

        [McpServerTool(Name = "get_blocking_items")]
        [Description("Scan every inaccessible location/section with unclaimed items and return the set of item codes their rules reference but aren't currently provided. Returns a map of code -> list of blocked locations. This is a static analysis of the rule set against current state; it does not simulate what would change if you had those items.")]
        public static async Task<string> GetBlockingItems()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var session = TrackerSession.Current;
                var codeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                void RecordCode(string code, string locationName)
                {
                    if (string.IsNullOrWhiteSpace(code)) return;
                    // Currently provided? skip — not blocking.
                    AccessibilityLevel _unused;
                    if (session.Tracker.ProviderCountForCode(code, out _unused) > 0)
                        return;

                    if (!codeMap.TryGetValue(code, out var list))
                    {
                        list = new List<string>();
                        codeMap[code] = list;
                    }
                    if (!list.Contains(locationName))
                        list.Add(locationName);
                }

                foreach (var loc in session.Locations.AllLocations)
                {
                    if (loc == null) continue;
                    if (loc.AccessibilityLevel != AccessibilityLevel.None) continue;

                    foreach (var section in loc.Sections)
                    {
                        if (!section.Visible) continue;
                        if (!section.HasUnclaimedItems) continue;

                        foreach (var rs in new[] { section.AccessibilityRules, section.GateAccessibilityRules })
                        {
                            if (rs == null) continue;
                            foreach (var rule in rs.Rules)
                            {
                                foreach (var code in rule.Codes)
                                    RecordCode(code.mCode, loc.Name);
                            }
                        }
                    }

                    if (loc.AccessibilityRules != null)
                    {
                        foreach (var rule in loc.AccessibilityRules.Rules)
                        {
                            foreach (var code in rule.Codes)
                                RecordCode(code.mCode, loc.Name);
                        }
                    }
                }

                var result = codeMap
                    .OrderByDescending(kv => kv.Value.Count)
                    .Select(kv => new
                    {
                        code = kv.Key,
                        blockingLocationCount = kv.Value.Count,
                        locations = kv.Value
                    })
                    .ToArray();

                return JsonSerializer.Serialize(new { codes = result });
            });
        }

        // --- Tool 3: rank items by unlock potential ---------------------

        [McpServerTool(Name = "rank_items_by_unlock_potential")]
        [Description("For each item that is not yet at its max state, fork the session, maximize that item alone, recompute accessibility, and count how many locations would improve. Returns items sorted descending by improvement count. EXPENSIVE: O(num_candidates × fork cost). Pass a filter to constrain. Fork cost scales with pack Lua size.")]
        public static async Task<string> RankItemsByUnlockPotential(
            [Description("Optional comma-separated list of item names to restrict to (case-insensitive). Empty = all non-maxed items.")] string filter = null,
            [Description("Max number of results to return (default 20)")] int limit = 20)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var session = TrackerSession.Current;
                if (session.Tracker.ActiveGamePackage == null)
                    return JsonSerializer.Serialize(new { error = "No pack loaded" });

                HashSet<string> allowed = null;
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    allowed = new HashSet<string>(
                        filter.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim()),
                        StringComparer.OrdinalIgnoreCase);
                }

                var baseline = SnapshotAccessibility();

                var results = new List<(string name, string type, int before, int after, int delta, int newlyProgressable, int newlyNormal)>();

                foreach (var item in session.Items.Items.ToArray())
                {
                    if (item?.Name == null) continue;
                    if (allowed != null && !allowed.Contains(item.Name)) continue;
                    if (IsAtMax(item)) continue;

                    TrackerSession fork;
                    try { fork = session.Fork(); }
                    catch
                    {
                        results.Add((item.Name, item.GetType().Name, 0, 0, 0, 0, 0));
                        continue;
                    }

                    int delta = 0, newlyProgressable = 0, newlyNormal = 0, before = 0, after = 0;
                    try
                    {
                        using (fork.EnterScope())
                        {
                            // Find the same item in the fork — shared instance, so reference equality works.
                            if (!MaximizeItem(item)) continue;
                            fork.Locations.RefeshAccessibility();

                            foreach (var loc in fork.Locations.AllLocations)
                            {
                                if (loc?.Name == null) continue;
                                if (!baseline.TryGetValue(loc.Name, out var was)) continue;
                                var now = loc.AccessibilityLevel;
                                if (IsProgressable(was)) before++;
                                if (IsProgressable(now)) after++;
                                if ((int)now > (int)was) delta++;
                                if (!IsProgressable(was) && IsProgressable(now)) newlyProgressable++;
                                if (was != AccessibilityLevel.Normal && now == AccessibilityLevel.Normal) newlyNormal++;
                            }
                        }
                    }
                    finally
                    {
                        // Fork state drops out of scope; no explicit teardown needed.
                        // ScriptManager's fresh NLua instance is GC-reclaimed once
                        // the fork session is unreachable.
                    }

                    results.Add((item.Name, item.GetType().Name, before, after, delta, newlyProgressable, newlyNormal));
                }

                var sorted = results
                    .Where(r => r.delta > 0 || r.newlyProgressable > 0)
                    .OrderByDescending(r => r.newlyProgressable)
                    .ThenByDescending(r => r.delta)
                    .ThenByDescending(r => r.newlyNormal)
                    .Take(limit)
                    .Select(r => new
                    {
                        name = r.name,
                        type = r.type,
                        improvedLocationCount = r.delta,
                        newlyProgressableCount = r.newlyProgressable,
                        newlyNormalCount = r.newlyNormal,
                        progressableBefore = r.before,
                        progressableAfter = r.after
                    })
                    .ToArray();

                return JsonSerializer.Serialize(new
                {
                    candidates = sorted,
                    totalEvaluated = results.Count
                });
            });
        }

        // --- Tool 4: simulate_item_state --------------------------------

        public class SimulationEntry
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }

        [McpServerTool(Name = "simulate_item_state")]
        [Description("Apply a batch of item mutations inside a fork, recompute accessibility, return the diff: locations whose accessibility changed. Input is a JSON array string like '[{\"name\":\"Bow\",\"value\":\"true\"},{\"name\":\"Master Sword\",\"value\":\"2\"}]'. Value semantics match set_item_state: 'true'/'false' for toggles, integer for consumables, stage index for progressives.")]
        public static async Task<string> SimulateItemState(
            [Description("JSON array of {name, value} mutation pairs.")] string mutationsJson)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                List<SimulationEntry> mutations;
                try
                {
                    mutations = JsonSerializer.Deserialize<List<SimulationEntry>>(mutationsJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception e)
                {
                    return JsonSerializer.Serialize(new { success = false, error = $"Could not parse mutationsJson: {e.Message}" });
                }

                if (mutations == null || mutations.Count == 0)
                    return JsonSerializer.Serialize(new { success = false, error = "Empty mutation list" });

                var session = TrackerSession.Current;
                if (session.Tracker.ActiveGamePackage == null)
                    return JsonSerializer.Serialize(new { success = false, error = "No pack loaded" });

                var baseline = SnapshotAccessibility();

                TrackerSession fork;
                try { fork = session.Fork(); }
                catch (Exception e)
                {
                    return JsonSerializer.Serialize(new { success = false, error = $"Fork failed: {e.Message}" });
                }

                var applied = new List<object>();
                var errors = new List<object>();

                using (fork.EnterScope())
                {
                    foreach (var m in mutations)
                    {
                        if (string.IsNullOrWhiteSpace(m?.Name))
                        {
                            errors.Add(new { name = m?.Name, error = "Missing name" });
                            continue;
                        }

                        ITrackableItem match = null;
                        foreach (var it in fork.Items.Items)
                        {
                            if (it?.Name != null && it.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                match = it;
                                break;
                            }
                        }

                        if (match == null)
                        {
                            errors.Add(new { name = m.Name, error = "Item not found" });
                            continue;
                        }

                        if (!ApplyItemMutation(match, m.Value, out var err))
                        {
                            errors.Add(new { name = m.Name, error = err });
                            continue;
                        }

                        applied.Add(new
                        {
                            name = match.Name,
                            type = match.GetType().Name,
                            value = m.Value,
                            newState = StateDescriptor(match)
                        });
                    }

                    fork.Locations.RefeshAccessibility();

                    var diffs = new List<object>();
                    foreach (var loc in fork.Locations.AllLocations)
                    {
                        if (loc?.Name == null) continue;
                        if (!baseline.TryGetValue(loc.Name, out var was)) continue;
                        var now = loc.AccessibilityLevel;
                        if (was != now)
                        {
                            diffs.Add(new
                            {
                                name = loc.Name,
                                before = was.ToString(),
                                after = now.ToString(),
                                availableItemCount = loc.AvailableItemCount
                            });
                        }
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        applied,
                        errors,
                        locationsChanged = diffs.Count,
                        changes = diffs
                    });
                }
            });
        }

        // --- Tool 5: progression summary --------------------------------

        [McpServerTool(Name = "get_progression_summary")]
        [Description("Narrative 'what should I do next?' combining the other progression tools. Reports: (a) currently accessible locations with unclaimed items (immediate gains), (b) top N items whose acquisition would most unlock the map (via rank_items_by_unlock_potential). Runs the expensive ranking — prefer this single call over multiple small ones when asking the AI for a recommendation.")]
        public static async Task<string> GetProgressionSummary(
            [Description("Max unlock candidates to return (default 5)")] int topCandidates = 5,
            [Description("Max immediate-gain locations to return (default 15)")] int maxImmediate = 15)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var session = TrackerSession.Current;
                if (session.Tracker.ActiveGamePackage == null)
                    return JsonSerializer.Serialize(new { error = "No pack loaded" });

                // Immediate gains: locations currently progressable with unclaimed items.
                var immediate = new List<object>();
                foreach (var loc in session.Locations.AllLocations)
                {
                    if (loc == null) continue;
                    if (!IsProgressable(loc.AccessibilityLevel)) continue;
                    if (loc.AvailableItemCount == 0) continue;

                    immediate.Add(new
                    {
                        name = loc.Name,
                        accessibility = loc.AccessibilityLevel.ToString(),
                        availableItemCount = loc.AvailableItemCount,
                        pinned = loc.Pinned
                    });
                }
                immediate = immediate
                    .OrderByDescending(o => (uint)o.GetType().GetProperty("availableItemCount").GetValue(o))
                    .Take(maxImmediate)
                    .ToList();

                // Unlock candidates: reuse the ranking tool's logic inline to
                // avoid double-forking.
                var baseline = SnapshotAccessibility();
                var rankings = new List<(string name, string type, int delta, int newlyProgressable)>();

                foreach (var item in session.Items.Items.ToArray())
                {
                    if (item?.Name == null) continue;
                    if (IsAtMax(item)) continue;

                    TrackerSession fork;
                    try { fork = session.Fork(); }
                    catch { continue; }

                    int delta = 0, newlyProgressable = 0;
                    using (fork.EnterScope())
                    {
                        if (!MaximizeItem(item)) continue;
                        fork.Locations.RefeshAccessibility();

                        foreach (var loc in fork.Locations.AllLocations)
                        {
                            if (loc?.Name == null) continue;
                            if (!baseline.TryGetValue(loc.Name, out var was)) continue;
                            var now = loc.AccessibilityLevel;
                            if ((int)now > (int)was) delta++;
                            if (!IsProgressable(was) && IsProgressable(now)) newlyProgressable++;
                        }
                    }

                    if (delta > 0 || newlyProgressable > 0)
                        rankings.Add((item.Name, item.GetType().Name, delta, newlyProgressable));
                }

                var topItems = rankings
                    .OrderByDescending(r => r.newlyProgressable)
                    .ThenByDescending(r => r.delta)
                    .Take(topCandidates)
                    .Select(r => new
                    {
                        name = r.name,
                        type = r.type,
                        improvedLocationCount = r.delta,
                        newlyProgressableCount = r.newlyProgressable
                    })
                    .ToArray();

                return JsonSerializer.Serialize(new
                {
                    immediateGains = new
                    {
                        count = immediate.Count,
                        locations = immediate
                    },
                    topUnlockCandidates = topItems,
                    totalItemsEvaluated = rankings.Count
                });
            });
        }
    }
}
