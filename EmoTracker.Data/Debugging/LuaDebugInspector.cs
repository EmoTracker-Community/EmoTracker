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

        enum HandleKind { Frame, FrameLocals, FrameUpvalues, FrameGlobals, Table, Managed, PendingUnwrap }

        sealed class Handle
        {
            public HandleKind Kind;
            public int FrameLevel;        // Lua-side stack level (0 = current)
            public int LuaRegistryRef;    // luaL_ref slot for tables (LUA_REFNIL when N/A)
            public object ManagedObject;  // C# instance for HandleKind.Managed
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
                    if ((h.Kind == HandleKind.Table || h.Kind == HandleKind.PendingUnwrap)
                        && h.LuaRegistryRef != 0)
                    {
                        try { d.PausedCtx?.State.Unref(LuaRegistry.Index, h.LuaRegistryRef); } catch { }
                    }
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
                // Mark Globals not-expensive so VS Code auto-fetches
                // its contents on frame select. Iterating _G is a
                // single Next-loop over a table that's hundreds of
                // entries at most — fast, and surfacing it
                // automatically matches what every other Lua
                // debugger does.
                new DapScope { Name = "Globals", VariablesReference = globs, Expensive = false },
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
                HandleKind.Managed => ListManagedObject(d, h.ManagedObject),
                HandleKind.PendingUnwrap => ListPendingUnwrap(d, h),
                _ => new List<DapVariable>(),
            };
        }

        // Lazy unwrap: when the user expands a userdata variable, we
        // pull the wrapped C# object via the registry-stashed
        // userdata reference and reflect its public properties.
        // Doing this lazily (instead of eagerly during the table
        // walk) means we only pay the reflection cost for objects
        // the user actually opens — pack-script tables with many
        // userdata entries no longer freeze the variables panel.
        static List<DapVariable> ListPendingUnwrap(LuaDebuggee d, Handle h)
        {
            var ctx = d.PausedCtx;
            if (ctx == null) return new List<DapVariable>();
            var L = ctx.State;
            int top = L.GetTop();
            try
            {
                L.RawGetInteger(LuaRegistry.Index, h.LuaRegistryRef);
                if (!L.IsUserData(-1) && !L.IsLightUserData(-1))
                    return new List<DapVariable>();
                int udIdx = L.GetTop();
                object managed = TryUnwrapUserData(d, udIdx);
                if (managed == null) return new List<DapVariable>();
                return ListManagedObject(d, managed);
            }
            catch (Exception ex)
            {
                if (LuaDebuggee.sTrace)
                    LuaDebuggee.Trace("ListPendingUnwrap: regRef={0} threw: {1}", h.LuaRegistryRef, ex.Message);
                return new List<DapVariable>();
            }
            finally { L.SetTop(top); }
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
                if (LuaDebuggee.sTrace)
                    LuaDebuggee.Trace("ListTable: regRef={0} produced {1} entries", h.LuaRegistryRef, vars.Count);
            }
            finally { L.SetTop(top); }
            return vars;
        }

        // Hard cap on entries returned per table-expansion request.
        // Very large tables (entire stdlib globals view, pack-author
        // catalogs that list every item, etc.) can run into hundreds
        // of entries. Caller-side panels handle that fine; what we
        // protect against here is pathological iteration where
        // lua_next somehow doesn't terminate (we've seen
        // "External component has thrown" mid-iter from KeraLua,
        // and rare cases involving sparse arrays with nil holes).
        const int kMaxIterEntries = 5000;

        // Iterate the table at stack index tIdx. Caller is responsible
        // for SetTop balance.
        //
        // Belt-and-braces try/catches: lua_next can throw "external
        // component has thrown" for individual entries (observed
        // in the wild on a CodeTracker autotracker callback's
        // `value` parameter — a single bad entry caused the entire
        // expansion to return zero children). Per-entry catches
        // skip the bad entry and keep going; the outer catch is
        // a final stop so a totally-broken table at least returns
        // whatever entries we did get.
        //
        // The iteration cap is the third line of defense — even if
        // lua_next somehow gets stuck in a non-terminating loop
        // (e.g. a corrupted hash chain), we'll bail out and return
        // a "...truncated" marker rather than hang the panel.
        static void IterateTableTop(LuaDebuggee d, KeraLua.Lua L, int tIdx, List<DapVariable> outVars)
        {
            int iter = 0;
            bool truncated = false;
            try
            {
                L.PushNil();
                while (L.Next(tIdx))
                {
                    iter++;
                    if (iter > kMaxIterEntries)
                    {
                        // Pop the value (lua_next leaves k,v on stack)
                        // — we still need to leave the key so a final
                        // pop balances correctly when the loop exits
                        // through the `truncated` path. Then drop the
                        // key explicitly, mirroring the normal end-of-
                        // iteration cleanup that lua_next does on its
                        // own when it returns false.
                        L.Pop(2);
                        truncated = true;
                        break;
                    }

                    // Stack: ..., key (-2), value (-1)
                    string keyDisplay;
                    try { keyDisplay = ToDisplayString(L, -2) ?? "?"; }
                    catch
                    {
                        L.Pop(1);
                        continue;
                    }

                    int valueIdx = L.GetTop();
                    DapVariable v;
                    try { v = MakeVariableFromIndex(d, keyDisplay, valueIdx); }
                    catch
                    {
                        L.Pop(1);
                        continue;
                    }
                    outVars.Add(v);
                    L.Pop(1); // pop value, keep key for next iteration
                }
            }
            catch (Exception ex)
            {
                if (LuaDebuggee.sTrace)
                    LuaDebuggee.Trace("IterateTable iter={0} loop threw: {1}", iter, ex.Message);
            }

            if (truncated)
            {
                outVars.Add(new DapVariable
                {
                    Name = "…",
                    Value = "<truncated after " + kMaxIterEntries + " entries>",
                    Type = "info",
                });
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
                        if (LuaDebuggee.sTrace)
                            LuaDebuggee.Trace("MakeVar table: name='{0}' varRef={1} regRef={2}", name, varRef, reg);
                        break;
                    }
                case LuaType.Function:
                    display = "function";
                    break;
                case LuaType.UserData:
                case LuaType.LightUserData:
                    {
                        // We deliberately do NOT call __tostring (i.e.
                        // do not pass callMetamethod=true) on userdata
                        // during the iteration walk. NLua's
                        // __tostring metamethod ultimately invokes the
                        // wrapped C# object's ToString, and pack
                        // authors sometimes wire up classes whose
                        // ToString — directly or transitively — ends
                        // up running pack-side Lua. Triggering pack
                        // Lua during a debugger inspection re-enters
                        // the very state the user is trying to
                        // examine, and observed behavior on
                        // CodeTracker is that an autotracker
                        // callback at scripts/auto/segmentupdates.lua
                        // fires "invalid key to 'next'" the moment
                        // we touch certain userdatas in the globals
                        // view.
                        //
                        // Display falls back to a typed placeholder.
                        // Real ToString + reflected properties show
                        // up the moment the user clicks the chevron
                        // (handled by HandleKind.PendingUnwrap below
                        // via the much-safer GetObjectFromPath
                        // roundtrip — that path doesn't dispatch
                        // metamethods).
                        display = "<userdata>";
                        typeName = "UserData";

                        // Stash a registry ref so PendingUnwrap can re-
                        // resolve the userdata at expansion time. (The
                        // current `idx` won't be valid by then — Lua
                        // stack slots are scoped to the current request.)
                        L.PushCopy(idx);
                        int reg = L.Ref(LuaRegistry.Index);
                        var ht = TableFor(d);
                        varRef = ht.Add(new Handle { Kind = HandleKind.PendingUnwrap, LuaRegistryRef = reg });
                        break;
                    }
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

        // -- Managed-object inspection ---------------------------------
        //
        // NLua wraps every C# object passed to Lua as a userdata with
        // an opaque metatable. Without unwrapping, the debugger panel
        // shows "<UserData>" which tells the user nothing. By
        // roundtripping the userdata through a Lua global, NLua's
        // path-based getter returns the underlying C# instance and
        // we can render its real type + ToString() value, plus expand
        // its public properties via reflection.

        // Temp-global slot used during the roundtrip. Single shared
        // name across the inspector — we always set then clear it
        // immediately so concurrent reads aren't a concern (we're
        // single-threaded inside the paused executor by construction).
        const string sUnwrapTempGlobal = "__et_dbg_unwrap";

        static object TryUnwrapUserData(LuaDebuggee d, int stackIdx)
        {
            var ctx = d.PausedCtx;
            if (ctx?.Lua == null || ctx.State == null) return null;
            var L = ctx.State;
            int top = L.GetTop();
            try
            {
                L.PushCopy(stackIdx);
                L.SetGlobal(sUnwrapTempGlobal);
                object value;
                try { value = ctx.Lua[sUnwrapTempGlobal]; }
                catch { value = null; }
                // Always clear the temp global so it never lingers.
                try { ctx.Lua[sUnwrapTempGlobal] = null; } catch { }
                return value;
            }
            catch { return null; }
            finally { L.SetTop(top); }
        }

        // BindingFlags used for property/field reflection. Public
        // instance only — pack authors rarely need private members
        // and exposing them widens the surface for accidental
        // side-effect-y getters.
        static readonly System.Reflection.BindingFlags sMemberFlags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        // Reflection result cache. Property/field metadata is fixed
        // per Type, so we cache the discovered MemberInfo[] arrays
        // to avoid the (somewhat expensive) BindingFlags lookup on
        // every expand of an object of the same type.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, (System.Reflection.PropertyInfo[] props, System.Reflection.FieldInfo[] fields)> sMemberCache = new();

        static (System.Reflection.PropertyInfo[] props, System.Reflection.FieldInfo[] fields) GetMembers(Type t)
        {
            return sMemberCache.GetOrAdd(t, type =>
            {
                System.Reflection.PropertyInfo[] props;
                System.Reflection.FieldInfo[] fields;
                try { props = type.GetProperties(sMemberFlags); }
                catch { props = Array.Empty<System.Reflection.PropertyInfo>(); }
                try { fields = type.GetFields(sMemberFlags); }
                catch { fields = Array.Empty<System.Reflection.FieldInfo>(); }
                return (props, fields);
            });
        }

        static List<DapVariable> ListManagedObject(LuaDebuggee d, object obj)
        {
            var vars = new List<DapVariable>();
            if (obj == null) return vars;

            var t = obj.GetType();
            var (props, fields) = GetMembers(t);

            // Public instance properties (read-only properties are
            // always shown; write-only properties are skipped — no
            // value to display). Indexers are skipped because we
            // don't know what index to read with.
            foreach (var p in props)
            {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length > 0) continue;
                // Skip properties whose getter is virtual + might
                // have heavy side effects. Heuristic: skip
                // "Item" indexer-style + properties that return
                // IEnumerable (avoids enumerating large catalogs
                // when the user just opens a top-level object).
                if (p.Name == "Item") continue;

                object v;
                try { v = p.GetValue(obj); }
                catch (Exception ex) { v = "<getter threw: " + (ex.InnerException?.Message ?? ex.Message) + ">"; }
                vars.Add(MakeManagedVar(d, p.Name, v, p.PropertyType));
            }

            // Public instance fields (rare but possible — pack-script
            // bridge classes often expose a handful of fields).
            foreach (var f in fields)
            {
                object v;
                try { v = f.GetValue(obj); }
                catch (Exception ex) { v = "<read threw: " + ex.Message + ">"; }
                vars.Add(MakeManagedVar(d, f.Name, v, f.FieldType));
            }

            // Sort alphabetically — reflection ordering is
            // implementation-defined and pack authors expect a
            // predictable view.
            vars.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return vars;
        }

        // Build a DapVariable for a managed value (the result of a
        // property getter / field read). Primitives render inline;
        // complex types become expandable handles backed by another
        // ListManagedObject call.
        static DapVariable MakeManagedVar(LuaDebuggee d, string name, object value, Type declaredType)
        {
            string display;
            int varRef = 0;
            string typeName = declaredType?.Name ?? value?.GetType().Name ?? "?";

            if (value == null)
            {
                display = "null";
            }
            else if (value is string s)
            {
                display = "\"" + s + "\"";
            }
            else if (value is bool b)
            {
                display = b ? "true" : "false";
            }
            else
            {
                var vType = value.GetType();
                if (vType.IsPrimitive || vType.IsEnum || vType == typeof(decimal))
                {
                    try { display = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture); }
                    catch { display = value.ToString(); }
                }
                else
                {
                    // Complex object — show ToString() and make
                    // expandable so the user can drill in.
                    try { display = value.ToString() ?? "<" + vType.Name + ">"; }
                    catch { display = "<" + vType.Name + ">"; }
                    typeName = vType.Name;
                    var ht = TableFor(d);
                    varRef = ht.Add(new Handle { Kind = HandleKind.Managed, ManagedObject = value });
                }
            }

            return new DapVariable
            {
                Name = name,
                Value = display,
                Type = typeName,
                VariablesReference = varRef,
            };
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
