using EmoTracker.Core.DataModel;
using NLua;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Scripting
{
    /// <summary>
    /// Phase 5: deep-clones a source <see cref="Lua"/> interpreter's reachable
    /// state into a destination interpreter, used by <c>ScriptManager.Fork()</c>
    /// to migrate live Lua state per-state at fork time without replaying
    /// <c>scripts/init.lua</c> (which would lose any unsaved scratch globals
    /// and pack-author-defined caches).
    ///
    /// <para>
    /// This step-2 implementation handles primitives, tables (with cycle and
    /// shared-subtree preservation), and metatables. Lua-side closures and
    /// upvalue remapping are added in step 3; until then closures encountered
    /// in <c>_G</c> are skipped with a logged warning so packs that don't
    /// rely on global closures continue to fork correctly.
    /// </para>
    /// </summary>
    internal sealed class LuaStateCloner
    {
        readonly Lua mSource;
        readonly Lua mDestination;
        readonly IReadOnlyDictionary<object, object> mBridgeIdentityMap;
        readonly Action<string> mWarn;
        // Optional: resolver to bind every cloned LuaModelRef against on the
        // destination. When null, the cloner falls through to passing the
        // source's LuaModelRef instances by reference — which leaves the
        // fork's proxies resolving into the SOURCE state's graph and is
        // almost never what callers want. ScriptManager.OnForked /
        // RunCloneFrom always pass the destination state.
        readonly IModelResolver mDestinationResolver;

        // Identity map: source-side reference-typed Lua values → destination.
        // Keyed by an integer id assigned via a Lua-side helper table
        // (mSource["__et_cloner_idmap"]) so two C# wrappers around the same
        // Lua object — which NLua creates afresh on each access — map to
        // the same id. The underlying Lua tables compare by reference
        // identity natively (they're the actual Lua values), giving us a
        // sound cycle / shared-subtree primitive that doesn't depend on
        // NLua's wrapper Equals semantics.
        //
        // External callers (LuaItem.OnForked, ScriptManager.Fork's per-item
        // wire-up) query this map through <see cref="Resolve"/>, which runs
        // the source-side wrapper through the Lua helper to get its id
        // before looking up the destination clone. This is the correct
        // path for any caller holding a LuaTable / LuaFunction reference
        // that wasn't the exact wrapper we walked during CloneAll.
        readonly Dictionary<int, object> mIdentityMap = new Dictionary<int, object>();
        bool mIdMapInitialized;

        // C# bridge globals re-injected by ScriptManager.Load — never cloned
        // from the source (the destination's ScriptManager already binds its
        // own state-aware instances during Load). These are skipped even if
        // the destination doesn't currently have them registered (e.g. in
        // tests that don't run a full Load).
        static readonly HashSet<string> BridgeGlobalNames = new HashSet<string>
        {
            "Tracker", "Layout", "AccessibilityLevel", "NotificationType",
            "ScriptHost", "ImageReference",
        };

        // The cloner installs its own helpers on the source's _G to support
        // the integer-id identity map. Skip these on the walk so they don't
        // leak into the destination (and so they don't trigger the "closure
        // not yet supported" warning for the helper function).
        static readonly HashSet<string> ClonerHelperNames = new HashSet<string>
        {
            "__et_cloner_id", "__et_cloner_lookup",
            "__et_cloner_idmap", "__et_cloner_idmap_counter",
            "__et_cloner_tmp",
            "__et_cloner_func_info", "__et_cloner_dump_bytes",
            "__et_cloner_get_upvalue",
            "__et_cloner_load_bytes", "__et_cloner_set_upvalue",
            "__et_cloner_pending_bytes",
        };

        // Names present in the destination's _G at cloner construction time —
        // by definition stdlib + EmoTracker SystemLua additions + anything
        // ScriptManager.Load already installed before forking. Snapshotted
        // dynamically so missing-from-static-list stdlib names (e.g.
        // collectgarbage, warn, utf8) are correctly excluded without us
        // having to maintain a Lua-version-specific allowlist.
        readonly HashSet<string> mDestinationBaselineNames = new HashSet<string>();

        /// <summary>
        /// Constructs a cloner that will deep-copy from <paramref name="source"/>
        /// to <paramref name="destination"/>.
        ///
        /// <paramref name="bridgeIdentityMap"/> maps source-side bridge object
        /// references (NLua userdata wrapping <c>TrackerScriptInterface</c>,
        /// <c>LayoutScriptInterface</c>, etc.) to their destination-side
        /// equivalents. Used by step 3 to remap closure upvalues that capture
        /// these singletons; harmless to pass null (defaults to an empty map)
        /// for callers that don't yet need closure remapping.
        ///
        /// <paramref name="warn"/> is the diagnostic sink; exceptions during
        /// cloning surface here as user-readable strings so the cloner doesn't
        /// silently drop state. Defaults to a no-op.
        /// </summary>
        public LuaStateCloner(
            Lua source,
            Lua destination,
            IReadOnlyDictionary<object, object> bridgeIdentityMap = null,
            Action<string> warn = null,
            IModelResolver destinationResolver = null)
        {
            mSource = source ?? throw new ArgumentNullException(nameof(source));
            mDestination = destination ?? throw new ArgumentNullException(nameof(destination));
            mBridgeIdentityMap = bridgeIdentityMap ?? new Dictionary<object, object>();
            mWarn = warn ?? (_ => { });
            mDestinationResolver = destinationResolver;

            // Snapshot the destination's pristine _G so we know which names
            // are stdlib / pre-injected and shouldn't be cloned from source.
            // Done eagerly (not lazily) so callers that pass a partially-
            // initialized destination still get correct filtering — the
            // typical caller is ScriptManager.Fork(), which constructs the
            // destination's interpreter with its full bootstrap before
            // instantiating the cloner.
            var dstGEnum = ((LuaTable)destination["_G"]).GetEnumerator();
            while (dstGEnum.MoveNext())
            {
                if (dstGEnum.Key is string baselineName)
                    mDestinationBaselineNames.Add(baselineName);
            }
        }

        // Lazily install Lua-side helpers used by the cloner.
        //
        // Source-side:
        //   __et_cloner_id          - Lua tables/functions get a stable int id
        //                             (inserting the entry if not already there).
        //                             Used during the walk for cycle detection.
        //   __et_cloner_lookup      - read-only counterpart used by Resolve so
        //                             external callers don't pollute the map.
        //   __et_cloner_func_info   - returns (what, nups) from debug.getinfo.
        //   __et_cloner_dump_bytes  - returns the function's bytecode as a
        //                             table of byte values, avoiding NLua's
        //                             encoding-roundtrip corruption of binary
        //                             strings.
        //   __et_cloner_get_upvalue - wrap debug.getupvalue.
        //
        // Destination-side:
        //   __et_cloner_load_bytes  - rebuild a binary string from an int
        //                             table and load() it as a function.
        //   __et_cloner_set_upvalue - wrap debug.setupvalue.
        void EnsureIdMapInitialized()
        {
            if (mIdMapInitialized) return;

            mSource.DoString(@"
__et_cloner_idmap = {}
__et_cloner_idmap_counter = 0
function __et_cloner_id(v)
    local existing = __et_cloner_idmap[v]
    if existing then return existing, true end
    __et_cloner_idmap_counter = __et_cloner_idmap_counter + 1
    __et_cloner_idmap[v] = __et_cloner_idmap_counter
    return __et_cloner_idmap_counter, false
end
function __et_cloner_lookup(v)
    return __et_cloner_idmap[v]
end
function __et_cloner_func_info(f)
    local info = debug.getinfo(f, 'Su')
    return info.what, info.nups, info.short_src or '?'
end
function __et_cloner_dump_bytes(f)
    local s = string.dump(f, true)
    local bytes = {}
    for i = 1, #s do
        bytes[i] = s:byte(i)
    end
    return bytes
end
function __et_cloner_get_upvalue(f, i)
    return debug.getupvalue(f, i)
end
");

            mDestination.DoString(@"
function __et_cloner_load_bytes(bytes, name)
    local chunks = {}
    for i = 1, #bytes do
        chunks[i] = string.char(bytes[i])
    end
    local fn, err = load(table.concat(chunks), name, 'b')
    return fn, err
end
function __et_cloner_set_upvalue(f, i, value)
    debug.setupvalue(f, i, value)
end
");

            mIdMapInitialized = true;

            // Seed source._G → destination._G in the identity map before
            // anyone can call CloneValue / Resolve. This must run after
            // mIdMapInitialized = true so GetSourceId can walk the helper.
            SeedSourceGToDestinationG();
        }

        // Pre-populate the identity map so the source interpreter's _G
        // resolves to the destination interpreter's _G AND every baseline
        // C function / stdlib sub-table resolves to the destination's
        // equivalent. Without this seeding:
        //
        //   * Closures that capture _ENV (the default for every top-level
        //     function in Lua 5.2+) would have their _ENV recursively
        //     cloned via CloneTable — and CloneFunction skips C functions,
        //     so the cloned _G ends up with stdlib slots (rawget / pairs /
        //     type / ipairs / ...) all nil.
        //   * Closures that capture stdlib functions as locals (e.g.
        //     `local p = pairs` then `function() return p(t) end`) would
        //     have their upvalue cloned via CloneFunction → which returns
        //     null for C functions → upvalue becomes nil on the destination
        //     → calling that upvalue errors.
        //
        // We seed each baseline name's value via GetSourceId so the Lua-
        // level identity is recorded in the identity map; CloneValue then
        // short-circuits to the destination's same-name entry on encounter.
        // We also recurse one level into stdlib sub-tables (string, table,
        // math, etc.) so e.g. `local fmt = string.format` works.
        bool mGSeeded;
        void SeedSourceGToDestinationG()
        {
            if (mGSeeded) return;
            mGSeeded = true; // set early to break recursion via GetSourceId

            var srcG = mSource["_G"] as LuaTable;
            var dstG = mDestination["_G"] as LuaTable;
            if (srcG == null || dstG == null) return;

            // Seed _G itself.
            try
            {
                var (id, _) = GetSourceId(srcG);
                mIdentityMap[id] = dstG;
            }
            catch { /* defensive */ }

            // Seed every baseline-name entry (stdlib + cloner helpers +
            // bridge globals — the bridge globals map handles itself but
            // having an entry here too is a no-op since it'd be overridden
            // by mBridgeIdentityMap before falling through to mIdentityMap).
            foreach (var name in mDestinationBaselineNames)
            {
                object srcEntry, dstEntry;
                try
                {
                    srcEntry = srcG[name];
                    dstEntry = dstG[name];
                }
                catch { continue; }

                if (srcEntry == null || dstEntry == null) continue;

                // Skip primitives — they don't need identity mapping.
                if (IsPrimitive(srcEntry)) continue;

                try
                {
                    var (id, _) = GetSourceId(srcEntry);
                    mIdentityMap[id] = dstEntry;
                }
                catch { continue; }

                // For stdlib tables (string, table, math, os, io,
                // coroutine, debug, package), recurse one level so
                // captured sub-references like `string.format` resolve
                // through the seed too.
                if (srcEntry is LuaTable srcSub && dstEntry is LuaTable dstSub)
                {
                    SeedSubTableEntries(srcSub, dstSub);
                }
            }
        }

        // Walk one level deep, seeding identity for every named entry
        // whose value exists in both src and dst sub-tables. Used for
        // stdlib namespace tables.
        void SeedSubTableEntries(LuaTable srcTable, LuaTable dstTable)
        {
            var iter = srcTable.GetEnumerator();
            while (iter.MoveNext())
            {
                var key = iter.Key as string;
                if (key == null) continue;

                var srcVal = iter.Value;
                if (srcVal == null) continue;
                if (IsPrimitive(srcVal)) continue;

                object dstVal;
                try { dstVal = dstTable[key]; }
                catch { continue; }
                if (dstVal == null) continue;

                try
                {
                    var (id, _) = GetSourceId(srcVal);
                    mIdentityMap[id] = dstVal;
                }
                catch { /* defensive */ }
            }
        }

        static bool IsPrimitive(object v)
        {
            switch (v)
            {
                case bool _:
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                case string _:
                case char _:
                    return true;
            }
            return false;
        }

        // Returns (id, alreadySeen) for a source-side Lua value.
        (int id, bool alreadySeen) GetSourceId(object value)
        {
            EnsureIdMapInitialized();
            using (var idFunc = (LuaFunction)mSource["__et_cloner_id"])
            {
                var result = idFunc.Call(value);
                int id = Convert.ToInt32(result[0]);
                bool seen = result.Length > 1 && Convert.ToBoolean(result[1]);
                return (id, seen);
            }
        }

        /// <summary>
        /// Resolves a source-side Lua value (typically a <see cref="LuaTable"/>
        /// or <see cref="LuaFunction"/>) to its destination-side clone, using
        /// the Lua-level identity map populated during <see cref="CloneAll"/>.
        ///
        /// <para>
        /// This is the correct lookup path for any caller holding a reference
        /// that came from somewhere other than the cloner's internal walk —
        /// e.g. <c>LuaItem.OnForked</c>'s <c>mItemState</c> /
        /// <c>mOnLeftClick</c> field references, or
        /// <c>ScriptManager.InvokeStandardCallback</c>'s cached
        /// <see cref="LuaFunction"/> handles. NLua creates a fresh
        /// <see cref="LuaTable"/> / <see cref="LuaFunction"/> wrapper on every
        /// access of the same underlying Lua object, so a Dictionary keyed on
        /// the C# wrapper reference would miss every external lookup;
        /// <c>Resolve</c> instead routes through the Lua-side identity helper
        /// (which compares by Lua-level reference equality, the actual
        /// "sameness" we care about).
        /// </para>
        ///
        /// <para>
        /// Returns:
        /// <list type="bullet">
        ///   <item><paramref name="src"/> itself if it's a primitive (Lua
        ///     primitives are immutable and don't need cloning).</item>
        ///   <item>The destination-side counterpart if <paramref name="src"/>
        ///     was cloned during <see cref="CloneAll"/>.</item>
        ///   <item>The bridge-identity-map's destination if the source-side
        ///     reference is a known C# bridge global.</item>
        ///   <item><c>null</c> if the source reference was never seen by the
        ///     cloner (e.g. a stdlib function that wasn't cloned, or a
        ///     reference into a different interpreter).</item>
        /// </list>
        /// </para>
        /// </summary>
        public object Resolve(object src)
        {
            if (src == null) return null;

            // Primitives pass through (immutable in Lua).
            switch (src)
            {
                case bool _:
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                case string _:
                case char _:
                    return src;
            }

            // Bridge-identity remap: if a caller hands in a source-side
            // bridge object, resolve to the destination's bridge directly.
            if (mBridgeIdentityMap.TryGetValue(src, out var bridge))
                return bridge;

            // Tables / functions: Lua-level id lookup. Without the helper
            // (i.e. before CloneAll has been called) there's nothing to
            // resolve — return null.
            if (!mIdMapInitialized)
                return null;

            using (var lookupFunc = (LuaFunction)mSource["__et_cloner_lookup"])
            {
                if (lookupFunc == null) return null;
                var result = lookupFunc.Call(src);
                if (result == null || result.Length == 0 || result[0] == null)
                    return null;

                int id;
                try
                {
                    id = Convert.ToInt32(result[0]);
                }
                catch
                {
                    return null;
                }

                return mIdentityMap.TryGetValue(id, out var dest) ? dest : null;
            }
        }

        /// <summary>
        /// Typed convenience overload of <see cref="Resolve"/> for callers
        /// that hold a <see cref="LuaTable"/> reference and want a typed
        /// result. Returns null if the source wasn't cloned.
        /// </summary>
        public LuaTable Resolve(LuaTable src) => Resolve((object)src) as LuaTable;

        /// <summary>
        /// Typed convenience overload of <see cref="Resolve"/> for callers
        /// that hold a <see cref="LuaFunction"/> reference and want a typed
        /// result. Returns null if the source wasn't cloned (e.g. when
        /// closure cloning is the next step's responsibility).
        /// </summary>
        public LuaFunction Resolve(LuaFunction src) => Resolve((object)src) as LuaFunction;

        /// <summary>
        /// Walks the source's <c>_G</c> table and clones every non-baseline,
        /// non-bridge entry into the destination's globals. After this
        /// returns, <see cref="Resolve"/> can be used to map
        /// source-side Lua values (tables and — once step 3 lands —
        /// functions) to their destination clones, which is how
        /// <c>LuaItem.OnForked</c> and the holder-aware standard-callback
        /// dispatch redirect their cached references away from the source's
        /// orphaned interpreter.
        /// </summary>
        public void CloneAll()
        {
            // Ensure the identity map is initialized + source._G → dest._G
            // is seeded before iteration. EnsureIdMapInitialized installs
            // the helper functions and seeds source._G → destination._G
            // so closures that capture _ENV upvalues resolve to the
            // destination's pristine _G (with full stdlib intact).
            EnsureIdMapInitialized();

            // Iterate every key in source's globals via the LuaTable enumerator
            // (the same shape LuaItem.Save uses for its own table walks).
            // NLua's LuaTableEnumerator implements IDictionaryEnumerator but
            // not IDisposable, so we don't wrap in a 'using'.
            var gEnum = ((LuaTable)mSource["_G"]).GetEnumerator();
            while (gEnum.MoveNext())
            {
                var key = gEnum.Key as string;
                if (key == null) continue;

                if (mDestinationBaselineNames.Contains(key)) continue;
                if (BridgeGlobalNames.Contains(key)) continue;
                if (ClonerHelperNames.Contains(key)) continue;

                object cloned;
                try
                {
                    cloned = CloneValue(gEnum.Value);
                }
                catch (Exception ex)
                {
                    mWarn(string.Format("LuaStateCloner: failed to clone global '{0}': {1}", key, ex.Message));
                    continue;
                }

                if (cloned != null || gEnum.Value == null)
                {
                    mDestination[key] = cloned;
                }
            }
        }

        /// <summary>
        /// Clones a single Lua value from the source interpreter into the
        /// destination's universe. Primitives are returned unchanged
        /// (immutable in Lua); tables are recursively cloned with cycle
        /// preservation; closures and userdata fall through to the
        /// type-specific helpers (currently stubbed in step 2 for closures).
        /// </summary>
        public object CloneValue(object sourceValue)
        {
            if (sourceValue == null) return null;

            // Primitives: pass through. Lua's value types are all immutable.
            switch (sourceValue)
            {
                case bool _:
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                case string _:
                case char _:
                    return sourceValue;
            }

            // Bridge-identity remap: closures captured the source's Tracker /
            // Layout / etc. — substitute the destination's instances. Keyed
            // by default object equality on the bridge wrapper.
            if (mBridgeIdentityMap.TryGetValue(sourceValue, out var bridgeValue))
                return bridgeValue;

            // LuaModelRef remap (opt-in proxy path; see ScriptManager.WrapAsLuaProxy).
            if (sourceValue is LuaModelRef srcModelRef)
            {
                if (mDestinationResolver != null)
                    return srcModelRef.WithResolver(mDestinationResolver);
                mWarn("LuaStateCloner: cloning a LuaModelRef without a destinationResolver; fork proxies will resolve into the source state.");
                return srcModelRef;
            }

            // ModelTypeBase auto-remap (defensive — usually the bridge map
            // above already handles known catalog entries; this catches any
            // ModelTypeBase-derived captures that weren't seeded into the
            // bridge map for whatever reason).
            if (sourceValue is EmoTracker.Core.DataModel.ModelTypeBase srcModel)
            {
                if (mDestinationResolver != null && srcModel.DefinitionId != Guid.Empty)
                {
                    var destModel = mDestinationResolver.Resolve<EmoTracker.Core.DataModel.ModelTypeBase>(srcModel.DefinitionId);
                    if (destModel != null)
                        return destModel;
                    mWarn(string.Format(
                        "LuaStateCloner: model {0} (DefinitionId {1}) not present in destination resolver; passing source reference through.",
                        srcModel.GetType().FullName, srcModel.DefinitionId));
                }
                return sourceValue;
            }

            // Tables: recursive clone with cycle preservation. The id map
            // assigns a stable integer per Lua-level reference; if we've
            // already cloned that source table, return the same destination.
            if (sourceValue is LuaTable srcTable)
            {
                var (id, seen) = GetSourceId(srcTable);
                if (seen && mIdentityMap.TryGetValue(id, out var cachedTable))
                    return cachedTable;
                return CloneTable(srcTable, id);
            }

            // Functions: same id-based cycle handling as tables. Pure Lua
            // closures get bytecode-dump-and-reload + per-upvalue clone;
            // C functions are skipped with a warning (step 3 deliberately
            // doesn't try to resolve them by-name yet — most pack scripts
            // don't store standalone C function references in user globals).
            if (sourceValue is LuaFunction srcFunc)
            {
                var (id, seen) = GetSourceId(srcFunc);
                if (seen && mIdentityMap.TryGetValue(id, out var cachedFunc))
                    return cachedFunc;
                return CloneFunction(srcFunc, id);
            }

            // Userdata + miscellaneous: pass through by reference. NLua
            // surfaces C#-managed objects as the underlying CLR reference
            // (not an opaque userdata wrapper), so this branch typically
            // lands on bridge-typed objects we should have caught above.
            // Anything that falls through is shared-by-reference between
            // source and destination — acceptable for stateless / content-
            // immutable shared objects, risky otherwise.
            //
            // Known-safe pass-through types are silenced:
            //   - ImageReference (and subclasses): pack scripts construct
            //     these via ImageReference:FromPackRelativePath and store
            //     them in user tables. Their externally-observable state
            //     (URI, Filter, layered/filter compositions) is set at
            //     construction and never mutated afterwards. The base's
            //     ResolvedImage / SourceWidth / SourceHeight cache slots
            //     ARE mutable, but they're framework-populated and
            //     idempotent — sharing the cached resolution between
            //     fork and source actually reduces memory pressure.
            //   - System.Enum: enum values are immutable primitives that
            //     happen not to match the boxed-primitive switch above.
            //     Pack scripts pull AccessibilityLevel.* etc. into tables
            //     constantly; pass through silently.
            // Anything else is genuinely unexpected and gets warned.
            if (sourceValue is EmoTracker.Data.Media.ImageReference) return sourceValue;
            if (sourceValue is System.Enum) return sourceValue;

            mWarn(string.Format("LuaStateCloner: passing through reference of type {0}; unexpected for non-bridge globals.",
                sourceValue.GetType().FullName));
            return sourceValue;
        }

        LuaTable CloneTable(LuaTable srcTable, int srcId)
        {
            // Allocate a fresh empty table on the destination. NLua's most
            // direct way is via DoString returning a table reference — there
            // is no Lua.NewTable() that returns a free-standing handle in
            // this version. We allocate via a temporary global, copy the
            // reference, and clear the temporary slot.
            mDestination.DoString("__et_cloner_tmp = {}");
            LuaTable destTable = (LuaTable)mDestination["__et_cloner_tmp"];
            mDestination["__et_cloner_tmp"] = null;

            // Record the (source -> destination) mapping BEFORE recursing so
            // cycles terminate: a recursive clone of a value that points
            // back at srcTable will see destTable already in the map and
            // return it without re-entering CloneTable.
            mIdentityMap[srcId] = destTable;

            var iter = srcTable.GetEnumerator();
            while (iter.MoveNext())
            {
                var key = iter.Key;
                var clonedKey = CloneValue(key);
                var clonedValue = CloneValue(iter.Value);
                if (clonedKey != null)
                {
                    destTable[clonedKey] = clonedValue;
                }
            }

            // Carry the metatable across, if present. setmetatable on the
            // destination would return Lua-side, but we go through the API:
            // getmetatable on the source, clone, setmetatable on the
            // destination via Lua's setmetatable.
            using (var sourceGetMetatable = (LuaFunction)mSource["getmetatable"])
            {
                if (sourceGetMetatable != null)
                {
                    var metaResult = sourceGetMetatable.Call(srcTable);
                    if (metaResult != null && metaResult.Length > 0 && metaResult[0] is LuaTable srcMeta)
                    {
                        var clonedMeta = CloneValue(srcMeta) as LuaTable;
                        if (clonedMeta != null)
                        {
                            using (var setMt = (LuaFunction)mDestination["setmetatable"])
                            {
                                setMt?.Call(destTable, clonedMeta);
                            }
                        }
                    }
                }
            }

            return destTable;
        }

        // -------- Closure cloning ----------------------------------------

        LuaFunction CloneFunction(LuaFunction srcFunc, int srcId)
        {
            // debug.getinfo gives us "what" (Lua / C / main) and the upvalue
            // count. C functions are skipped (step 3 doesn't try to resolve
            // by name); pure Lua functions go through the dump-load-rewire
            // pipeline.
            string what;
            int nups;
            string sourceName;
            using (var infoFunc = (LuaFunction)mSource["__et_cloner_func_info"])
            {
                var info = infoFunc.Call(srcFunc);
                if (info == null || info.Length < 2 || info[0] == null)
                {
                    mWarn("LuaStateCloner: debug.getinfo returned no result; skipping function.");
                    return null;
                }
                what = info[0] as string ?? "?";
                nups = Convert.ToInt32(info[1] ?? 0);
                sourceName = (info.Length >= 3 ? info[2] as string : null) ?? "clone";
            }

            if (what != "Lua" && what != "main")
            {
                // C function — typically a stdlib reference. Pack scripts
                // that store standalone C function references in globals
                // are unusual; most C-function captures happen as upvalues
                // in Lua closures, where they're handled by the upvalue-
                // copy below (NLua hands them through as the same C function
                // reference, which the destination's stdlib already provides).
                mWarn(string.Format("LuaStateCloner: skipping C function '{0}' (resolution-by-name is not yet implemented).", sourceName));
                return null;
            }

            // Dump bytecode as a table of byte values to avoid NLua's
            // default UTF-8 round-trip corrupting binary bytes.
            LuaTable bytesTable;
            using (var dumpFunc = (LuaFunction)mSource["__et_cloner_dump_bytes"])
            {
                var dumpResult = dumpFunc.Call(srcFunc);
                if (dumpResult == null || dumpResult.Length == 0 || !(dumpResult[0] is LuaTable bt))
                {
                    mWarn(string.Format("LuaStateCloner: string.dump returned no bytes for '{0}'.", sourceName));
                    return null;
                }
                bytesTable = bt;
            }

            // Push the bytes to the destination via a temporary global, then
            // load them. Same temporary-global trick as CloneTable uses for
            // table allocation — NLua doesn't expose a direct cross-
            // interpreter table-handle transfer, so we reconstruct on the
            // destination side.
            //
            // Walk the source's bytes table and rebuild on the destination.
            mDestination.DoString("__et_cloner_pending_bytes = {}");
            var destBytesTable = (LuaTable)mDestination["__et_cloner_pending_bytes"];
            int byteCount = 0;
            var bytesIter = bytesTable.GetEnumerator();
            while (bytesIter.MoveNext())
            {
                byteCount++;
                destBytesTable[bytesIter.Key] = bytesIter.Value;
            }

            // Load the bytecode on the destination.
            LuaFunction destFunc;
            using (var loadFunc = (LuaFunction)mDestination["__et_cloner_load_bytes"])
            {
                var loadResult = loadFunc.Call(destBytesTable, "et_clone_" + srcId + "_" + sourceName);
                if (loadResult == null || loadResult.Length == 0 || !(loadResult[0] is LuaFunction df))
                {
                    string err = (loadResult != null && loadResult.Length >= 2) ? (loadResult[1] as string) : "unknown error";
                    mWarn(string.Format("LuaStateCloner: load() failed for '{0}': {1}", sourceName, err ?? "<no error>"));
                    mDestination["__et_cloner_pending_bytes"] = null;
                    return null;
                }
                destFunc = df;
            }
            mDestination["__et_cloner_pending_bytes"] = null;

            // Record the (source -> destination) id mapping BEFORE recursing
            // into upvalues so a closure that captures itself (recursive
            // Lua function) terminates: when its own upvalue is cloned, the
            // recursion sees this id already mapped.
            mIdentityMap[srcId] = destFunc;

            // Copy upvalues, cloning each value. Upvalues are 1-indexed in
            // Lua; debug.getupvalue returns (name, value) and debug.setupvalue
            // takes (function, index, value).
            //
            // Special-case _ENV: when an upvalue's name is "_ENV", substitute
            // the destination's _G directly. This bypasses CloneValue's
            // seed-based remap (which has been observed to fail across NLua
            // wrapper-identity boundaries), and also handles closures that
            // captured _ENV by reference but where the captured value's
            // C# wrapper identity differs from any wrapper we've seeded.
            // The freshly load()'d destination function already has _ENV set
            // to dest._G by default — we re-set it explicitly here to
            // overwrite the no-op default that we'd otherwise produce by
            // running CloneValue on the source's _G.
            for (int i = 1; i <= nups; i++)
            {
                string upvalueName;
                object upvalueValue;
                using (var getUpvalFunc = (LuaFunction)mSource["__et_cloner_get_upvalue"])
                {
                    var upResult = getUpvalFunc.Call(srcFunc, i);
                    if (upResult == null || upResult.Length < 2)
                    {
                        // Upvalue at this slot doesn't exist or is otherwise
                        // not readable — skip. Lua leaves it nil-valued on
                        // the destination, which matches a fresh closure's
                        // initial state.
                        continue;
                    }
                    upvalueName = upResult[0] as string;
                    upvalueValue = upResult[1];
                }

                object clonedUpvalue;
                if (upvalueName == "_ENV")
                {
                    // Use destination's _G directly. Don't CloneValue —
                    // its seed-based path produces dest._G but only when
                    // CloneValue resolves the source._G's wrapper identity
                    // through GetSourceId, which has shown empirically to
                    // not always hit. Going direct is safer.
                    clonedUpvalue = mDestination["_G"];
                }
                else
                {
                    try
                    {
                        clonedUpvalue = CloneValue(upvalueValue);
                    }
                    catch (Exception ex)
                    {
                        mWarn(string.Format("LuaStateCloner: failed to clone upvalue {0} of '{1}': {2}", i, sourceName, ex.Message));
                        continue;
                    }
                }

                using (var setUpvalFunc = (LuaFunction)mDestination["__et_cloner_set_upvalue"])
                {
                    setUpvalFunc.Call(destFunc, i, clonedUpvalue);
                }
            }

            return destFunc;
        }
    }
}
