using EmoTracker.Data;
using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EmoTracker.Extensions.DeveloperTerminal
{
    /// <summary>
    /// Built-in terminal extension shipping the pack-development
    /// command set: catalog inspection, pack-load flow, state
    /// management, per-state settings.
    /// <list type="bullet">
    ///   <item>Universal terminal-mechanics commands (clear / help /
    ///         echo / history) live on
    ///         <see cref="DeveloperTerminalCoreExtension"/>.</item>
    ///   <item>Lua-interpreter commands (inspect / global / time)
    ///         live on
    ///         <see cref="DeveloperTerminalLuaExtension"/>.</item>
    /// </list>
    /// Splitting along these lines lets each surface grow independently
    /// without bloating the others.
    /// </summary>
    public sealed class DeveloperTerminalPackToolsExtension : ITerminalExtension
    {
        public string Name => "Terminal Pack Tools";
        public string UID => "emotracker_terminal_pack_tools";
        public int Priority => -900;

        TrackerState mState;
        readonly List<TerminalCommand> mCommands = new();

        public IReadOnlyList<TerminalCommand> Commands => mCommands;

        public void OnAttachedToState(TrackerState state)
        {
            mState = state;
            mCommands.Clear();
            BuildCommands();
        }

        public void OnDetachedFromState(TrackerState state)
        {
            mState = null;
            mCommands.Clear();
        }

        public ITerminalExtension Fork(TrackerState destState)
        {
            return new DeveloperTerminalPackToolsExtension();
        }

        // -----------------------------------------------------------------
        //  Command table
        // -----------------------------------------------------------------

        void BuildCommands()
        {
            // ---- Inspection / discovery ---------------------------------
            mCommands.Add(new TerminalCommand
            {
                Name = "state",
                Description = "Show bound state info (id, name, pack, variant, dirty flag, catalog sizes).",
                Execute = CmdState,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "pack",
                Description = "Show loaded pack metadata (name, UID, author, version, platform, available variants).",
                Execute = CmdPack,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "find",
                Description = "Find an item or location by code. Usage: /find <code>",
                Execute = CmdFind,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "items",
                Description = "List items in the bound state's catalog. Usage: /items [filter]",
                Execute = CmdItems,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "locations",
                Description = "List locations in the bound state's catalog. Usage: /locations [filter]",
                Execute = CmdLocations,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "extensions",
                Description = "List extensions across all four scopes (app/window/package/tracker) for the bound state.",
                Execute = CmdExtensions,
            });

            // ---- Pack-load flow -----------------------------------------
            mCommands.Add(new TerminalCommand
            {
                Name = "reload",
                Description = "Re-run pack load on the bound state.",
                Execute = CmdReload,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "variant",
                Description = "Switch the bound state's variant. Usage: /variant <variantUid>",
                Execute = CmdVariant,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "refreshacc",
                Description = "Force a LocationDatabase.RefeshAccessibility() on the bound state.",
                Execute = CmdRefreshAccessibility,
            });

            // ---- State management ---------------------------------------
            mCommands.Add(new TerminalCommand
            {
                Name = "undo",
                Description = "Invoke the bound state's transaction processor undo.",
                Execute = CmdUndo,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "save",
                Description = "Save the bound state's progress. Usage: /save <path>",
                Execute = CmdSave,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "load",
                Description = "Load a save into the bound state. Usage: /load <path>",
                Execute = CmdLoad,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "fork",
                Description = "Fork the bound state into a new tab on the active window. Usage: /fork [name]",
                Execute = CmdFork,
            });

            // ---- Settings -----------------------------------------------
            mCommands.Add(new TerminalCommand
            {
                Name = "set",
                Description = "Set a per-state SessionSettings flag. Usage: /set <key> <true|false>",
                Execute = CmdSet,
            });
            mCommands.Add(new TerminalCommand
            {
                Name = "get",
                Description = "Read a per-state SessionSettings flag. Usage: /get <key>",
                Execute = CmdGet,
            });
        }

        // ====================================================================
        //  Inspection / discovery
        // ====================================================================

        void CmdState(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var pi = state.PackageInstance;
            var pkg = pi?.GamePackage;
            state.Scripts.Output($"State id    : {state.Id}");
            state.Scripts.Output($"State name  : {state.Name ?? "(unnamed)"}");
            state.Scripts.Output($"Dirty       : {state.IsDirty}");
            state.Scripts.Output($"Pack        : {pkg?.DisplayName ?? "(none)"}{(pkg != null ? " (" + pkg.UniqueID + ")" : "")}");
            state.Scripts.Output($"Variant     : {pi?.ActiveVariant?.DisplayName ?? "(none)"}{(pi?.ActiveVariant != null ? " (" + pi.ActiveVariant.UniqueID + ")" : "")}");
            state.Scripts.Output($"Items       : {state.Items.Items.Count()}");
            state.Scripts.Output($"Locations   : {state.Locations.AllLocations.Count()}");
            state.Scripts.Output($"Maps        : {state.Maps.Maps.Count()}");
        }

        void CmdPack(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var pi = state.PackageInstance;
            var pkg = pi?.GamePackage;
            if (pkg == null)
            {
                state.Scripts.OutputWarning("No pack loaded on this state.");
                return;
            }
            state.Scripts.Output($"Display name : {pkg.DisplayName}");
            state.Scripts.Output($"Unique ID    : {pkg.UniqueID}");
            state.Scripts.Output($"Author       : {pkg.Author}");
            state.Scripts.Output($"Version      : {pkg.Version}");
            state.Scripts.Output($"Platform     : {pkg.Platform}");
            state.Scripts.Output($"Game         : {pkg.Game}");
            state.Scripts.Output($"Active variant: {pi.ActiveVariant?.DisplayName ?? "(none)"} ({pi.ActiveVariant?.UniqueID ?? "-"})");
            var variants = pkg.AvailableVariants?.ToList();
            if (variants != null && variants.Count > 0)
            {
                state.Scripts.Output("Available variants:");
                foreach (var v in variants)
                {
                    var marker = ReferenceEquals(v, pi.ActiveVariant) ? "* " : "  ";
                    state.Scripts.Output($"  {marker}{v.DisplayName} ({v.UniqueID})");
                }
            }
        }

        void CmdFind(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var code = args?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                state.Scripts.OutputError("Usage: /find <code>");
                return;
            }

            int hits = 0;
            // Items by code (provider-count match).
            foreach (var item in state.Items.Items.OfType<ItemBase>())
            {
                uint count = item.ProvidesCode(code);
                if (count > 0)
                {
                    state.Scripts.Output(
                        $"item   {item.GetType().Name,-22} name=\"{item.Name}\" provides count={count}");
                    hits++;
                }
            }
            // Locations whose name matches (locations don't have a single
            // string identifier — they're identified by structural path —
            // so we match against Name as the closest user-facing handle).
            foreach (var loc in state.Locations.AllLocations)
            {
                if ((loc.Name ?? "").Contains(code, StringComparison.OrdinalIgnoreCase))
                {
                    state.Scripts.Output(
                        $"loc    name=\"{loc.Name}\"  accessibility={loc.AccessibilityLevel}");
                    hits++;
                }
            }
            if (hits == 0)
                state.Scripts.OutputWarning($"No matches for code: {code}");
        }

        void CmdItems(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            string filter = args?.Trim() ?? "";
            int shown = 0;
            foreach (var item in state.Items.Items.OfType<ItemBase>())
            {
                if (filter.Length > 0 &&
                    !((item.Name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    continue;
                state.Scripts.Output($"  {item.GetType().Name,-22} \"{item.Name}\"");
                shown++;
                if (shown >= 200)
                {
                    state.Scripts.Output($"  … (truncated at 200)");
                    return;
                }
            }
            state.Scripts.Output($"({shown} item{(shown == 1 ? "" : "s")})");
        }

        void CmdLocations(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            string filter = args?.Trim() ?? "";
            int shown = 0;
            foreach (var loc in state.Locations.AllLocations)
            {
                if (filter.Length > 0 &&
                    !((loc.Name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    continue;
                int sectionCount = 0; foreach (var _ in loc.Sections) sectionCount++;
                state.Scripts.Output(
                    $"  {loc.AccessibilityLevel,-15} \"{loc.Name}\"  (sections={sectionCount})");
                shown++;
                if (shown >= 200)
                {
                    state.Scripts.Output($"  … (truncated at 200)");
                    return;
                }
            }
            state.Scripts.Output($"({shown} location{(shown == 1 ? "" : "s")})");
        }

        void CmdExtensions(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var mgr = ExtensionManager.Instance;
            var win = ApplicationModel.Instance.CurrentlyActiveWindowContext;

            void DumpScope(string label, IEnumerable<IExtension> exts)
            {
                var list = exts?.ToList();
                if (list == null || list.Count == 0)
                {
                    state.Scripts.Output($"  [{label}] (none)");
                    return;
                }
                foreach (var e in list.OrderBy(x => x.Priority))
                    state.Scripts.Output($"  [{label}] prio={e.Priority,5}  {e.Name}  ({e.UID})");
            }

            state.Scripts.Output("Application:");
            DumpScope("application", mgr.ApplicationExtensions);
            state.Scripts.Output("Window (active):");
            DumpScope("window", mgr.GetWindowExtensions(win));
            state.Scripts.Output("Package (state's pack):");
            DumpScope("package", mgr.GetPackageExtensions(state.PackageInstance));
            state.Scripts.Output("Tracker (state):");
            DumpScope("tracker", mgr.GetTrackerExtensions(state));
            state.Scripts.Output("Terminal (state):");
            DumpScope("terminal", mgr.GetTerminalExtensions(state));
        }

        // ====================================================================
        //  Pack-load flow
        // ====================================================================

        void CmdReload(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            try
            {
                state.Reload();
                state.Scripts.Output("Reload complete.");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Reload failed: " + ex.Message);
            }
        }

        void CmdVariant(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var uid = args?.Trim();
            if (string.IsNullOrEmpty(uid))
            {
                state.Scripts.OutputError("Usage: /variant <variantUid>");
                return;
            }
            var pkg = state.PackageInstance?.GamePackage;
            if (pkg == null) { state.Scripts.OutputError("No pack loaded."); return; }

            var variant = pkg.FindVariant(uid);
            if (variant == null)
            {
                state.Scripts.OutputError($"Unknown variant: {uid}");
                state.Scripts.Output("Available:");
                foreach (var v in pkg.AvailableVariants ?? Enumerable.Empty<IGamePackageVariant>())
                    state.Scripts.Output($"  {v.UniqueID} — {v.DisplayName}");
                return;
            }

            try
            {
                state.ActivatePackage(pkg, variant);
                state.Scripts.Output($"Switched variant to {variant.DisplayName} ({variant.UniqueID}).");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Variant switch failed: " + ex.Message);
            }
        }

        void CmdRefreshAccessibility(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            try
            {
                state.Locations.RefreshAccessibility();
                state.Scripts.Output("Accessibility refreshed.");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Accessibility refresh failed: " + ex.Message);
            }
        }

        // ====================================================================
        //  State management
        // ====================================================================

        void CmdUndo(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            try
            {
                // state.Transactions is already typed as
                // IUndoableTransactionProcessor (see TrackerState.cs);
                // no cast needed.
                state.Transactions.Undo();
                state.Scripts.Output("Undid last transaction.");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Undo failed: " + ex.Message);
            }
        }

        void CmdSave(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var path = args?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                state.Scripts.OutputError("Usage: /save <path>");
                return;
            }
            try
            {
                bool ok = state.SaveProgress(path);
                state.Scripts.Output(ok ? $"Saved to {path}." : "Save returned false.");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Save failed: " + ex.Message);
            }
        }

        void CmdLoad(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var path = args?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                state.Scripts.OutputError("Usage: /load <path>");
                return;
            }
            try
            {
                bool ok = state.LoadProgress(path);
                state.Scripts.Output(ok ? $"Loaded {path}." : "Load returned false.");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Load failed: " + ex.Message);
            }
        }

        void CmdFork(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var name = args?.Trim();
            try
            {
                var app = ApplicationModel.Instance;
                var ctx = app.CurrentlyActiveWindowContext;
                if (ctx == null)
                {
                    state.Scripts.OutputError("No active window context.");
                    return;
                }
                var pi = state.PackageInstance;
                if (pi == null)
                {
                    state.Scripts.OutputError("Bound state is not associated with a PackageInstance.");
                    return;
                }
                var fork = string.IsNullOrEmpty(name)
                    ? app.CreateAdditionalState(pi)
                    : app.CreateAdditionalState(pi, name);
                ctx.AddState(fork, makeActive: true);
                app.OnActiveStateSwitched(fork);
                state.Scripts.Output($"Forked to new tab: {fork.Name} (id={fork.Id}).");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Fork failed: " + ex.Message);
            }
        }

        // ====================================================================
        //  Settings (per-state SessionSettings)
        // ====================================================================

        // Names of settable bool properties on SessionSettings. We
        // reflect each call (small set, infrequent invocation, no
        // hot-path concern).
        static readonly string[] SessionSettingsKeys =
        {
            nameof(SessionSettings.IgnoreAllLogic),
            nameof(SessionSettings.DisplayAllLocations),
            nameof(SessionSettings.AlwaysAllowClearing),
            nameof(SessionSettings.AutoUnpinLocationsOnClear),
            nameof(SessionSettings.PinLocationsOnItemCapture),
            nameof(SessionSettings.MapEnabled),
            nameof(SessionSettings.SwapLeftRight),
        };

        void CmdSet(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var parts = (args ?? "").Trim().Split(new[] { ' ', '\t' }, 2,
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                state.Scripts.OutputError("Usage: /set <key> <true|false>");
                state.Scripts.Output("Available keys: " + string.Join(", ", SessionSettingsKeys));
                return;
            }

            var key = parts[0];
            var rawVal = parts[1];

            var prop = ResolveSessionSettingsProperty(state, key);
            if (prop == null) return;

            if (!bool.TryParse(rawVal, out bool boolVal))
            {
                state.Scripts.OutputError($"Value must be true or false (got: {rawVal})");
                return;
            }
            try
            {
                prop.SetValue(state.Settings, boolVal);
                state.Scripts.Output($"{prop.Name} = {boolVal}");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Set failed: " + ex.Message);
            }
        }

        void CmdGet(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var key = args?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                state.Scripts.Output("Per-state settings:");
                foreach (var k in SessionSettingsKeys)
                {
                    var p = typeof(SessionSettings).GetProperty(k);
                    var v = p?.GetValue(state.Settings);
                    state.Scripts.Output($"  {k} = {v}");
                }
                return;
            }
            var prop = ResolveSessionSettingsProperty(state, key);
            if (prop == null) return;
            try
            {
                var v = prop.GetValue(state.Settings);
                state.Scripts.Output($"{prop.Name} = {v}");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError("Get failed: " + ex.Message);
            }
        }

        PropertyInfo ResolveSessionSettingsProperty(TrackerState state, string key)
        {
            // Case-insensitive match against the known keys. Not purely
            // typeof(SessionSettings).GetProperty so we can tolerate the
            // user's typing without forcing exact case.
            var match = SessionSettingsKeys.FirstOrDefault(
                k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                state.Scripts.OutputError($"Unknown setting: {key}");
                state.Scripts.Output("Available: " + string.Join(", ", SessionSettingsKeys));
                return null;
            }
            return typeof(SessionSettings).GetProperty(match);
        }

    }
}
