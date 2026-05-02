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
                            ApplicationModel.Instance.ActivatePackage(pack, v);
                            return JsonSerializer.Serialize(new { success = true, variant = v.DisplayName });
                        }
                        return JsonSerializer.Serialize(new { success = false, error = "Variant not found" });
                    }

                    ApplicationModel.Instance.ActivatePackage(pack, null);
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
                    var locations = ApplicationModel.Instance?.PrimaryState?.Locations?.AllLocations;
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

                    using (found.OpenTransaction())
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

        // Phase 7 XAML migration testing: spawn a fork tab on the active
        // window so cross-tab content swap can be exercised.
        [McpServerTool(Name = "create_fork_tab")]
        [Description("Phase 7 testing: create a fork of the active state and add it as a tab on the active window. Returns the new state's id.")]
        public static async Task<string> CreateForkTab()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var app = ApplicationModel.Instance;
                    var ctx = app.CurrentlyActiveWindowContext;
                    if (ctx == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No active window context" });

                    var pi = app.PackageInstances.FirstOrDefault(p => p.GamePackage != null);
                    if (pi == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No PackageInstance with a loaded pack" });

                    var fork = app.CreateAdditionalState(pi);
                    ctx.AddState(fork, makeActive: true);
                    app.OnActiveStateSwitched(fork);

                    return JsonSerializer.Serialize(new { success = true, stateId = fork.Id.ToString(), name = fork.Name, openTabs = ctx.OpenStates.Count });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "switch_to_tab")]
        [Description("Phase 7 testing: switch to a different tab on the active window by state id. Lists open tab ids on success.")]
        public static async Task<string> SwitchToTab(
            [Description("State id (Guid string) of the tab to activate")] string stateId)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (!Guid.TryParse(stateId, out var id))
                        return JsonSerializer.Serialize(new { success = false, error = "Invalid Guid" });
                    var app = ApplicationModel.Instance;
                    var ctx = app.CurrentlyActiveWindowContext;
                    if (ctx == null)
                        return JsonSerializer.Serialize(new { success = false, error = "No active window" });
                    var target = ctx.OpenStates.FirstOrDefault(s => s.Id == id);
                    if (target == null)
                        return JsonSerializer.Serialize(new { success = false, error = "State not in open tabs" });
                    ctx.ActiveState = target;
                    app.OnActiveStateSwitched(target);
                    return JsonSerializer.Serialize(new { success = true, activeStateId = target.Id.ToString() });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message });
                }
            });
        }

        [McpServerTool(Name = "load_new_pack_via_state_manager")]
        [Description("Phase 7 debug: simulate the state-manager popup's 'Load New Pack' button — calls ApplicationModel.LoadNewPack and adds the resulting state as a tab on the current window. Optionally pass a variant uid.")]
        public static async Task<string> LoadNewPackViaStateManager(
            [Description("The unique ID of the pack to load")] string uniqueId,
            [Description("Optional variant unique ID; pass empty for none")] string variant = null)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var packages = PackageManager.Instance.AvailablePackages;
                    PackageRepositoryEntry found = null;
                    foreach (var entry in packages)
                    {
                        if (entry.UID?.Equals(uniqueId, StringComparison.OrdinalIgnoreCase) == true)
                        { found = entry; break; }
                    }
                    if (found?.ExistingPackage == null)
                        return JsonSerializer.Serialize(new { success = false, error = "pack not installed" });
                    var pack = found.ExistingPackage;
                    EmoTracker.Data.IGamePackageVariant v = null;
                    if (!string.IsNullOrEmpty(variant))
                    {
                        if (pack is EmoTracker.Data.Packages.GamePackage gp)
                            v = gp.FindVariant(variant);
                    }
                    var ctx = ApplicationModel.Instance.CurrentlyActiveWindowContext;
                    if (ctx == null) return JsonSerializer.Serialize(new { success = false, error = "no ctx" });
                    var primary = ApplicationModel.Instance.LoadNewPack(pack, v);
                    ctx.AddState(primary, makeActive: true);
                    ApplicationModel.Instance.OnActiveStateSwitched(primary);
                    return JsonSerializer.Serialize(new { success = true, stateId = primary.Id.ToString(), variant = v?.UniqueID });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = ex.Message, stack = ex.StackTrace });
                }
            });
        }

        [McpServerTool(Name = "list_tabs")]
        [Description("Phase 7 testing: list every open tab on every window with state id, name, and which is active.")]
        public static async Task<string> ListTabs()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var app = ApplicationModel.Instance;
                    var windows = new List<object>();
                    foreach (var w in app.Windows)
                    {
                        var tabs = new List<object>();
                        foreach (var s in w.OpenStates)
                            tabs.Add(new { id = s.Id.ToString(), name = s.Name, active = ReferenceEquals(s, w.ActiveState) });
                        windows.Add(new { windowId = w.Id.ToString(), tabs });
                    }
                    return JsonSerializer.Serialize(new { activeWindowId = app.CurrentlyActiveWindowContext?.Id.ToString(), windows });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

#if DEBUG_PHASE7_INSPECT
        [McpServerTool(Name = "inspect_first_itemgrid")]
        [Description("Phase 7 debug: inspect the first ItemGrid layout-element + its first row's first item.")]
        public static async Task<string> InspectFirstItemGrid()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var ctx = ApplicationModel.Instance.CurrentlyActiveWindowContext;
                    var state = ctx?.ActiveState;
                    var layout = state?.Layouts.FindLayout("shared_item_grid");
                    if (layout?.Root == null) return JsonSerializer.Serialize(new { error = "no layout" });
                    EmoTracker.Data.Layout.ItemGrid grid = null;
                    void Walk(EmoTracker.Data.Layout.LayoutItem n)
                    {
                        if (grid != null) return;
                        if (n is EmoTracker.Data.Layout.ItemGrid g) { grid = g; return; }
                        foreach (var c in n.EnumerateChildren()) Walk(c);
                    }
                    Walk(layout.Root);
                    if (grid == null) return JsonSerializer.Serialize(new { error = "no itemgrid" });
                    var data = grid.Data;
                    var rows = data?.Rows.GetEnumerator();
                    string firstItem = null; int firstHash = 0; bool firstActive = false;
                    if (rows != null && rows.MoveNext())
                    {
                        var row = rows.Current;
                        var rowEnum = row?.GetEnumerator();
                        if (rowEnum != null && rowEnum.MoveNext())
                        {
                            var item = rowEnum.Current;
                            firstItem = item?.Name;
                            firstHash = item?.GetHashCode() ?? 0;
                            if (item is EmoTracker.Data.Items.ToggleItem ti) firstActive = ti.Active;
                            else if (item is EmoTracker.Data.Items.ProgressiveItem pi) firstActive = pi.CurrentStage > 0;
                        }
                    }
                    return JsonSerializer.Serialize(new {
                        stateName = state.Name,
                        gridHash = grid.GetHashCode(),
                        gridOwner = (grid.OwnerState as EmoTracker.Data.Sessions.TrackerState)?.Name,
                        innerDataHash = data?.GetHashCode() ?? 0,
                        firstItemName = firstItem,
                        firstItemHash = firstHash,
                        firstItemActive = firstActive,
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

#endif
#if DEBUG_PHASE7_INSPECT
        [McpServerTool(Name = "inspect_first_item_layout")]
        [Description("Phase 7 debug: inspect the first Item LayoutItem in active state's tracker_horizontal layout.")]
        public static async Task<string> InspectFirstItemLayout()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var ctx = ApplicationModel.Instance.CurrentlyActiveWindowContext;
                    var state = ctx?.ActiveState;
                    var layout = state?.Layouts.FindLayout("shared_item_grid")
                                 ?? state?.Layouts.FindLayout("tracker_horizontal");
                    if (layout?.Root == null) return JsonSerializer.Serialize(new { error = "no layout/root" });
                    var found = FindFirstItem(layout.Root);
                    if (found == null) return JsonSerializer.Serialize(new { error = "no Item element in layout tree" });
                    var data = found.Data;
                    return JsonSerializer.Serialize(new {
                        stateName = state.Name,
                        itemHash = found.GetHashCode(),
                        ownerStateName = (found.OwnerState as EmoTracker.Data.Sessions.TrackerState)?.Name,
                        dataNotNull = data != null,
                        dataType = data?.GetType().FullName,
                        dataName = data?.Name,
                        dataHash = data?.GetHashCode() ?? 0,
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message, stack = ex.StackTrace });
                }
            });
        }

        static EmoTracker.Data.Layout.Item FindFirstItem(EmoTracker.Data.Layout.LayoutItem node)
        {
            if (node is EmoTracker.Data.Layout.Item it) return it;
            foreach (var c in node.EnumerateChildren())
            {
                var found = FindFirstItem(c);
                if (found != null) return found;
            }
            return null;
        }

#endif

#if DEBUG_PHASE7_INSPECT
        [McpServerTool(Name = "inspect_layout_dc")]
        [Description("Phase 7 debug: inspect the active window's TrackerLayout DataContext.")]
        public static async Task<string> InspectLayoutDC()
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var ctx = ApplicationModel.Instance.CurrentlyActiveWindowContext;
                    if (ctx == null) return JsonSerializer.Serialize(new { error = "no ctx" });
                    var mw = ctx.OwnerWindow as MainWindow;
                    if (mw == null) return JsonSerializer.Serialize(new { error = "no mw", ownerType = ctx.OwnerWindow?.GetType().FullName });
                    var tl = mw.FindControl<UI.LayoutControl>("TrackerLayout");
                    if (tl == null) return JsonSerializer.Serialize(new { error = "no tl" });
                    var dc = tl.DataContext;
                    return JsonSerializer.Serialize(new {
                        activeState = ctx.ActiveState?.Name,
                        dcHash = dc?.GetHashCode() ?? 0,
                        dcType = dc?.GetType().FullName,
                        layoutsHash = ctx.ActiveState?.Layouts.FindLayout("tracker_horizontal")?.GetHashCode() ?? 0,
                    });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });
        }

#endif

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
                    var undo = ApplicationModel.Instance.PrimaryState?.Transactions as IUndoableTransactionProcessor;
                    if (undo != null)
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
            var items = ApplicationModel.Instance?.PrimaryState?.Items?.Items;
            if (items == null) return null;
            foreach (var item in items)
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
