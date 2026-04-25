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
            Action<string> warn = null)
        {
            mSource = source ?? throw new ArgumentNullException(nameof(source));
            mDestination = destination ?? throw new ArgumentNullException(nameof(destination));
            mBridgeIdentityMap = bridgeIdentityMap ?? new Dictionary<object, object>();
            mWarn = warn ?? (_ => { });

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

        // Lazily install Lua-side helpers that assign a stable integer id
        // to each table / function we encounter on the source. Lua tables
        // compare by reference natively (same table → same key → same id).
        // <c>__et_cloner_id</c> is the assigning lookup (inserts if missing);
        // <c>__et_cloner_lookup</c> is the read-only counterpart used by
        // <see cref="Resolve"/> so external callers don't pollute the map
        // with values the cloner never actually cloned.
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
");
            mIdMapInitialized = true;
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

            // Tables: recursive clone with cycle preservation. The id map
            // assigns a stable integer per Lua-level reference; if we've
            // already cloned that source table, return the same destination.
            if (sourceValue is LuaTable srcTable)
            {
                var (id, seen) = GetSourceId(srcTable);
                if (seen && mIdentityMap.TryGetValue(id, out var cachedDest))
                    return cachedDest;
                return CloneTable(srcTable, id);
            }

            // Functions: deferred to step 3. Skip with a warning so packs
            // that don't store closures in globals fork cleanly today; packs
            // that do will see those closures dropped on fork until step 3
            // lands.
            if (sourceValue is LuaFunction)
            {
                mWarn("LuaStateCloner: encountered a LuaFunction; closure cloning is not yet implemented (step 3) — skipping.");
                return null;
            }

            // Userdata + miscellaneous: pass through by reference. NLua
            // surfaces C#-managed objects as the underlying CLR reference
            // (not an opaque userdata wrapper), so this branch typically
            // lands on bridge-typed objects we should have caught above.
            // Anything that falls through is shared-by-reference between
            // source and destination — acceptable for stateless shared
            // objects (e.g. enums-as-objects), risky otherwise. The warning
            // surfaces unexpected cases.
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
    }
}
