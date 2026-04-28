using EmoTracker.Data.Sessions;
using NLua;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
