using EmoTracker.Data;
using EmoTracker.Data.Sessions;
using NLua;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace EmoTracker.Extensions.DeveloperTerminal
{
    /// <summary>
    /// Built-in terminal extension for Lua-interpreter slash commands:
    /// inspecting values, reading globals, timing expressions.
    /// Separated from the pack-tools extension so the Lua surface can
    /// grow independently — future additions like <c>/lua &lt;file&gt;</c>,
    /// expression dump helpers, profiling, etc. all belong here.
    /// </summary>
    public sealed class DeveloperTerminalLuaExtension : ITerminalExtension
    {
        public string Name => "Terminal Lua Tools";
        public string UID => "emotracker_terminal_lua";
        public int Priority => -950;

        TrackerState mState;
        readonly List<TerminalCommand> mCommands = new();

        public IReadOnlyList<TerminalCommand> Commands => mCommands;

        public void OnAttachedToState(TrackerState state)
        {
            mState = state;
            mCommands.Clear();

            mCommands.Add(new TerminalCommand
            {
                Name = "inspect",
                Description = "Pretty-print a Lua expression's value with recursive table expansion. Usage: /inspect <expr>",
                Execute = CmdInspect,
            });

            mCommands.Add(new TerminalCommand
            {
                Name = "global",
                Description = "Pretty-print a Lua global by name. Usage: /global <name>",
                Execute = CmdGlobal,
            });

            mCommands.Add(new TerminalCommand
            {
                Name = "time",
                Description = "Time a Lua expression's wall-clock execution. Usage: /time <expr>",
                Execute = CmdTime,
            });

            mCommands.Add(new TerminalCommand
            {
                Name = "slowcalls",
                Description = "Show top N slowest C#→Lua callbacks recorded since startup (or since /resetslowcalls). Usage: /slowcalls [N=20]",
                Execute = CmdSlowCalls,
            });

            mCommands.Add(new TerminalCommand
            {
                Name = "resetslowcalls",
                Description = "Clear the /slowcalls timing tally.",
                Execute = CmdResetSlowCalls,
            });
        }

        public void OnDetachedFromState(TrackerState state)
        {
            mState = null;
            mCommands.Clear();
        }

        public ITerminalExtension Fork(TrackerState destState)
        {
            return new DeveloperTerminalLuaExtension();
        }

        // ====================================================================
        //  Commands
        // ====================================================================

        void CmdInspect(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var expr = args?.Trim();
            if (string.IsNullOrEmpty(expr))
            {
                state.Scripts.OutputError("Usage: /inspect <expr>");
                return;
            }
            try
            {
                var result = state.Scripts.ExecuteLuaString("return " + expr);
                if (result == null || result.Length == 0)
                {
                    state.Scripts.Output("nil");
                    return;
                }
                foreach (var v in result)
                    state.Scripts.Output(InspectValue(v));
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError(ex.Message);
            }
        }

        void CmdGlobal(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var name = args?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                state.Scripts.OutputError("Usage: /global <name>");
                return;
            }
            try
            {
                var v = state.Scripts.GetLuaGlobal(name);
                state.Scripts.Output($"{name} = {InspectValue(v)}");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError(ex.Message);
            }
        }

        void CmdTime(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            var expr = args?.Trim();
            if (string.IsNullOrEmpty(expr))
            {
                state.Scripts.OutputError("Usage: /time <expr>");
                return;
            }
            try
            {
                var sw = Stopwatch.StartNew();
                object[] result = null;
                try { result = state.Scripts.ExecuteLuaString("return " + expr); }
                catch { result = state.Scripts.ExecuteLuaString(expr); }
                sw.Stop();

                if (result != null && result.Length > 0)
                {
                    foreach (var r in result)
                        state.Scripts.Output(InspectValue(r));
                }
                state.Scripts.Output(
                    $"  → {sw.Elapsed.TotalMilliseconds:F3} ms ({sw.ElapsedTicks} ticks)");
            }
            catch (Exception ex)
            {
                state.Scripts.OutputError(ex.Message);
            }
        }

        void CmdSlowCalls(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;

            int n = 20;
            if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args.Trim(), out int parsed) && parsed > 0)
                n = parsed;

            var snapshot = state.Scripts.GetCallStatsSnapshot()?.ToList();
            if (snapshot == null || snapshot.Count == 0)
            {
                state.Scripts.Output("No call timings recorded. Make sure the app was launched with -dev, then trigger some pack callbacks (item toggles, autotracker activity).");
                return;
            }

            // Sort by total time descending. Tie-break by count so
            // higher-frequency hot spots win when totals match.
            var top = snapshot
                .OrderByDescending(s => s.TotalTicks)
                .ThenByDescending(s => s.Count)
                .Take(n)
                .ToList();

            double freq = Stopwatch.Frequency;

            // Header. Column widths chosen to fit the typical dev
            // terminal width (~120 chars) without wrapping for normal
            // pack callbacks; long location strings will trail off
            // the right edge but stay readable.
            const string fmt = "{0,8} {1,12} {2,10} {3,10}  {4}";
            state.Scripts.Output(string.Format(fmt, "count", "total ms", "avg ms", "max ms", "function"));
            state.Scripts.Output(string.Format(fmt, "-----", "--------", "------", "------", "--------"));

            foreach (var s in top)
            {
                double total = s.TotalTicks / freq * 1000.0;
                double avg = s.Count > 0 ? total / s.Count : 0.0;
                double max = s.MaxTicks / freq * 1000.0;

                string loc = string.IsNullOrEmpty(s.Name)
                    ? string.Format("{0}:{1}", s.Source, s.Line)
                    : string.Format("{0}:{1} ({2})", s.Source, s.Line, s.Name);

                state.Scripts.Output(string.Format(fmt,
                    s.Count,
                    total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    avg.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    max.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    loc));
            }

            state.Scripts.Output(string.Format("({0} callbacks tracked, showing top {1})",
                snapshot.Count, top.Count));
        }

        void CmdResetSlowCalls(TrackerState state, string args)
        {
            if (state?.Scripts == null) return;
            state.Scripts.ResetCallStats();
            state.Scripts.Output("Cleared call timing tally.");
        }

        // ====================================================================
        //  Lua value pretty-printer (depth-limited, cycle-safe).
        //  Internal — exposed as static methods so peer extensions can
        //  reuse the same render shape. Kept on this Lua-tools class so
        //  the rendering policy lives next to the commands that use it.
        // ====================================================================

        const int MaxInspectDepth = 4;

        internal static string InspectValue(object v)
        {
            var sb = new StringBuilder();
            InspectInto(v, sb, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
            return sb.ToString();
        }

        static void InspectInto(object v, StringBuilder sb, int depth, HashSet<object> visited)
        {
            if (v is null) { sb.Append("nil"); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is string s) { sb.Append('"').Append(s).Append('"'); return; }
            if (v is double or float or int or long or uint or ulong or short or ushort or byte or sbyte or decimal)
            {
                sb.Append(System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            if (v is LuaTable t)
            {
                if (visited.Contains(t)) { sb.Append("<cycle>"); return; }
                if (depth >= MaxInspectDepth) { sb.Append("{ … }"); return; }
                visited.Add(t);

                sb.Append('{');
                bool any = false;
                foreach (var key in t.Keys)
                {
                    any = true;
                    sb.Append('\n');
                    sb.Append(' ', (depth + 1) * 2);
                    sb.Append('[').Append(KeyRepresentation(key)).Append("] = ");
                    InspectInto(t[key], sb, depth + 1, visited);
                    sb.Append(',');
                }
                if (any) { sb.Append('\n').Append(' ', depth * 2); }
                sb.Append('}');
                return;
            }

            if (v is LuaFunction)
            {
                sb.Append("<function>");
                return;
            }

            // CLR-side object: just ToString. NLua exposes most things
            // this way (Tracker, Item, Location, etc.).
            sb.Append('<').Append(v.GetType().Name).Append("> ").Append(v);
        }

        static string KeyRepresentation(object key)
        {
            if (key is string ks) return "\"" + ks + "\"";
            return System.Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
