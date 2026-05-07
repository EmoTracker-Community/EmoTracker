using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using EmoTracker.Data.Scripting;
using Newtonsoft.Json;
using NLua;
using NLua.Exceptions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

// Phase 6 step 11: ScriptManager.cs hosts the singleton + the logging /
// callback infrastructure that legitimately runs on the singleton. The
// Lua-bridge globals (TrackerScriptInterface, LayoutScriptInterface,
// LoggingBlock) still reach singletons because Lua bindings are
// pre-allocated before per-state context exists; per-state Lua lands
// when each state allocates its own bindings (deferred follow-up).
#pragma warning disable CS0618

namespace EmoTracker.Data
{
    class ImageReferenceProvider
    {
        // Phase 7.1.h: bound to a state at construction so the
        // pack-relative factory uses THIS state's PackageInstance's
        // GamePackage instead of consulting the active session — which
        // would be null during a definitional-state load (init.lua
        // runs before any primary state is forked).
        readonly Sessions.TrackerState mState;

        public ImageReferenceProvider(Sessions.TrackerState state)
        {
            mState = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ImageReference FromPackRelativePath(string path, string filter = null)
        {
            return ImageReference.FromPackRelativePath(mState.PackageInstance, path, filter);
        }

        public ImageReference FromImageReference(ImageReference existingReference, string filter = null)
        {
            return ImageReference.FromImageReference(existingReference, filter);
        }

        public ImageReference FromLayeredImageReferences(params ImageReference[] layers)
        {
            return ImageReference.FromLayeredImageReferences(layers);
        }
    }

    class TrackerScriptInterface
    {
        readonly Sessions.TrackerState mState;

        public TrackerScriptInterface(Sessions.TrackerState state)
        {
            mState = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void AddItems(string path)
        {
            var pkg = mState.PackageInstance?.GamePackage;
            if (pkg == null) return;
            mState.Items.IncrementalLoad(path, pkg, bLegacy: false, state: mState);
        }

        public void AddMaps(string path)
        {
            var pkg = mState.PackageInstance?.GamePackage;
            if (pkg == null) return;
            mState.Maps.IncrementalLoad(path, pkg, state: mState);
        }

        public void AddLocations(string path)
        {
            var pkg = mState.PackageInstance?.GamePackage;
            if (pkg == null) return;
            mState.Locations.IncrementalLoad(path, pkg, bLegacy: false, state: mState);
        }

        public void AddLayouts(string path)
        {
            var pkg = mState.PackageInstance?.GamePackage;
            if (pkg == null) return;
            mState.Layouts.IncrementalLoad(path, pkg);
        }

        ICodeProvider GetProvider(ref string code)
        {
            if (code.StartsWith("@"))
            {
                code = code.Substring(1);
                return mState.Locations;
            }
            if (code.StartsWith("$"))
            {
                code = code.Substring(1);
                return mState.Scripts;
            }
            return mState.Items;
        }

        public object FindObjectForCode(string code)
        {
            var provider = GetProvider(ref code);
            return provider?.FindObjectForCode(code);
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            var provider = GetProvider(ref code);
            if (provider == null)
            {
                maxAccessibility = AccessibilityLevel.None;
                return 0;
            }
            return provider.ProviderCountForCode(code, out maxAccessibility);
        }

        public string ActiveVariantUID
        {
            get { return mState.ActiveVariantUID; }
        }

        public Location RootLocation
        {
            get { return mState.Locations.Root; }
        }

        public bool DisplayAllLocations
        {
            get { return mState.Settings.DisplayAllLocations; }
            set { mState.Settings.DisplayAllLocations = value; }
        }

        public bool AlwaysAllowClearing
        {
            get { return mState.Settings.AlwaysAllowClearing; }
            set { mState.Settings.AlwaysAllowClearing = value; }
        }

        public bool PinLocationsOnItemCapture
        {
            get { return mState.Settings.PinLocationsOnItemCapture; }
            set { mState.Settings.PinLocationsOnItemCapture = value; }
        }

        public bool AutoUnpinLocationsOnClear
        {
            get { return mState.Settings.AutoUnpinLocationsOnClear; }
            set { mState.Settings.AutoUnpinLocationsOnClear = value; }
        }

    }

    class LayoutScriptInterface
    {
        readonly Sessions.TrackerState mState;

        public LayoutScriptInterface(Sessions.TrackerState state)
        {
            mState = state ?? throw new ArgumentNullException(nameof(state));
        }

        public Layout.Layout FindLayout(string key)
        {
            return mState.Layouts?.FindLayout(key);
        }

        public Layout.LayoutItem FindElement(string uid)
        {
            return mState.Layouts?.FindElement(uid);
        }

        public string GetColorForAccessibility(AccessibilityLevel accessibility)
        {
            switch (accessibility)
            {
                case AccessibilityLevel.None:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_None;

                case AccessibilityLevel.Cleared:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Cleared;

                case AccessibilityLevel.Inspect:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Inspect;
                
                case AccessibilityLevel.Partial:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Partial;

                case AccessibilityLevel.SequenceBreak:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_SequenceBreak;

                case AccessibilityLevel.Normal:
                    return Settings.ApplicationColors.Instance.AccessibilityColor_Normal;                
            }

            return null;
        }
    }

    public class LoggingBlock : IDisposable
    {
        readonly ScriptManager mScripts;
        public LoggingBlock(ScriptManager scripts)
        {
            mScripts = scripts;
            if (mScripts != null) mScripts.LogIndent++;
        }

        public void Dispose()
        {
            if (mScripts != null) mScripts.LogIndent--;
        }
    }

    /// <summary>
    /// Phase 5: <see cref="ScriptManager"/> is no longer an
    /// <see cref="ObservableSingleton{T}"/>; it is a regular instantiable
    /// class so Phase 6 can hold one per <c>TrackerState</c>. The static
    /// <see cref="Current"/> property tracks "the active primary"
    /// ScriptManager, defaulting to a single lazily-created instance for
    /// pre-Phase-6 callers; Phase 6's state-switch code reassigns it.
    ///
    /// <para>
    /// <see cref="Instance"/> remains as a transitional alias for
    /// <see cref="Current"/> so the existing ~97 <c>Sessions.SessionContext.ActiveState?.Scripts</c>
    /// callsites continue to work unchanged. New holder-aware code should
    /// prefer <see cref="ModelTypeBase.GetScriptManager"/> where a model
    /// reference is available, so per-state routing falls into place
    /// automatically once Phase 6 lands.
    /// </para>
    /// </summary>
    /// <summary>
    /// Phase 7.1: <see cref="ScriptManager"/> is per-state. Each
    /// <c>TrackerState</c> owns one. Reach via the holder's
    /// <see cref="ModelTypeBase.GetScriptManager"/>, or via
    /// <c>ApplicationModel.Instance.PrimaryState.Scripts</c> /
    /// <c>Sessions.SessionContext.ActiveState.Scripts</c>.
    /// </summary>
    public class ScriptManager : ModelTypeBase, ICodeProvider, IScriptManager
    {
        public class LogLine
        {
            public string Text { get; set; }
            public string Color { get; set; }
        }

        ObservableCollection<LogLine> mLogOutput = new ObservableCollection<LogLine>();
        DelegateCommand mClearLogCommand;

        static readonly string SystemLua =
@"

import = function () end

function print(...)
    local printResult = """"
    for i, v in ipairs(table.pack(...)) do
        printResult = printResult..tostring(v).. ""\t""
    end
    if string.len(printResult) > 0 then
        _output(printResult)
    end
 end

-- Safe-call error handler. Builds the traceback that callers
-- propagate as the error value. When a DAP debug session is
-- attached and exception break is armed, the C#-side
-- __et_dap_on_error upcall pauses the executor thread inside this
-- handler so VS Code can inspect locals at the throw frame BEFORE
-- the error unwinds. The pcall around the upcall ensures a
-- buggy debugger plumbing path can never break the underlying
-- error path.
-- DAP exception-break upcall. Pure-Lua wrapper that routes through
-- ScriptHost (the per-state ScriptManager userdata) to reach the
-- OnLuaErrorForDap C# method. NLua's userdata method dispatch
-- handles argument marshalling correctly — direct delegate /
-- RegisterFunction bindings of OnLuaErrorForDap failed at call
-- time with 'invalid arguments', for reasons specific to NLua's
-- non-userdata dispatch paths. The pcall in __et_safe_call_handler
-- ensures debugger plumbing failures never break the underlying
-- error path (the traceback string is still propagated).
function __et_dap_on_error(err, tb)
    if ScriptHost and ScriptHost.OnLuaErrorForDap then
        ScriptHost:OnLuaErrorForDap(tostring(err), tostring(tb))
    end
end

function __et_safe_call_handler(err)
    local tb = debug.traceback(err, 2)
    pcall(__et_dap_on_error, err, tb)
    return tb
end

-- Resolves a Lua function's source identity for the dev-mode call
-- timing tally. Returns (source, linedefined, name); used by the
-- C#-side ScriptManager.SafeCall hot path to key per-callback
-- timing stats. Off the timing critical path: called once per
-- unique LuaFunction wrapper (cached afterwards).
function __et_func_key(f)
    local info = debug.getinfo(f, 'Sn')
    return info and (info.source or '?') or '?',
           info and (info.linedefined or 0) or 0,
           info and (info.name or '') or ''
end

-- Safe-call wrapper: invokes a function via xpcall so that the
-- error handler captures the Lua call stack at the point of
-- failure. Returns:
--   true, result1, result2, ...   on success
--   false, errorMessageWithTraceback   on failure
function _safe_call(fn, ...)
    local args = table.pack(...)
    return xpcall(function() return fn(table.unpack(args, 1, args.n)) end, __et_safe_call_handler)
end

-- Note: __et_proxy_cache is intentionally NOT initialized here —
-- having it here would land it in the cloner's destination-baseline
-- name set and the fork would start with an empty cache.  Instead
-- __et_wrap_model_ref lazy-creates it, so the source's populated
-- cache (with cloned proxies and cloned LuaModelRefs) deep-copies
-- across fork like any other user global; the fork ends up with a
-- pre-warmed cache that resolves into the fork's graph.

-- Metatable installed on every model proxy. Resolution happens on
-- every member access through the underlying LuaModelRef so forks
-- (which carry their own LuaModelRef pointing at the fork's resolver)
-- see the fork's instances.  See ScriptManager.WrapAsLuaProxy for the
-- C#-side construction.
__et_model_proxy_meta = {
    __index = function(t, k)
        local target = t.__model_ref:Resolve()
        if target == nil then return nil end
        local v = target[k]
        -- Method binding fix-up: `proxy:Method(...)` desugars to
        -- `proxy.Method(proxy, ...)`. NLua-bound C# methods expect the
        -- C# instance as `self`, not the proxy table, so wrap functions
        -- in a thunk that re-resolves and substitutes the live target.
        -- Re-resolution is cheap (same resolver, same id) and ensures
        -- the call lands on the most-recently-active instance even if
        -- the resolver's graph was swapped between `__index` and the
        -- subsequent call.
        if type(v) == 'function' then
            return function(_, ...)
                local live = t.__model_ref:Resolve()
                if live == nil then return nil end
                return live[k](live, ...)
            end
        end
        return v
    end,
    __newindex = function(t, k, v)
        local target = t.__model_ref:Resolve()
        if target ~= nil then
            target[k] = v
        end
    end,
    __eq = function(a, b)
        if a == nil or b == nil then return false end
        local aRef = rawget(a, '__model_ref')
        local bRef = rawget(b, '__model_ref')
        if aRef == nil or bRef == nil then return false end
        return aRef.DefinitionIdString == bRef.DefinitionIdString
    end,
    __tostring = function(t)
        local target = t.__model_ref:Resolve()
        if target ~= nil then return tostring(target) end
        return 'ModelProxy(<unresolved>)'
    end,
}

-- C#-callable helper: looks up an existing proxy by its DefinitionId
-- string in __et_proxy_cache, or creates a new metatabled proxy table
-- pointing at <modelRef>.  The cache lookup is what makes
--   local a = Tracker:FindObjectForCode('foo')
--   local b = Tracker:FindObjectForCode('foo')
--   assert(a == b)            -- raw == works (same table)
--   __et_proxy_cache[id] = a  -- a is usable as a table key
-- behave as a pack-author would naively expect.
function __et_wrap_model_ref(modelRef, defIdString)
    if modelRef == nil or defIdString == nil or defIdString == '' then
        return nil
    end
    if __et_proxy_cache == nil then __et_proxy_cache = {} end
    local existing = __et_proxy_cache[defIdString]
    if existing ~= nil then return existing end
    local proxy = setmetatable({__model_ref = modelRef}, __et_model_proxy_meta)
    __et_proxy_cache[defIdString] = proxy
    return proxy
end
 ";

        IGamePackage mPackage;
        Lua mLua;

        // -- Lua-call timing instrumentation (-dev only) --
        //
        // When IsDevMode is true, every C#→Lua invocation routed
        // through SafeCall is timed and tallied per-function. The
        // dev-terminal /slowcalls command surfaces and /resetslowcalls
        // clears this data. Off-path overhead in non-dev builds is
        // a single static-readonly bool check (constant-folded by
        // the JIT on Release); the dev-mode branch adds a Stopwatch
        // timestamp pair (~50 ns) plus a ConditionalWeakTable lookup.
        public sealed class CallStats
        {
            public string Source;
            public int Line;
            public string Name;
            public long TotalTicks;
            public long MaxTicks;
            public int Count;
        }

        static readonly bool sCallTimingEnabled = UserDirectory.IsDevMode;

        readonly System.Collections.Concurrent.ConcurrentDictionary<string, CallStats> mCallStats =
            new System.Collections.Concurrent.ConcurrentDictionary<string, CallStats>();

        // Per-LuaFunction → CallStats cache. ConditionalWeakTable so
        // we don't pin LuaFunction wrappers (NLua may dispose +
        // recreate them). The first SafeCall on a given wrapper
        // pays the debug.getinfo cost; subsequent calls go straight
        // to Interlocked updates on the cached struct.
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<LuaFunction, CallStats> mFuncStatsCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<LuaFunction, CallStats>();

        /// <summary>
        /// Snapshot of all collected SafeCall timings, suitable for
        /// pretty-printing. Returns an empty enumeration when
        /// <c>-dev</c> mode wasn't enabled (so the timing path is
        /// off and nothing has been recorded).
        /// </summary>
        public List<CallStats> GetCallStatsSnapshot()
        {
            return new List<CallStats>(mCallStats.Values);
        }

        /// <summary>Clears the SafeCall timing tally.</summary>
        public void ResetCallStats()
        {
            mCallStats.Clear();
        }

        // Hot-path-friendly stats record. Cheap when the wrapper is
        // already cached (one CWT read); pays the debug.getinfo
        // cost once per unique LuaFunction.
        [NLua.LuaHide]
        CallStats GetOrCreateStatsFor(LuaFunction func)
        {
            if (mFuncStatsCache.TryGetValue(func, out var cached))
                return cached;

            string source = "?";
            int line = 0;
            string name = "";
            try
            {
                using (var keyFn = mLua["__et_func_key"] as LuaFunction)
                {
                    if (keyFn != null)
                    {
                        var r = keyFn.Call(func);
                        if (r != null && r.Length >= 1) source = r[0] as string ?? "?";
                        if (r != null && r.Length >= 2) line = Convert.ToInt32(r[1] ?? 0);
                        if (r != null && r.Length >= 3) name = r[2] as string ?? "";
                    }
                }
            }
            catch { /* defensive — never let timing break a real call */ }

            // Strip Lua's "@" file prefix for readability.
            if (!string.IsNullOrEmpty(source) && (source[0] == '@' || source[0] == '='))
                source = source.Substring(1);

            // Key: source:line plus the function name (when known).
            // Two functions on the same line in the same source share
            // a key — fine in practice, the slot remains
            // unambiguous for typical pack code where a closure on a
            // line is the only one that could cost timing weight.
            string key = string.IsNullOrEmpty(name)
                ? string.Format("{0}:{1}", source, line)
                : string.Format("{0}:{1} ({2})", source, line, name);

            var stats = mCallStats.GetOrAdd(key, _ => new CallStats { Source = source, Line = line, Name = name });
            // Add ignores duplicates if another thread raced us; OK,
            // the GetOrAdd above resolved the canonical instance.
            try { mFuncStatsCache.Add(func, stats); } catch (ArgumentException) { /* already added by another thread */ }
            return stats;
        }

        [NLua.LuaHide]
        void RecordCall(LuaFunction func, long elapsedTicks)
        {
            var stats = GetOrCreateStatsFor(func);
            System.Threading.Interlocked.Add(ref stats.TotalTicks, elapsedTicks);
            System.Threading.Interlocked.Increment(ref stats.Count);
            // CAS-loop max update.
            long curMax;
            do
            {
                curMax = stats.MaxTicks;
                if (elapsedTicks <= curMax) return;
            }
            while (System.Threading.Interlocked.CompareExchange(ref stats.MaxTicks, elapsedTicks, curMax) != curMax);
        }

        // -- Lua debugger (DAP) integration --
        //
        // Allocated on bootstrap iff Debugging.LuaDebugServer.Instance
        // is set (dev-mode + the LuaDebugServer extension started).
        // Disposed/unregistered on Reset so a forked-and-discarded
        // state doesn't leak a registry entry. Public so the app
        // extension can map a state's PackPath onto it post-load
        // (we don't know the pack root at bootstrap time — pack
        // assignment happens later in Load()).
        public Debugging.LuaDebuggee Debuggee { get; private set; }

        [NLua.LuaHide]
        public IEnumerable<LogLine> LogOutput
        {
            get { return mLogOutput; }
        }

        /// <summary>
        /// Seed this manager's <see cref="LogOutput"/> with a snapshot
        /// of <paramref name="source"/>'s lines. Used by
        /// <see cref="Sessions.TrackerState.Fork"/> so the fork's
        /// developer-terminal opens with the source's pack-load /
        /// init.lua / prior-command transcript visible — instead of
        /// the empty buffer a freshly-constructed ScriptManager
        /// otherwise gives. Replaces this manager's existing entries.
        /// </summary>
        [NLua.LuaHide]
        public void SeedLogOutputFromFork(ScriptManager source)
        {
            if (source == null) return;
            mLogOutput.Clear();
            foreach (var line in source.mLogOutput)
                mLogOutput.Add(line);
        }

        public DelegateCommand ClearLogCommand
        {
            get { return mClearLogCommand; }
        }

        static readonly string kFirstIndentString = "   ";
        static readonly string kIndentString = "   ";
        static readonly string kLastIndentString = "   ";

        string mIndentText = "";
        int mLogIndent = 0;
        public int LogIndent
        {
            get { return mLogIndent; }
            set
            {
                mLogIndent = Math.Max(value, 0);

                mIndentText = "";
                for (int i = 0; i < mLogIndent; ++i)
                {
                    if (i == 0)
                        mIndentText += kFirstIndentString;
                    else if (i == mLogIndent - 1)
                        mIndentText += kLastIndentString;
                    else
                        mIndentText += kIndentString;
                }
            }
        }

        Dictionary<string, object> mGlobals = new Dictionary<string, object>();

        [NLua.LuaHide]
        public void SetGlobalObject(string key, object value)
        {
            try
            {
                mGlobals[key] = value;

                if (mLua != null)
                    mLua[key] = value;
            }
            catch
            {
            }
        }

        [NLua.LuaHide]
        private object[] LoadScript(IGamePackage package, string path)
        {
            Output(string.Format("Loading Script: {0}", path));
            using (LoggingBlock block = new LoggingBlock(this))
            {
                try
                {
                    object[] result = null;

                    var variant = (this.OwnerState as Sessions.TrackerState)?.PackageInstance?.ActiveVariant;
                    using (Stream s = package.Open(path, variant))
                    {
                        if (s != null && s.Length > 0)
                        {
                            byte[] buffer = new byte[s.Length];
                            if (s.Read(buffer, 0, buffer.Length) == buffer.Length)
                            {
                                // Bypass NLua's DoString so we control
                                // the chunkname Lua sees. Prefix with
                                // "@" — Lua's standard convention for
                                // "this chunk came from a file" — so
                                // debug.getinfo's Source returns
                                // "@<path>", which the Lua debugger
                                // hook + tooling like VS Code can
                                // resolve cleanly. NLua's own DoString
                                // appears to lose the chunkname in
                                // some load paths, leaving every
                                // chunk reporting as the default
                                // "chunk".
                                result = LoadAndRunBuffer(buffer, "@" + path);
                            }
                        }
                        else
                        {
                            Output("Script not found");
                        }
                    }

                    return result;
                }
                catch (Exception e)
                {
                    Output(string.Format("A C# exception occurred while loading script: {0}", path));
                    using (LoggingBlock excBlock = new LoggingBlock(this))
                    {
                        // Phase 7.1 debug: dump the full exception (including
                        // any inner exceptions from NLua's marshalling layer)
                        // so we can see what's failing inside init.lua-driven
                        // C# calls. NLua wraps user-code exceptions; only the
                        // InnerException carries the real stack.
                        for (Exception ex = e; ex != null; ex = ex.InnerException)
                        {
                            OutputError("[{0}] {1}", ex.GetType().Name, ex.Message);
                            if (!string.IsNullOrEmpty(ex.StackTrace))
                                OutputError(ex.StackTrace);
                        }
                    }
                    return null;
                }
            }
        }

        public object[] LoadScript(string path)
        {
            return LoadScript(mPackage, path);
        }

        /// <summary>
        /// Lua-load a byte buffer with an explicit chunk name (we
        /// pass <c>"@&lt;path&gt;"</c>) and run it. Replaces the
        /// previous <c>NLua.Lua.DoString(byte[], string)</c> call
        /// inside <see cref="LoadScript"/> because NLua's load path
        /// was producing chunks whose <c>debug.getinfo().source</c>
        /// resolved to the default <c>"chunk"</c> string rather than
        /// the chunk name we passed — breaking the Lua debugger's
        /// breakpoint matching, since the hook had no way to tell
        /// which file was executing.
        /// </summary>
        [NLua.LuaHide]
        object[] LoadAndRunBuffer(byte[] buffer, string chunkName)
        {
            var L = mLua.State;
            int topBefore = L.GetTop();

            var loadStatus = L.LoadBuffer(buffer, chunkName);
            if (loadStatus != KeraLua.LuaStatus.OK)
            {
                string err = L.ToString(-1, callMetamethod: false) ?? "load error";
                L.SetTop(topBefore);
                throw new NLua.Exceptions.LuaScriptException(err, chunkName);
            }

            // PCall with multret (-1) so multi-return chunks surface
            // their results. errfunc=0 means use the default error
            // formatter (no traceback, but our _safe_call wrapper
            // adds one when needed for callbacks).
            var callStatus = L.PCall(0, -1, 0);
            if (callStatus != KeraLua.LuaStatus.OK)
            {
                string err = L.ToString(-1, callMetamethod: false) ?? "runtime error";
                L.SetTop(topBefore);
                throw new NLua.Exceptions.LuaScriptException(err, chunkName);
            }

            // Translate the return values off the stack into managed
            // objects, mirroring NLua's DoString return shape. For
            // the script-load path callers usually ignore the result;
            // we return an array of plain values (string/number/
            // boolean) without wrapping tables — pack scripts that
            // need a wrapped table go through the regular global
            // surface instead.
            int top = L.GetTop();
            int count = top - topBefore;
            if (count <= 0) return null;

            var results = new object[count];
            for (int i = 0; i < count; ++i)
            {
                int idx = topBefore + 1 + i;
                results[i] = ConvertStackValue(L, idx);
            }
            L.SetTop(topBefore);
            return results;
        }

        static object ConvertStackValue(KeraLua.Lua L, int idx)
        {
            switch (L.Type(idx))
            {
                case KeraLua.LuaType.Nil: return null;
                case KeraLua.LuaType.Boolean: return L.ToBoolean(idx);
                case KeraLua.LuaType.Number:
                    return L.IsInteger(idx) ? (object)L.ToInteger(idx) : (object)L.ToNumber(idx);
                case KeraLua.LuaType.String: return L.ToString(idx, callMetamethod: false);
                default: return L.ToString(idx, callMetamethod: false);
            }
        }

        public ScriptManager()
        {
            mClearLogCommand = new DelegateCommand(ClearLogOutput);
        }

        private void ClearLogOutput(object obj = null)
        {
            mLogOutput.Clear();
        }

        // Phase 7.13: per-state memory segments + timers, owned directly
        // by the ScriptManager. Pack init scripts call AddMemoryWatch /
        // AddMemoryTimer below; AutoTrackerExtension reads from
        // MemorySegments / MemoryTimers to drive its polling loop. Both
        // collections are cleared by Reset(); segments are forkable via
        // ModelTypeBase, so a state-fork carries the segments across
        // automatically (their LuaFunction callbacks are re-cloned on
        // the fork via RewireForkedLuaSegment after RunCloneFrom).
        readonly List<AutoTracking.LuaMemorySegment> mMemorySegments = new();
        public IReadOnlyList<AutoTracking.LuaMemorySegment> MemorySegments => mMemorySegments;

        readonly List<AutoTracking.MemoryTimer> mMemoryTimers = new();
        public IReadOnlyList<AutoTracking.MemoryTimer> MemoryTimers => mMemoryTimers;

        /// <summary>
        /// The <see cref="ScriptManager"/> this manager was forked from, or
        /// null if this manager was never the destination of a clone (e.g.
        /// the definitional state, or a fresh empty state). Set by
        /// <see cref="RunCloneFrom"/>; cleared by <see cref="Reset"/>.
        /// Consumers reach back through here to replay fork-time-only
        /// side effects whose Lua function references need to be remapped
        /// via <see cref="ForkCloner"/> (e.g. AutoTracker's memory-watch
        /// re-registration on fork).
        /// </summary>
        internal ScriptManager ForkSource { get; private set; }

        INotificationService mNotificationService = null;
        public void SetNotificationService(INotificationService service)
        {
            mNotificationService = service;
        }

        [NLua.LuaHide]
        public void Load(IGamePackage package)
        {
            mPackage = package;
            if (mPackage != null)
            {
                //  Dispose our previous Lua instance
                DisposeObjectAndDefault(ref mLua);

                BootstrapInterpreter();

                LoadScript(mPackage, "scripts/init.lua");
            }
        }

        /// <summary>
        /// Allocates a fresh <see cref="Lua"/> interpreter on this manager
        /// and installs the standard EmoTracker scaffolding: the
        /// <c>_output</c> bridge for <see cref="OutputRaw"/>, the os/io
        /// sandbox, the C#-side bridge globals (Tracker / Layout / etc.),
        /// and the <see cref="SystemLua"/> Lua-side helpers (_safe_call,
        /// print, import). Used by both <see cref="Load"/> (which then runs
        /// <c>scripts/init.lua</c>) and the Phase 5 fork path (which then
        /// hands the freshly-bootstrapped interpreter to
        /// <see cref="LuaStateCloner"/> to migrate live state from the
        /// source manager). Exposed at <c>internal</c> so unit tests in
        /// <c>EmoTracker.SourceGenerators.Tests</c> can drive the
        /// bootstrap directly without a full <see cref="IGamePackage"/>
        /// fixture; production callers go through <see cref="Load"/>.
        /// </summary>
        internal void BootstrapInterpreter()
        {
            mLua = new Lua();
            mLua.DebugHook += MLua_DebugHook;
            mLua.HookException += MLua_HookException;
            mLua.RegisterFunction("_output", this, this.GetType().GetMethod("OutputRaw"));

            // The C# upcall used by __et_safe_call_handler is reached
            // via ScriptHost (which is `this`) — see the Lua-side
            // SystemLua snippet that defines __et_dap_on_error as a
            // pure-Lua wrapper around `ScriptHost:OnLuaErrorForDap`.
            // We tried RegisterFunction(target, MethodInfo) and a
            // direct Action<string,string> delegate assignment, and
            // both failed NLua's argument-binding step at pcall time
            // ("Invalid arguments to method call" / "Cannot invoke
            // delegate"). Going through ScriptHost's userdata
            // method-dispatch path uses the same machinery as every
            // other Lua-to-C# call in the codebase — well-trodden
            // and known to bind (string, string) cleanly.

            //  Remove disallowed os methods
            try
            {
                using (LuaTable os = (LuaTable)mLua["os"])
                {
                    os["execute"] = null;
                    os["exit"] = null;
                    os["setlocale"] = null;
                }
            }
            catch
            {
            }

            if (mPackage != null && !mPackage.FlaggedAsUnsafe)
            {
                try
                {
                    using (LuaTable os = (LuaTable)mLua["os"])
                    {
                        os["tmpname"] = null;
                        os["rename"] = null;
                        os["getenv"] = null;
                        os["remove"] = null;
                    }
                }
                catch
                {
                    mLua["os"] = null;
                }

                mLua["io"] = null;
            }

            // Per-state bridge globals: each ScriptManager allocates its own
            // Tracker / Layout interfaces bound to its owning state, so
            // Lua-side `Tracker:AddItems(...)` / `Layout:Find(...)` route
            // into the correct state's catalogs without consulting any
            // global ambient slot.
            var ownerState = this.OwnerState as Sessions.TrackerState;
            if (ownerState == null)
                throw new InvalidOperationException(
                    "ScriptManager.BootstrapInterpreter requires the manager's OwnerState " +
                    "to be set to its TrackerState before bootstrapping. Bridge globals " +
                    "(Tracker, Layout) cannot be constructed without a state context.");
            mLua["Tracker"] = new TrackerScriptInterface(ownerState);
            mLua["Layout"] = new LayoutScriptInterface(ownerState);
            mLua["AccessibilityLevel"] = new AccessibilityLevel();
            mLua["NotificationType"] = new NotificationType();
            mLua["ScriptHost"] = this;
            mLua["ImageReference"] = new ImageReferenceProvider(ownerState);

            if (ApplicationSettings.Instance.SupportLua53VersionChecks)
                mLua["_VERSION"] = "Lua 5.3";

            foreach (var entry in mGlobals)
            {
                mLua[entry.Key] = entry.Value;
            }

            // Run SystemLua via LoadAndRunBuffer (rather than NLua's
            // DoString) so it gets a real chunkname Lua can report.
            // Without this, every frame from _safe_call /
            // __et_safe_call_handler / __et_dap_on_error in the
            // Lua-debugger call stack would surface as the default
            // "chunk", indistinguishable from any string-loaded
            // user code.
            LoadAndRunBuffer(System.Text.Encoding.UTF8.GetBytes(SystemLua), "@<system>");

            // Register this interpreter with the DAP server, if one
            // is running (dev-mode only). Each ScriptManager
            // (definitional, primary, every fork) gets its own
            // debuggee with a distinct DAP "thread" id so VS Code
            // can target a specific state's interpreter for stepping
            // and inspection.
            if (Debugging.LuaDebugServer.Instance != null)
            {
                try
                {
                    string label = ownerState?.Name;
                    if (string.IsNullOrEmpty(label))
                        label = "lua:" + this.DefinitionId.ToString("N").Substring(0, 8);
                    Debuggee = new Debugging.LuaDebuggee(mLua, label);
                    Debugging.LuaDebugServer.Instance.RegisterDebuggee(Debuggee);

                    // Pack-root resolution via a live callback so
                    // forked TrackerStates (whose PackageInstance is
                    // stamped AFTER BootstrapInterpreter runs) still
                    // get a path once the fork is registered. Without
                    // the indirection, debuggees on forks would
                    // permanently report a null PackRootPath and DAP
                    // sources would never carry a clickable file path.
                    Debuggee.PackRootPathResolver = () =>
                    {
                        try
                        {
                            return (this.OwnerState as Sessions.TrackerState)
                                ?.PackageInstance?.GamePackage?.Source?.PackPath
                                ?? mPackage?.Source?.PackPath;
                        }
                        catch { return null; }
                    };
                }
                catch (Exception ex)
                {
                    OutputWarning("[Debugger] failed to register state: {0}", ex.Message);
                    Debuggee = null;
                }
            }
        }

        /// <summary>
        /// Lua-side upcall registered as <c>__et_dap_on_error</c>.
        /// Invoked from the <c>__et_safe_call_handler</c> xpcall
        /// error handler whenever a Lua error fires under
        /// <see cref="SafeCall"/>. When a DAP session is attached
        /// and exception break is armed, this pauses the executor
        /// thread inside the error handler so VS Code can inspect
        /// locals before the stack unwinds. No-op when no debugger
        /// is attached or when exception break is off — the regular
        /// traceback path continues unaffected.
        /// </summary>
        // Public (no [LuaHide]) so NLua's userdata-method dispatch
        // surfaces this on the `ScriptHost` global. The Lua-side
        // __et_dap_on_error in SystemLua wraps the call so the
        // xpcall error handler reaches us without hitting NLua's
        // brittle delegate / RegisterFunction binding paths (those
        // failed with "Invalid arguments to method call" for
        // reasons that don't reproduce against the userdata path).
        public void OnLuaErrorForDap(string err, string traceback)
        {
            Debugging.LuaDebuggee.Info("OnLuaErrorForDap: state='{0}' err='{1}'",
                (this.OwnerState as Sessions.TrackerState)?.Name ?? "?",
                err == null ? "" : (err.Length > 120 ? err.Substring(0, 120) + "…" : err));

            try { Debuggee?.EnterExceptionPause(err, traceback); }
            catch { /* never let debugger plumbing throw into Lua */ }
        }

        [NLua.LuaHide]
        public void OutputRaw(string text, string color = "Goldenrod")
        {
            if (text != null)
            {
                using (StringReader reader = new StringReader(text))
                {
                    string s = reader.ReadLine();
                    while (s != null)
                    {
                        while (mLogOutput.Count > 500)
                            mLogOutput.RemoveAt(0);

                        mLogOutput.Add(new LogLine() { Text = string.Format("{0}{1}", mIndentText, s), Color = color });
                        s = reader.ReadLine();
                    }
                }
            }
        }


        [NLua.LuaHide]
        public void Output(string text)
        {
            if (text != null)
                OutputRaw(text, "DarkGray");
        }

        [NLua.LuaHide]
        public void Output(string format, params object[] args)
        {
            if (format != null)
                OutputRaw(string.Format(format, args), "DarkGray");
        }

        [NLua.LuaHide]
        public void OutputException(Exception e)
        {
            JsonReaderException jsonException = e as JsonReaderException;
            LuaException luaException = e as LuaException;
            if (jsonException != null)
            {
                OutputError("JSON Parse Error");
                using (new LoggingBlock(this))
                {
                    OutputError(jsonException.Message);

                    if (!string.IsNullOrWhiteSpace(jsonException.HelpLink))
                        OutputError("  For more information, see: {0}", jsonException.HelpLink);
                }
            }
            else if (luaException != null)
            {
                OutputError("Lua Execution Error");
                using (new LoggingBlock(this))
                {
                    OutputError(luaException.Message);
                }
            }
            else
            {
                OutputError("Exception: {0}\n{1}", e.Message, e.StackTrace);
            }
        }

        [NLua.LuaHide]
        public void OutputError(string text)
        {
            if (text != null)
                OutputRaw(text, "Red");
        }

        [NLua.LuaHide]
        public void OutputError(string format, params object[] args)
        {
            if (format != null)
                OutputError(string.Format(format, args));
        }

        [NLua.LuaHide]
        public void OutputWarning(string text)
        {
            if (text != null)
                OutputRaw(text, "Yellow");
        }

        [NLua.LuaHide]
        public void OutputWarning(string format, params object[] args)
        {
            if (format != null)
                OutputWarning(string.Format(format, args));
        }

        private void MLua_HookException(object sender, NLua.Event.HookExceptionEventArgs e)
        {
            if (e != null)
                OutputError(e.ToString());

            System.Diagnostics.Debug.WriteLine(e);
        }

        private void MLua_DebugHook(object sender, NLua.Event.DebugHookEventArgs e)
        {
            if (e != null)
                OutputError(e.ToString());

            System.Diagnostics.Debug.WriteLine(e);
        }

        [NLua.LuaHide]
        public void Reset()
        {
            // Unregister + dispose the debuggee BEFORE closing the
            // underlying Lua state. The debuggee's Dispose path tries
            // to remove its hook from the state; doing it post-Close
            // would AV.
            if (Debuggee != null)
            {
                try { Debugging.LuaDebugServer.Instance?.UnregisterDebuggee(Debuggee); } catch { }
                try { Debuggee.Dispose(); } catch { }
                Debuggee = null;
            }

            if (mLua != null)
            {
                mLua.Close();
                mLua = null;
            }

            // ForkCloner holds Lua-side helpers on its mSource interpreter;
            // when this manager is the source, mLua is now closed and any
            // future Resolve() call would NRE on the closed interpreter.
            // Drop the cloner so callers see "no clone happened" rather
            // than crashing.
            ForkCloner = null;
            ForkSource = null;

            // Dispose + drop the per-state memory segments + timers.
            // Their LuaFunction callbacks reference the about-to-be-
            // closed interpreter; holding them past Reset would dangle.
            // The pack's next init.lua re-registers fresh segments via
            // AddMemoryWatch with the new interpreter's LuaFunctions.
            bool segmentsHadEntries = mMemorySegments.Count > 0;
            foreach (var seg in mMemorySegments)
            {
                try { seg.Dispose(); } catch { /* defensive */ }
            }
            mMemorySegments.Clear();
            if (segmentsHadEntries)
                NotifyPropertyChanged(nameof(MemorySegments));

            bool timersHadEntries = mMemoryTimers.Count > 0;
            foreach (var t in mMemoryTimers)
            {
                try { t.Dispose(); } catch { /* defensive */ }
            }
            mMemoryTimers.Clear();
            if (timersHadEntries)
                NotifyPropertyChanged(nameof(MemoryTimers));

            // mExpressionCache is a per-state cache of provider-count
            // results keyed on Lua-callable codes. After Reset the codes
            // would resolve through a fresh interpreter; drop stale
            // entries (per plan §5.9, the cloner inherits an empty cache
            // on fork via field initializer — keep that semantic on Reset
            // too).
            mExpressionCache.Clear();

            ClearLogOutput();
        }

        /// <summary>
        /// Disposes the manager — currently a thin wrapper over
        /// <see cref="Reset"/>. Future per-state managers in Phase 6
        /// will also tear down their bridge instances, memory-watch
        /// registrations, etc.; the override is here as the lifecycle
        /// hook those changes will plug into.
        /// </summary>
        public override void Dispose()
        {
            Reset();
            base.Dispose();
        }

        /// <summary>
        /// Invokes a LuaFunction via xpcall with debug.traceback as the error handler.
        /// On success, returns the function's results. On failure, throws a LuaException
        /// whose message includes the full Lua call stack at the point of failure.
        /// </summary>
        [NLua.LuaHide]
        public object[] SafeCall(LuaFunction func, params object[] args)
        {
            if (mLua == null)
                throw new InvalidOperationException("No Lua environment is loaded");

            using (LuaFunction safeCall = mLua["_safe_call"] as LuaFunction)
            {
                if (safeCall == null)
                    return func.Call(args);  // Fallback if _safe_call not available

                // Build argument array: _safe_call(func, arg1, arg2, ...)
                object[] callArgs = new object[args.Length + 1];
                callArgs[0] = func;
                Array.Copy(args, 0, callArgs, 1, args.Length);

                // Wall-clock the call so /slowcalls can rank by total
                // time. Stopwatch.GetTimestamp is ~10 ns; in non-dev
                // builds the static branch folds out entirely.
                long startTicks = sCallTimingEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                object[] result;
                try
                {
                    result = safeCall.Call(callArgs);
                }
                finally
                {
                    if (sCallTimingEnabled)
                    {
                        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;
                        try { RecordCall(func, elapsed); }
                        catch { /* timing must never break the call */ }
                    }
                }

                if (result == null || result.Length == 0)
                    return null;

                bool ok = Convert.ToBoolean(result[0]);
                if (!ok)
                {
                    string errorMsg = result.Length > 1 ? result[1]?.ToString() : "Unknown Lua error";
                    // Phase 7 debug: log the stack trace + scriptmanager identity
                    // when SafeCall fails to help diagnose stale-LuaFunction issues
                    // (where func is from a different Lua state than mLua).
                    try
                    {
                        var st = new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: false);
                        System.IO.File.AppendAllText("/tmp/lua_safecall_fail.log",
                            $"[{System.DateTime.Now:HH:mm:ss.fff}] SafeCall fail: sm={this.GetHashCode()} mLua={mLua?.GetHashCode()} func.Type={func?.GetType().FullName} err={errorMsg.Substring(0, System.Math.Min(120, errorMsg.Length))}\n{st}\n----\n");
                    } catch {}
                    throw new LuaException(errorMsg);
                }

                // Strip the leading 'true' status from the results
                if (result.Length <= 1)
                    return null;

                object[] actualResults = new object[result.Length - 1];
                Array.Copy(result, 1, actualResults, 0, actualResults.Length);
                return actualResults;
            }
        }

        [NLua.LuaHide]
        public object[] ExecuteLuaString(string luaCode)
        {
            if (mLua == null)
                throw new InvalidOperationException("No Lua environment is loaded");
            return mLua.DoString(luaCode);
        }

        [NLua.LuaHide]
        public object GetLuaGlobal(string name)
        {
            if (mLua == null)
                throw new InvalidOperationException("No Lua environment is loaded");
            return mLua[name];
        }

        [NLua.LuaHide]
        public bool IsLuaLoaded
        {
            get { return mLua != null; }
        }

        [NLua.LuaHide]
        public object FindObjectForCode(string code)
        {
            return null;
        }

        struct CacheEntry
        {
            public uint count;
            public AccessibilityLevel maxAccessibility;
        }

        private Dictionary<string, CacheEntry> mExpressionCache = new Dictionary<string, CacheEntry>();

        [NLua.LuaHide]
        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility)
        {
            maxAccessibility = AccessibilityLevel.Normal;
            uint count = 0;

            //  First check the cache to see if we've processed this already since the last invalidate
            CacheEntry cachedResult;
            if (mExpressionCache.TryGetValue(code, out cachedResult))
            {
                maxAccessibility = cachedResult.maxAccessibility;
                return cachedResult.count;
            }

            try
            {
                string[] tokens = code.Split('|');
                for (int i = 0; i < tokens.Length; ++i)
                {
                    tokens[i] = tokens[i].Trim();
                }

                using (LuaFunction func = mLua[tokens[0]] as LuaFunction)
                {
                    if (func != null)
                    {
                        object[] result;

                        IEnumerable<string> args = tokens.Skip(1);
                        if (args != null && args.Any())
                        {
                            result = SafeCall(func, args.ToArray<object>());
                        }
                        else
                        {
                            result = SafeCall(func);
                        }

                        if (result == null)
                        {
                            OutputError("Lua function `{0}` did not return a count. All Lua functions used as logical expressions must return a count.", code);
                        }
                        else
                        {
                            if (result.Length >= 2)
                            {
                                AccessibilityLevel? luaLevel = result[1] as AccessibilityLevel?;
                                if (luaLevel != null && luaLevel.HasValue)
                                    maxAccessibility = luaLevel.Value;
                            }

                            count = Convert.ToUInt32(result.First());
                        }
                    }
                    else
                    {
                        OutputError("Couldn't execute lua function `{0}` because it does not exist", code);
                    }
                }
            }
            catch (Exception e)
            {
                Output(e.ToString());
            }

            //  Cache the results
            mExpressionCache[code] = new CacheEntry() { count = count, maxAccessibility = maxAccessibility };

            return count;
        }

        [LuaHide]
        internal void ClearExpressionCache()
        {
            mExpressionCache.Clear();
        }

        bool mbInPostLogicUpdate = false;

        /// <summary>
        /// Pre-Phase-5 nested enum, retained verbatim so the existing
        /// <c>Sessions.SessionContext.ActiveState?.Scripts.InvokeStandardCallback(ScriptManager.StandardCallback.X, ...)</c>
        /// callsites keep compiling. Values mirror
        /// <see cref="EmoTracker.Core.DataModel.StandardCallback"/> exactly
        /// (same names, same order); the <see cref="IScriptManager"/>
        /// surface accepts the Core enum and casts internally.
        /// </summary>
        public enum StandardCallback
        {
            AccessibilityUpdating,
            AccessibilityUpdated,
            StartLoadingSaveFile,
            FinishLoadingSaveFile,
            PackReady,
            AutoTrackerStarted,
            AutoTrackerStopped,
            LocationUpdating,
            LocationUpdated
        }

        /// <summary>
        /// <see cref="IScriptManager"/> implementation: forwards to the
        /// existing <see cref="InvokeStandardCallback(StandardCallback, object[])"/>
        /// after casting between the Core-side and Data-side enums (which
        /// have identical underlying values). New holder-aware callsites
        /// (model.GetScriptManager().InvokeStandardCallback(...)) flow
        /// through this surface; legacy <c>Sessions.SessionContext.ActiveState?.Scripts.InvokeStandardCallback</c>
        /// callers go straight to the public overload.
        /// </summary>
        void IScriptManager.InvokeStandardCallback(EmoTracker.Core.DataModel.StandardCallback callback, params object[] args)
        {
            InvokeStandardCallback((StandardCallback)callback, args);
        }

        [LuaHide]
        public void InvokeStandardCallback(StandardCallback callback, params object[] args)
        {
            string functionName = null;
            switch (callback)
            {
                case StandardCallback.AccessibilityUpdating:
                    functionName = "tracker_on_accessibility_updating";
                    break;

                case StandardCallback.AccessibilityUpdated:
                    functionName = "tracker_on_accessibility_updated";
                    break;

                case StandardCallback.StartLoadingSaveFile:
                    functionName = "tracker_on_begin_loading_save_file";
                    break;

                case StandardCallback.FinishLoadingSaveFile:
                    functionName = "tracker_on_finish_loading_save_file";
                    break;

                case StandardCallback.PackReady:
                    functionName = "tracker_on_pack_ready";
                    break;

                case StandardCallback.AutoTrackerStarted:
                    functionName = "autotracker_started";
                    break;

                case StandardCallback.AutoTrackerStopped:
                    functionName = "autotracker_stopped";
                    break;

                case StandardCallback.LocationUpdating:
                    functionName = "tracker_on_location_updating";
                    break;

                case StandardCallback.LocationUpdated:
                    functionName = "tracker_on_location_updated";
                    break;
            }

            if (!mbInPostLogicUpdate)
            {
                try
                {
                    mbInPostLogicUpdate = true;

                    if (!string.IsNullOrWhiteSpace(functionName))
                    {
                        using (LuaFunction func = mLua[functionName] as LuaFunction)
                        {
                            if (func != null)
                                SafeCall(func, args);
                        }
                    }
                }
                catch (Exception e)
                {
                    OutputException(e);
                }
                finally
                { 
                    mbInPostLogicUpdate = false;
                }
            }
        }

        /// <summary>
        /// Pack-script entry point for <c>ScriptHost:AddMemoryWatch(...)</c>.
        /// Mints a per-state <see cref="LuaMemorySegment"/>, registers it
        /// with the owning state's <see cref="IModelResolver"/> (so the
        /// LuaStateCloner can remap pack-cached segment references via
        /// DefinitionId on fork), and returns it to Lua. The segment is
        /// stored on this manager's <see cref="MemorySegments"/> list —
        /// the per-state <see cref="AutoTrackerExtension"/> reads from
        /// there to drive its polling loop.
        ///
        /// <para>
        /// No <c>IMemoryWatchService</c> bridge is involved any more
        /// (Phase 7.13). The segment IS the bridge: it owns its
        /// <see cref="LuaFunction"/> callback, dispatches onto the UI
        /// thread inside <see cref="LuaMemorySegment.OnSegmentDataUpdated"/>,
        /// and forks natively as a <see cref="ModelTypeBase"/>.
        /// </para>
        /// </summary>
        public AutoTracking.LuaMemorySegment AddMemoryWatch(string name, ulong startAddress, ulong length, LuaFunction callback, int period = 1000)
        {
            var segment = new AutoTracking.LuaMemorySegment(name, startAddress, length, period);
            segment.OwnerState = this.OwnerState;
            segment.Callback = callback;
            mMemorySegments.Add(segment);
            (this.OwnerState as Sessions.TrackerState)?.RegisterModel(segment);
            // Notify so subscribers (e.g. AutoTrackerExtension's Active
            // property) re-evaluate visibility against the new count.
            // The List<> isn't observable, so we surface mutations here.
            NotifyPropertyChanged(nameof(MemorySegments));
            return segment;
        }

        public void RemoveMemoryWatch(IMemorySegment segment)
        {
            if (segment is AutoTracking.LuaMemorySegment lms)
            {
                if (mMemorySegments.Remove(lms))
                {
                    lms.Dispose();
                    NotifyPropertyChanged(nameof(MemorySegments));
                }
            }
        }

        /// <summary>
        /// Pack-script entry point for <c>ScriptHost:AddMemoryTimer(...)</c>.
        /// Mints a periodic per-state callback driven by the autotracker
        /// polling loop (no memory window — just a "tick every Period ms"
        /// hook). Stored on <see cref="MemoryTimers"/> for the per-state
        /// <see cref="AutoTrackerExtension"/> to consume.
        /// </summary>
        public AutoTracking.MemoryTimer AddMemoryTimer(string name,
            Func<AutoTracking.IAutoTrackingProvider, Packages.PackageManager.Game, bool> callback,
            int period = 500)
        {
            var timer = new AutoTracking.MemoryTimer(name, callback, period);
            mMemoryTimers.Add(timer);
            NotifyPropertyChanged(nameof(MemoryTimers));
            return timer;
        }

        public void RemoveMemoryTimer(AutoTracking.MemoryTimer timer)
        {
            if (timer == null) return;
            if (mMemoryTimers.Remove(timer))
            {
                timer.Dispose();
                NotifyPropertyChanged(nameof(MemoryTimers));
            }
        }

        /// <summary>
        /// Phase 7.13 fork-time hook: adds an already-forked
        /// <see cref="LuaMemorySegment"/> (produced by
        /// <see cref="MemorySegment.Fork"/> on the source) to this
        /// manager's <see cref="MemorySegments"/> list. Called by
        /// <see cref="Sessions.TrackerState.Fork"/> BEFORE
        /// <see cref="RunCloneFrom"/> so the segment is registered on the
        /// fork before the cloner walks pack-script tables.
        /// </summary>
        internal void AdoptForkedSegment(AutoTracking.LuaMemorySegment forkSegment)
        {
            if (forkSegment == null) return;
            mMemorySegments.Add(forkSegment);
            NotifyPropertyChanged(nameof(MemorySegments));
        }

        /// <summary>
        /// Phase 7.13 fork-time hook: re-clones the source's
        /// <see cref="LuaMemorySegment.Callback"/> through this fork's
        /// <see cref="ForkCloner"/> so the Callback's underlying
        /// LuaFunction binds to the FORK's interpreter rather than the
        /// source's. Called by <see cref="Sessions.TrackerState.Fork"/>
        /// after <see cref="RunCloneFrom"/> populates the cloner's
        /// identity map.
        /// </summary>
        internal void RewireForkedLuaSegment(AutoTracking.LuaMemorySegment forkSegment, AutoTracking.LuaMemorySegment srcSegment)
        {
            if (forkSegment == null || srcSegment == null) return;
            if (ForkCloner == null) return;
            if (srcSegment.Callback == null) return;
            forkSegment.Callback = ForkCloner.CloneValue(srcSegment.Callback) as LuaFunction;
        }

        public INotificationService NotificationService
        {
            get { return mNotificationService; }
        }

        public void PushMarkdownNotification(NotificationType type, string markdown, int timeout = -1)
        {
            if (mNotificationService != null)
                mNotificationService.PushMarkdownNotification(type, markdown, timeout);
        }

        /// <summary>
        /// Wraps <paramref name="target"/> in a fork-safe Lua proxy table
        /// (a metatabled <see cref="LuaTable"/> whose <c>__index</c> /
        /// <c>__newindex</c> resolve through a <see cref="LuaModelRef"/> on
        /// every access). Returns the same proxy table for repeated calls
        /// against the same target on the same state, so Lua scripts can
        /// rely on raw <c>==</c> identity and use proxies as table keys.
        ///
        /// <para>
        /// On fork, the <see cref="LuaStateCloner"/> visits every proxy
        /// reachable from <c>_G</c> (including the per-state proxy cache)
        /// and synthesizes a fresh <see cref="LuaModelRef"/> bound to the
        /// destination state's resolver — so the fork's proxies resolve
        /// into the fork's own model graph without any pack-script
        /// involvement.
        /// </para>
        ///
        /// <para>
        /// Returns null if <paramref name="target"/> is null, the manager
        /// has no Lua interpreter loaded, or the target has no
        /// <see cref="ModelTypeBase.DefinitionId"/> (which would mean we
        /// have no stable identity to remap on fork).
        /// </para>
        /// </summary>
        [NLua.LuaHide]
        public LuaTable WrapAsLuaProxy(ModelTypeBase target)
        {
            if (target == null) return null;
            if (mLua == null) return null;
            if (target.DefinitionId == Guid.Empty) return null;

            // Resolver: prefer this manager's owning state (each state
            // resolves its own graph), fall back to the model's owner
            // state if the manager isn't yet bound (atypical).
            var resolver = (this.OwnerState as IModelResolver)
                ?? (target.OwnerState as IModelResolver);
            if (resolver == null) return null;

            var modelRef = new LuaModelRef(resolver, target.DefinitionId);

            using (var wrapFunc = mLua["__et_wrap_model_ref"] as LuaFunction)
            {
                if (wrapFunc == null) return null;
                var result = wrapFunc.Call(modelRef, modelRef.DefinitionIdString);
                if (result == null || result.Length == 0) return null;
                return result[0] as LuaTable;
            }
        }

        public LuaItem CreateLuaItem()
        {
            LuaItem item = new LuaItem();
            // Register the freshly-created LuaItem into the owning state's
            // ItemDatabase. This ScriptManager belongs to the state that
            // is currently running its init.lua / pack scripts.
            var ownerState = this.OwnerState as Sessions.TrackerState;
            if (ownerState != null)
            {
                item.OwnerState = ownerState;
                ownerState.Items.RegisterItem(item);
            }
            return item;
        }

        // -------- Phase 5 fork plumbing -----------------------------------

        /// <summary>
        /// The <see cref="LuaStateCloner"/> used during this manager's
        /// <see cref="OnForked"/> step. Step 7's <c>LuaItem.OnForked</c>
        /// reads this on the fork's manager to resolve its source-side
        /// <c>mItemState</c> / callback references to the destination's
        /// clones — without it those references would point at the
        /// source's now-orphaned interpreter and any callback would
        /// either no-op or exec against the wrong state.
        /// <para>
        /// Null on a freshly-created (non-forked) manager. Holds the
        /// cloner used during the most recent fork until the next fork
        /// or until the manager is reset / disposed.
        /// </para>
        /// </summary>
        internal LuaStateCloner ForkCloner { get; private set; }

        // Phase 6 step 8: extra bridge-identity entries to merge into the
        // bridge map during the next OnForked. Set by TrackerState.Fork so
        // closure upvalues that captured the source state's per-state model
        // instances (Items / Locations / Maps / etc.) get remapped to the
        // fork's instances at clone time. Consumed (cleared) by OnForked.
        internal IReadOnlyDictionary<object, object> PendingExtraBridges { get; set; }

        /// <summary>
        /// Phase 5: produces a fork of this manager whose Lua interpreter
        /// is a deep copy of this manager's live Lua state. The fork's
        /// interpreter shares definition data (mPackage, mGlobals,
        /// service refs) by reference but allocates a fresh
        /// <see cref="Lua"/> with its own bridge bindings; pack-author
        /// state from <c>scripts/init.lua</c> and any subsequent runtime
        /// mutations carry across via <see cref="LuaStateCloner.CloneAll"/>.
        /// </summary>
        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
            var copy = new ScriptManager();
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            return copy;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (ScriptManager)source;

            // Definition-tier state: share by reference per plan §5.2 — these
            // are pack-defined values that are constant across the fork
            // family.
            //
            // mGlobals is the externally-injected globals dict (populated
            // via SetGlobalObject during pack load). Shared-by-reference is
            // intentional: pack-load-time injections are constant across
            // forks. Note this means SetGlobalObject called on a fork
            // post-construction also mutates the source's view — that's
            // not per-state isolation. SetGlobalObject is intended for
            // pack-load-time use only; runtime per-state global mutation
            // goes through ExecuteLuaString or direct mLua[k] = v on the
            // fork's interpreter.
            //
            // Phase 7.13: memory segments fork natively as ModelTypeBase
            // (TrackerState.Fork iterates src.MemorySegments and forks
            // each, then RewireForkedLuaSegment re-clones the LuaFunction
            // callback through ForkCloner). No mMemoryService bridge
            // needed.
            mPackage = src.mPackage;
            mGlobals = src.mGlobals;
            mNotificationService = src.mNotificationService;

            // Note: LogOutput copy is NOT done here. TrackerState.Fork
            // bypasses this OnForked path (it manually bootstraps a
            // fresh ScriptManager + runs RunCloneFrom rather than going
            // through InitializeAsForkOf), so the production fork flow
            // does the copy explicitly via SeedLogOutputFromFork below.

            // mExpressionCache — per plan §5.9, a forked manager's cache
            // starts empty rather than copying the source's. The field
            // initializer on the new ScriptManager instance already gives
            // us an empty Dictionary, so no explicit clear is needed; this
            // comment marks the intentional "empty on fork" choice so
            // future readers don't add a copy here.

            // Allocate a fresh interpreter on this fork and run the
            // scaffolding bootstrap (system Lua, bridge globals, sandbox).
            // After this returns, this.mLua is a viable destination for the
            // cloner to migrate the source's reachable Lua state into.
            DisposeObjectAndDefault(ref mLua);
            BootstrapInterpreter();

            // If the source has no Lua interpreter (Load was never called or
            // Reset was), there's nothing to clone — leave the fork's freshly-
            // bootstrapped interpreter as-is.
            if (src.mLua == null) return;

            // Build the bridge identity map: source.bridge → destination.bridge
            // for every C# bridge global ScriptManager binds. In Phase 5 the
            // bridges are still singletons, so each entry is just an identity
            // mapping (src.Tracker == dst.Tracker because both pull from
            // TrackerScriptInterface.Instance). Populating the map is still
            // the right thing for two reasons:
            //
            //   1. The cloner's CloneValue path warns when it falls through
            //      to "passing through reference" for unrecognized userdata.
            //      Without bridge entries, every closure that captures a
            //      bridge as an upvalue emits a noisy warning. With entries,
            //      the bridge-map lookup short-circuits cleanly.
            //
            //   2. Phase 6's per-state bridges will replace each
            //      TrackerScriptInterface.Instance with a state-bound
            //      instance — at which point src.Tracker ≠ dst.Tracker and
            //      this map's entries become the meaningful remap. The
            //      wiring point is in place from Phase 5 onward.
            var bridgeMap = new Dictionary<object, object>();
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "Tracker");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "Layout");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "AccessibilityLevel");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "NotificationType");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "ScriptHost");
            AddBridgeMapping(bridgeMap, src.mLua, mLua, "ImageReference");

            // Phase 6 step 8: merge extra bridge-identity entries — typically
            // (sourceModel → forkModel) pairs that TrackerState.Fork built
            // while walking the source's catalogs. Closures whose upvalues
            // captured a per-state model object (e.g. a pack-author closure
            // that holds a specific Item reference) get remapped to the
            // fork's corresponding model at clone time, satisfying plan
            // §5.9's "closures capturing per-state model objects" risk.
            if (PendingExtraBridges != null)
            {
                foreach (var kvp in PendingExtraBridges)
                {
                    if (!bridgeMap.ContainsKey(kvp.Key))
                        bridgeMap[kvp.Key] = kvp.Value;
                }
                // Consume — pending bridges are single-use per fork.
                PendingExtraBridges = null;
            }

            // Pass the destination state as the cloner's resolver so any
            // LuaModelRef instances embedded in cloned proxy tables (built
            // by ScriptManager.WrapAsLuaProxy on the source) get rebound to
            // resolve into the fork's model graph.
            ForkCloner = new LuaStateCloner(
                src.mLua, mLua, bridgeMap, OutputWarning,
                destinationResolver: this.OwnerState as IModelResolver);
            ForkCloner.CloneAll();
        }

        // Helper: read the same-named global from src's and dst's interpreters
        // and add the (srcValue → dstValue) entry to the bridge map. No-op
        // if either side is null (the bridge isn't installed for some
        // reason). When src and dst hold the same C# reference (Phase 5's
        // singleton case), the map entry is identity — harmless and
        // satisfies the cloner's bridge-lookup short-circuit.
        static void AddBridgeMapping(Dictionary<object, object> map, Lua src, Lua dst, string globalName)
        {
            object srcVal = src[globalName];
            object dstVal = dst[globalName];
            if (srcVal != null && dstVal != null && !map.ContainsKey(srcVal))
            {
                map[srcVal] = dstVal;
            }
        }

        /// <summary>
        /// Phase 6 step 8: drives the cloner explicitly with a caller-
        /// supplied additional bridge-identity map. Used by
        /// <c>TrackerState.Fork()</c> which needs to extend the standard
        /// 6-bridge map with (sourceModel → forkModel) entries built
        /// during the catalog walk so closure upvalues capturing
        /// per-state model objects are remapped at clone time.
        ///
        /// <para>
        /// Prerequisite: <see cref="BootstrapInterpreter"/> has been
        /// called on this manager. The standard 6-bridge map is built
        /// from this manager's bridge globals (Tracker, Layout, etc.) +
        /// <paramref name="source"/>'s; <paramref name="extraBridges"/>
        /// entries are merged on top.
        /// </para>
        /// </summary>
        internal void RunCloneFrom(ScriptManager source, IReadOnlyDictionary<object, object> extraBridges)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (mLua == null)
                throw new InvalidOperationException("BootstrapInterpreter must be called before RunCloneFrom");
            if (source.mLua == null) return; // nothing to clone

            var bridgeMap = new Dictionary<object, object>();
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "Tracker");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "Layout");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "AccessibilityLevel");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "NotificationType");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "ScriptHost");
            AddBridgeMapping(bridgeMap, source.mLua, mLua, "ImageReference");

            if (extraBridges != null)
            {
                foreach (var kvp in extraBridges)
                {
                    if (!bridgeMap.ContainsKey(kvp.Key))
                        bridgeMap[kvp.Key] = kvp.Value;
                }
            }

            // Same resolver routing as OnForked — see comment there. Both
            // entry points feed pack-script captures of model proxies
            // through to the fork's resolver.
            ForkCloner = new LuaStateCloner(
                source.mLua, mLua, bridgeMap, OutputWarning,
                destinationResolver: this.OwnerState as IModelResolver);
            ForkCloner.CloneAll();
            // Stash the source so post-fork side effects (e.g.
            // AutoTracker memory-watch replay) can walk source-side
            // registrations and remap LuaFunction references through the
            // cloner. Cleared by Reset.
            ForkSource = source;
        }

        /// <summary>
        /// Phase 5 step 7: rewires a forked <see cref="LuaItem"/>'s NLua
        /// reference fields (<see cref="LuaItem.ItemState"/> + the eight
        /// LuaFunction callbacks) from the source's interpreter to this
        /// fork's interpreter, using the cloner that ran during this
        /// manager's own <see cref="OnForked"/>.
        ///
        /// <para>
        /// Call sequence (orchestrated by Phase 6's TrackerState fork; in
        /// Phase 5 by tests / migration helpers):
        /// <list type="number">
        ///   <item><c>destManager = sourceManager.Fork()</c> — populates
        ///         <see cref="ForkCloner"/> on the destination.</item>
        ///   <item><c>destItem = sourceItem.Fork()</c> — runs
        ///         <see cref="LuaItem.OnForked"/>, which copies the
        ///         source's references verbatim onto destItem (so they're
        ///         not null but point at the source's interpreter).</item>
        ///   <item><c>destManager.RewireForkedLuaItem(destItem, sourceItem)</c>
        ///         — walks each reference through
        ///         <see cref="LuaStateCloner.Resolve"/>; destItem's
        ///         references now point at this fork's clones.</item>
        /// </list>
        /// </para>
        ///
        /// <para>
        /// The <paramref name="source"/> argument is the original LuaItem
        /// (still on the source state). It's needed so we know which
        /// source-side references to look up in the cloner — destItem's
        /// own references (post-OnForked) work too because OnForked copied
        /// them verbatim, but using <paramref name="source"/> directly is
        /// clearer and decouples the rewire from any speculative
        /// post-OnForked mutation on destItem.
        /// </para>
        ///
        /// <para>
        /// If <see cref="ForkCloner"/> is null (this manager was never
        /// forked, e.g. it's the source) or if the source's references
        /// weren't reachable from <c>_G</c> at clone time
        /// (<see cref="LuaStateCloner.Resolve"/> returns null), the
        /// corresponding fork-side field is set to null. Calling such a
        /// callback then no-ops — better than holding an orphan source
        /// reference.
        /// </para>
        /// </summary>
        public void RewireForkedLuaItem(LuaItem destination, LuaItem source)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ForkCloner == null) return;

            // RewireWithCloner reads each of the 9 reference fields off
            // source, runs them through the cloner, and assigns the
            // destination clone onto destination. It also pins
            // destination.mOwnerScriptManager = this so fork-side callback
            // invocations (OnLeftClick / Save / etc.) execute against
            // this fork's interpreter rather than the source's
            // _safe_call wrapper.
            destination.RewireWithCloner(ForkCloner, source, this);
        }
    }
}
