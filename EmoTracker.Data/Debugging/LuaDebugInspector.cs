using KeraLua;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Debugging
{
    /// <summary>
    /// Builds DAP responses (stack frames, scopes, variables,
    /// evaluate results) by talking to KeraLua directly. Every entry
    /// point on this class must be invoked on the executor thread
    /// while the debuggee is paused — that's the only thread allowed
    /// to touch the Lua state.
    ///
    /// <para>
    /// All KeraLua calls run inside a save-top / set-top guard so we
    /// don't leak stack slots if a path bails early. Variable
    /// handles are short-lived: <see cref="ResetHandlesForResume"/>
    /// drops them when the debuggee resumes, which is also when the
    /// next pause may pick a different stack shape.
    /// </para>
    /// </summary>
    public static class LuaDebugInspector
    {
        // -- Variable-reference table ----------------------------------
        //
        // DAP "variablesReference" handles. We allocate them per pause
        // and drop them on resume. A handle either points to:
        //   - a stack frame's locals bucket (FrameLocals)
        //   - a stack frame's upvalues bucket (FrameUpvalues)
        //   - the globals bucket (singleton per pause)
        //   - a Lua table previously surfaced as a variable, identified
        //     by its registry index
        //
        // Frame ids are also handles, but with a distinct kind.

        enum HandleKind { Frame, FrameLocals, FrameUpvalues, FrameGlobals, Table }

        sealed class Handle
        {
            public HandleKind Kind;
            public int FrameLevel;        // Lua-side stack level (0 = current)
            public int LuaRegistryRef;    // luaL_ref slot for tables (LUA_REFNIL when N/A)
        }

        // Per-debuggee handle table. Keyed on a debuggee instance so
        // different states don't collide. Cleared by
        // ResetHandlesForResume.
        sealed class HandleTable
        {
            public readonly Dictionary<int, Handle> Map = new();
            public int Next = 1;

            public int Add(Handle h)
            {
                int id = Next++;
                Map[id] = h;
                return id;
            }
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LuaDebuggee, HandleTable> sTables = new();

        static HandleTable TableFor(LuaDebuggee d)
        {
            return sTables.GetValue(d, _ => new HandleTable());
        }

        public static void ResetHandlesForResume(LuaDebuggee d)
        {
            if (sTables.TryGetValue(d, out var t))
            {
                // Unref every table-handle slot in the registry so we
                // don't leak roots across pauses.
                foreach (var h in t.Map.Values)
                {
                    if (h.Kind == HandleKind.Table && h.LuaRegistryRef != 0)
                        try { d.PausedCtx?.State.Unref(LuaRegistry.Index, h.LuaRegistryRef); } catch { }
                }
                t.Map.Clear();
                t.Next = 1;
            }
        }

        // -- Stack trace ------------------------------------------------

        public static List<DapStackFrame> BuildStackTrace(LuaDebuggee d)
        {
            var frames = new List<DapStackFrame>();
            var ctx = d.PausedCtx;
            if (ctx == null) return frames;
            var L = ctx.State;
            var ht = TableFor(d);

            int top = L.GetTop();
            try
            {
                LuaDebug ar = default;
                int level = 0;
                while (L.GetStack(level, ref ar) != 0)
                {
                    if (!L.GetInfo("nSl", ref ar))
                    {
                        level++;
                        continue;
                    }

                    string name = ar.Name ?? ar.What ?? "?";
                    string source = ar.Source ?? string.Empty;
                    int line = ar.CurrentLine > 0 ? ar.CurrentLine : 0;

                    var frame = new DapStackFrame
                    {
                        Id = ht.Add(new Handle { Kind = HandleKind.Frame, FrameLevel = level }),
                        Name = name,
                        Line = line,
                        Column = 1,
                        Source = MakeSource(d, source),
                    };
                    frames.Add(frame);
                    level++;
                }
            }
            finally
            {
                L.SetTop(top);
            }
            return frames;
        }

        // -- Scopes -----------------------------------------------------

        public static List<DapScope> BuildScopes(LuaDebuggee d, int frameId)
        {
            var ht = TableFor(d);
            if (!ht.Map.TryGetValue(frameId, out var fh) || fh.Kind != HandleKind.Frame)
                return new List<DapScope>();

            int level = fh.FrameLevel;
            var locals = ht.Add(new Handle { Kind = HandleKind.FrameLocals, FrameLevel = level });
            var upvals = ht.Add(new Handle { Kind = HandleKind.FrameUpvalues, FrameLevel = level });
            var globs = ht.Add(new Handle { Kind = HandleKind.FrameGlobals, FrameLevel = level });

            return new List<DapScope>
            {
                new DapScope { Name = "Locals", VariablesReference = locals, Expensive = false, PresentationHint = "locals" },
                new DapScope { Name = "Upvalues", VariablesReference = upvals, Expensive = false, PresentationHint = "arguments" },
                new DapScope { Name = "Globals", VariablesReference = globs, Expensive = true },
            };
        }

        // -- Variables --------------------------------------------------

        public static List<DapVariable> BuildVariables(LuaDebuggee d, int variablesReference)
        {
            var ctx = d.PausedCtx;
            var ht = TableFor(d);
            if (ctx == null || !ht.Map.TryGetValue(variablesReference, out var h))
                return new List<DapVariable>();

            return h.Kind switch
            {
                HandleKind.FrameLocals => ListLocals(d, h.FrameLevel),
                HandleKind.FrameUpvalues => ListUpvalues(d, h.FrameLevel),
                HandleKind.FrameGlobals => ListGlobals(d),
                HandleKind.Table => ListTable(d, h),
                _ => new List<DapVariable>(),
            };
        }

        static List<DapVariable> ListLocals(LuaDebuggee d, int level)
        {
            var L = d.PausedCtx.State;
            var vars = new List<DapVariable>();
            int top = L.GetTop();
            try
            {
                LuaDebug ar = default;
                if (L.GetStack(level, ref ar) == 0) return vars;

                int n = 1;
                while (true)
                {
                    string name = L.GetLocal(ar, n);
                    if (string.IsNullOrEmpty(name)) break;
                    // GetLocal pushed the value. Skip Lua-internal
                    // synthetic locals like "(temporary)" / "(*temporary)".
                    if (name.StartsWith("("))
                    {
                        L.Pop(1);
                    }
                    else
                    {
                        vars.Add(MakeVariableFromTop(d, name));
                    }
                    n++;
                }
            }
            finally { L.SetTop(top); }
            return vars;
        }

        static List<DapVariable> ListUpvalues(LuaDebuggee d, int level)
        {
            var L = d.PausedCtx.State;
            var vars = new List<DapVariable>();
            int top = L.GetTop();
            try
            {
                LuaDebug ar = default;
                if (L.GetStack(level, ref ar) == 0) return vars;
                // Push the function for this frame (GetInfo with "f").
                if (!L.GetInfo("f", ref ar)) return vars;
                int funcIdx = L.GetTop();
                if (!L.IsFunction(funcIdx)) return vars;

                int n = 1;
                while (true)
                {
                    string name = L.GetUpValue(funcIdx, n);
                    if (string.IsNullOrEmpty(name)) break;
                    vars.Add(MakeVariableFromTop(d, name));
                    n++;
                }
            }
            finally { L.SetTop(top); }
            return vars;
        }

        static List<DapVariable> ListGlobals(LuaDebuggee d)
        {
            var L = d.PausedCtx.State;
            var vars = new List<DapVariable>();
            int top = L.GetTop();
            try
            {
                L.PushGlobalTable();
                int tIdx = L.GetTop();
                IterateTableTop(d, L, tIdx, vars);
            }
            finally { L.SetTop(top); }
            // Globals view is noisy on a typical pack — sort
            // alphabetically so the pack's user globals are easier to
            // skim past the standard library.
            vars.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return vars;
        }

        static List<DapVariable> ListTable(LuaDebuggee d, Handle h)
        {
            var L = d.PausedCtx.State;
            var vars = new List<DapVariable>();
            int top = L.GetTop();
            try
            {
                L.RawGetInteger(LuaRegistry.Index, h.LuaRegistryRef);
                if (!L.IsTable(-1)) return vars;
                int tIdx = L.GetTop();
                IterateTableTop(d, L, tIdx, vars);
            }
            finally { L.SetTop(top); }
            return vars;
        }

        // Iterate the table at stack index tIdx. Caller is responsible
        // for SetTop balance.
        static void IterateTableTop(LuaDebuggee d, KeraLua.Lua L, int tIdx, List<DapVariable> outVars)
        {
            L.PushNil();
            while (L.Next(tIdx))
            {
                // Stack: ..., key (-2), value (-1)
                string keyDisplay = ToDisplayString(L, -2);
                int valueIdx = L.GetTop();
                outVars.Add(MakeVariableFromIndex(d, keyDisplay, valueIdx));
                L.Pop(1); // pop value, keep key for next iteration
            }
        }

        // Build a DapVariable from the value currently on top of the
        // stack and pop it.
        static DapVariable MakeVariableFromTop(LuaDebuggee d, string name)
        {
            var L = d.PausedCtx.State;
            int idx = L.GetTop();
            var v = MakeVariableFromIndex(d, name, idx);
            L.Pop(1);
            return v;
        }

        static DapVariable MakeVariableFromIndex(LuaDebuggee d, string name, int idx)
        {
            var L = d.PausedCtx.State;
            var type = L.Type(idx);
            string typeName = type.ToString();
            int varRef = 0;

            string display;
            switch (type)
            {
                case LuaType.Nil:
                    display = "nil";
                    break;
                case LuaType.Boolean:
                    display = L.ToBoolean(idx) ? "true" : "false";
                    break;
                case LuaType.Number:
                    display = L.IsInteger(idx)
                        ? L.ToInteger(idx).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : L.ToNumber(idx).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case LuaType.String:
                    display = "\"" + (L.ToString(idx, callMetamethod: false) ?? "") + "\"";
                    break;
                case LuaType.Table:
                    {
                        // Stash a registry ref so we can iterate later.
                        L.PushCopy(idx);
                        int reg = L.Ref(LuaRegistry.Index);
                        var ht = TableFor(d);
                        varRef = ht.Add(new Handle { Kind = HandleKind.Table, LuaRegistryRef = reg });
                        display = "table";
                        break;
                    }
                case LuaType.Function:
                    display = "function";
                    break;
                case LuaType.UserData:
                case LuaType.LightUserData:
                    display = ToDisplayString(L, idx) ?? "userdata";
                    break;
                default:
                    display = ToDisplayString(L, idx) ?? type.ToString();
                    break;
            }

            return new DapVariable
            {
                Name = name,
                Value = display,
                Type = typeName,
                VariablesReference = varRef,
            };
        }

        static string ToDisplayString(KeraLua.Lua L, int idx)
        {
            try
            {
                // Use raw conversion (no metamethod) to avoid
                // accidentally invoking pack-author __tostring while
                // paused — that could call back into Lua and the
                // hook would re-enter.
                var s = L.ToString(idx, callMetamethod: false);
                if (s != null) return s;
                var t = L.Type(idx);
                return $"<{t}>";
            }
            catch { return "<?>"; }
        }

        // -- Source mapping --------------------------------------------

        static DapSource MakeSource(LuaDebuggee d, string luaSource)
        {
            string name = luaSource ?? "(unknown)";
            string path = null;

            if (!string.IsNullOrEmpty(luaSource) && (luaSource[0] == '@' || luaSource[0] == '='))
                name = luaSource.Substring(1);

            // If the debuggee has a pack root mapped, build an
            // absolute path so VS Code can open the file directly.
            // Otherwise leave Path null and rely on Name only — VS
            // Code will show the chunk name without source-on-click.
            if (!string.IsNullOrEmpty(d.PackRootPath) && !string.IsNullOrEmpty(name))
            {
                try
                {
                    var combined = System.IO.Path.Combine(d.PackRootPath, name.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(combined))
                        path = System.IO.Path.GetFullPath(combined);
                }
                catch { }
            }

            return new DapSource { Name = name, Path = path };
        }

        // -- Evaluate ---------------------------------------------------

        /// <summary>
        /// Compile and run an expression against the paused state.
        /// We try "return &lt;expr&gt;" first so simple expressions
        /// produce a value; if that fails to parse, fall back to
        /// running the chunk as a statement (no result).
        /// </summary>
        public static DapVariable Evaluate(LuaDebuggee d, string expr, int frameId)
        {
            var L = d.PausedCtx.State;
            int top = L.GetTop();
            try
            {
                if (string.IsNullOrWhiteSpace(expr))
                    return new DapVariable { Name = "result", Value = "nil", Type = "Nil" };

                // Try as expression.
                LuaStatus status = L.LoadString("return " + expr, "=(eval)");
                if (status != LuaStatus.OK)
                {
                    // Pop error message, retry as statement.
                    L.Pop(1);
                    status = L.LoadString(expr, "=(eval)");
                    if (status != LuaStatus.OK)
                    {
                        string err = L.ToString(-1, callMetamethod: false) ?? "compile error";
                        return new DapVariable { Name = "error", Value = err, Type = "Error" };
                    }
                }

                // Call with 0 args, multret. Errors leave a string on
                // top of the stack which we surface.
                int beforeCall = L.GetTop() - 1; // index of the function we just loaded - 1
                // KeraLua doesn't expose a MultiReturn constant; -1 is
                // Lua's LUA_MULTRET, which keeps every result on the
                // stack so multi-return expressions surface correctly.
                var callStatus = L.PCall(0, -1, 0);
                if (callStatus != LuaStatus.OK)
                {
                    string err = L.ToString(-1, callMetamethod: false) ?? "runtime error";
                    return new DapVariable { Name = "error", Value = err, Type = "Error" };
                }

                int results = L.GetTop() - beforeCall;
                if (results <= 0)
                    return new DapVariable { Name = "result", Value = "nil", Type = "Nil" };

                // Surface only the first result. Multi-return could be
                // shown as an indexed grouping later.
                int firstIdx = beforeCall + 1;
                return MakeVariableFromIndex(d, "result", firstIdx);
            }
            finally { L.SetTop(top); }
        }
    }
}
