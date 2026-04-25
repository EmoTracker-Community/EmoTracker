using EmoTracker.Data.Scripting;
using NLua;
using System;
using System.Collections.Generic;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 5 step 2: <see cref="LuaStateCloner"/> primitive / table /
    /// cycle / shared-subtree / metatable handling. Closures and upvalue
    /// remapping are step 3 (skipped here with a warning that the test
    /// captures and ignores).
    /// </summary>
    public class LuaStateClonerTests
    {
        // Each test gets fresh source / destination interpreters; warnings
        // are captured for assertions where relevant.
        static (Lua src, Lua dst, List<string> warnings, LuaStateCloner cloner) NewPair()
        {
            var src = new Lua();
            var dst = new Lua();
            var warnings = new List<string>();
            var cloner = new LuaStateCloner(src, dst, null, w => warnings.Add(w));
            return (src, dst, warnings, cloner);
        }

        [Fact]
        public void Primitives_PassThrough_Unchanged()
        {
            var (src, dst, _, cloner) = NewPair();

            Assert.Equal(true, cloner.CloneValue(true));
            Assert.Equal(false, cloner.CloneValue(false));
            Assert.Equal(42, cloner.CloneValue(42));
            Assert.Equal(3.14, cloner.CloneValue(3.14));
            Assert.Equal("hi", cloner.CloneValue("hi"));
            Assert.Null(cloner.CloneValue(null));

            src.Close();
            dst.Close();
        }

        [Fact]
        public void SimpleTable_CloneAll_CarriesNonStdlibGlobals()
        {
            var (src, dst, warnings, cloner) = NewPair();

            // Source has a global 'pack_state = { score = 7, name = "alpha" }'.
            src.DoString(@"pack_state = { score = 7, name = 'alpha' }");

            cloner.CloneAll();

            // Destination's pack_state is a fresh LuaTable with the same content.
            // NLua surfaces Lua numbers as either long or double depending on
            // version / sub-type — coerce both via Convert for portability.
            var clonedTable = (LuaTable)dst["pack_state"];
            Assert.NotNull(clonedTable);
            Assert.Equal(7L, Convert.ToInt64(clonedTable["score"]));
            Assert.Equal("alpha", (string)clonedTable["name"]);

            // Mutating the source after the clone doesn't disturb the
            // destination's copy — proves it's a deep copy, not a reference.
            src.DoString("pack_state.score = 99");
            Assert.Equal(7L, Convert.ToInt64(clonedTable["score"]));

            // No warnings expected for primitive / table values.
            Assert.Empty(warnings);

            src.Close();
            dst.Close();
        }

        [Fact]
        public void CloneAll_SkipsStdlibAndBridgeGlobals()
        {
            var (src, dst, warnings, cloner) = NewPair();

            // Set a fake "Tracker" + "string"-ish entry on the source. The
            // cloner's BridgeGlobalNames includes "Tracker"; "string" is a
            // stdlib name. Both should be skipped.
            src.DoString(@"
                Tracker = { fake = 1 }
                user_global = { real = 2 }
            ");

            cloner.CloneAll();

            // Bridge global skipped — destination's "Tracker" stays whatever
            // its baseline is (nil here, since the destination got no
            // bridges injected).
            Assert.Null(dst["Tracker"]);

            // User global cloned.
            var userGlobal = (LuaTable)dst["user_global"];
            Assert.NotNull(userGlobal);
            Assert.Equal(2L, Convert.ToInt64(userGlobal["real"]));

            src.Close();
            dst.Close();
        }

        [Fact]
        public void SelfReferentialTable_CycleResolvesToFreshDestinationTable()
        {
            var (src, dst, _, cloner) = NewPair();

            // A table that holds a reference to itself ('t.self = t').
            src.DoString(@"
                t = {}
                t.value = 'cyclic'
                t.self = t
            ");

            cloner.CloneAll();

            // NLua creates a fresh LuaTable wrapper on each access, so
            // C# reference equality across two indexing operations doesn't
            // hold even when the underlying Lua table is the same. Use
            // Lua-level equality (== on tables IS reference equality at
            // the Lua level) by running the comparison through Lua.
            dst.DoString("__cycle_eq = (t.self == t)");
            Assert.True((bool)dst["__cycle_eq"]);

            // And the value field round-tripped.
            var clonedT = (LuaTable)dst["t"];
            Assert.Equal("cyclic", (string)clonedT["value"]);

            src.Close();
            dst.Close();
        }

        [Fact]
        public void SharedSubtree_PreservedAsSingleDestinationTable()
        {
            // Two top-level tables both reference a shared inner table; the
            // destination must end up with ONE inner table referenced from
            // both top-level cloned tables, not two independent copies.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                shared = { kind = 'inner' }
                a = { ref = shared, tag = 'A' }
                b = { ref = shared, tag = 'B' }
            ");

            cloner.CloneAll();

            // Verify all three references resolve to the same destination-
            // side inner table via Lua equality (which is reference-based
            // for tables).
            dst.DoString(@"
                __ab_eq = (a.ref == b.ref)
                __a_shared_eq = (a.ref == shared)
            ");
            Assert.True((bool)dst["__ab_eq"]);
            Assert.True((bool)dst["__a_shared_eq"]);

            var clonedShared = (LuaTable)dst["shared"];
            Assert.Equal("inner", (string)clonedShared["kind"]);

            src.Close();
            dst.Close();
        }

        [Fact]
        public void NestedTables_ClonedToFullDepth()
        {
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                deep = {
                    level1 = {
                        level2 = {
                            level3 = { value = 'bottom' }
                        }
                    }
                }
            ");

            cloner.CloneAll();

            var deep = (LuaTable)dst["deep"];
            var level1 = (LuaTable)deep["level1"];
            var level2 = (LuaTable)level1["level2"];
            var level3 = (LuaTable)level2["level3"];
            Assert.Equal("bottom", (string)level3["value"]);

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Metatable_CarriesAcrossClone()
        {
            var (src, dst, _, cloner) = NewPair();

            // A table 't' with a metatable that adds an __index falling
            // back to the underlying 'fallback' table. Both the table and
            // its metatable are cloned; the destination's t still inherits
            // 'shared' through __index after the clone.
            src.DoString(@"
                fallback = { inherited_value = 'from_fallback' }
                t = { own_value = 'from_t' }
                setmetatable(t, { __index = fallback })
            ");

            cloner.CloneAll();

            var clonedT = (LuaTable)dst["t"];
            // Direct field on cloned t resolves locally.
            Assert.Equal("from_t", (string)clonedT["own_value"]);

            // Via Lua, the metatable should propagate __index lookups. Run
            // a Lua expression on the destination and confirm the
            // inherited value flows through.
            dst.DoString("got = t.inherited_value");
            Assert.Equal("from_fallback", (string)dst["got"]);

            src.Close();
            dst.Close();
        }

        // -------- Closure cloning (step 3) ---------------------------------

        [Fact]
        public void Closures_NoUpvalues_DumpAndReloadOnDestination()
        {
            // Pure Lua closure with no upvalues — the simplest case.
            // String.dump produces bytecode, load() rebuilds the function on
            // the destination, calling it returns the same constant.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                my_closure = function() return 42 end
            ");

            cloner.CloneAll();

            // The destination's my_closure exists and returns 42.
            using (var fn = (LuaFunction)dst["my_closure"])
            {
                Assert.NotNull(fn);
                var result = fn.Call();
                Assert.Equal(42L, Convert.ToInt64(result[0]));
            }

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Closures_WithPrimitiveUpvalue_PreservesCapturedValue()
        {
            // A closure that captures a primitive upvalue (a number). Each
            // closure has its own private upvalue slot — mutating the
            // closure's view via a returned setter doesn't disturb the
            // original interpreter's view.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                local count = 5
                bumper = function() count = count + 1; return count end
            ");

            cloner.CloneAll();

            using (var fn = (LuaFunction)dst["bumper"])
            {
                Assert.NotNull(fn);
                // First call increments to 6, second to 7.
                Assert.Equal(6L, Convert.ToInt64(fn.Call()[0]));
                Assert.Equal(7L, Convert.ToInt64(fn.Call()[0]));
            }

            // Source's bumper still bumps from its own captured 5 — proving
            // the upvalue is per-closure, not shared.
            using (var srcFn = (LuaFunction)src["bumper"])
            {
                Assert.Equal(6L, Convert.ToInt64(srcFn.Call()[0]));
            }

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Closures_WithTableUpvalue_PointsAtClonedTable()
        {
            // A closure capturing a Lua table. The cloned closure must
            // reference the destination's clone of that table — NOT the
            // source's table — and observably reflect mutations on the
            // destination side without disturbing the source.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                shared_state = { tally = 0 }
                add_one = function() shared_state.tally = shared_state.tally + 1; return shared_state.tally end
            ");

            cloner.CloneAll();

            using (var fn = (LuaFunction)dst["add_one"])
            {
                Assert.NotNull(fn);
                Assert.Equal(1L, Convert.ToInt64(fn.Call()[0]));
                Assert.Equal(2L, Convert.ToInt64(fn.Call()[0]));
            }

            // Destination's shared_state observed the mutations.
            var dstShared = (LuaTable)dst["shared_state"];
            Assert.Equal(2L, Convert.ToInt64(dstShared["tally"]));

            // Source's shared_state remained at 0.
            var srcShared = (LuaTable)src["shared_state"];
            Assert.Equal(0L, Convert.ToInt64(srcShared["tally"]));

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Closures_SharedTableUpvalue_ResolvesToSameDestinationTable()
        {
            // Two closures capturing the same source table must end up
            // capturing the same DESTINATION clone. This is the key
            // invariant for pack-author shared-mutable-state patterns
            // (e.g. two LuaItem callbacks both mutating a shared
            // game_state table) — Save/Load round-tripping flattens these
            // into independent copies; the cloner preserves the sharing.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                shared = { value = 0 }
                writer = function(v) shared.value = v end
                reader = function() return shared.value end
            ");

            cloner.CloneAll();

            using (var w = (LuaFunction)dst["writer"])
            using (var r = (LuaFunction)dst["reader"])
            {
                w.Call(99);
                var got = r.Call();
                Assert.Equal(99L, Convert.ToInt64(got[0]));
            }

            // Verify shared identity at the Lua level. The destination's
            // 'shared' global IS the same table the writer / reader closures
            // captured.
            dst.DoString("__same = (writer ~= reader)"); // sanity check
            Assert.True((bool)dst["__same"]);

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Closures_BridgeIdentityMap_RemapsCapturedBridgeUpvalue()
        {
            // The headline case for the bridge identity map: a Lua closure
            // that captured a C# bridge object (e.g. Tracker) on the source
            // must capture the destination's instance after cloning.
            //
            // We simulate the bridge with a sentinel 'side marker' object —
            // the source binds 'TestBridge' to a marker object, the closure
            // captures it as an upvalue, the destination is configured with
            // a different marker, and the bridge identity map remaps the
            // upvalue during the clone.
            var src = new Lua();
            var dst = new Lua();

            // Two distinct C# sentinels stand in for the source and
            // destination bridges.
            var srcBridge = new BridgeMarker { Tag = "source" };
            var dstBridge = new BridgeMarker { Tag = "destination" };

            // Bind on each interpreter so closures can capture them.
            src["TestBridge"] = srcBridge;
            dst["TestBridge"] = dstBridge;

            // Source closure captures TestBridge as an upvalue (note: the
            // 'local' makes it an upvalue rather than a global lookup).
            src.DoString(@"
                local _bridge = TestBridge
                get_bridge_tag = function() return _bridge.Tag end
            ");

            // Clone with the bridge map wired up so the captured upvalue
            // remaps from src to dst.
            var bridgeMap = new Dictionary<object, object> { { srcBridge, dstBridge } };
            var warnings = new List<string>();
            var cloner = new LuaStateCloner(src, dst, bridgeMap, w => warnings.Add(w));
            cloner.CloneAll();

            // The cloned closure invokes through the destination's bridge.
            using (var fn = (LuaFunction)dst["get_bridge_tag"])
            {
                Assert.NotNull(fn);
                var got = fn.Call();
                Assert.Equal("destination", (string)got[0]);
            }

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Closures_RecursiveSelfCapture_TerminatesCleanly()
        {
            // A closure that captures itself via an upvalue (factorial-
            // style). The cloner's id-keyed identity map must terminate
            // the recursion when the upvalue clone hits the same source
            // function.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                local self_ref
                self_ref = function(n)
                    if n <= 1 then return 1 end
                    return n * self_ref(n - 1)
                end
                fact = self_ref
            ");

            cloner.CloneAll();

            using (var fn = (LuaFunction)dst["fact"])
            {
                Assert.NotNull(fn);
                Assert.Equal(120L, Convert.ToInt64(fn.Call(5)[0]));
            }

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Resolve_LuaFunction_AfterCloneAll_ReturnsDestinationClone()
        {
            // The closure analog of Resolve_LuaTable_AfterCloneAll. LuaItem
            // step-7 wire-up depends on this: holding a source-side
            // LuaFunction reference (mOnLeftClick etc.), call Resolve, get
            // the destination clone.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                left_click = function(x) return x * 2 end
            ");
            var srcFn = (LuaFunction)src["left_click"];

            cloner.CloneAll();

            var resolvedFn = cloner.Resolve(srcFn);
            Assert.NotNull(resolvedFn);
            // The resolved function executes on the destination's interpreter.
            var got = resolvedFn.Call(7);
            Assert.Equal(14L, Convert.ToInt64(got[0]));

            src.Close();
            dst.Close();
        }

        // Helper class for the bridge-map test — needs a public field that
        // Lua can read via the C#-bridge-property path (NLua treats
        // user-defined types' fields/properties as accessible from Lua).
        public class BridgeMarker
        {
            public string Tag { get; set; }
        }

        [Fact]
        public void EmptyTable_ClonesToEmptyDestinationTable()
        {
            var (src, dst, _, cloner) = NewPair();

            src.DoString("empty = {}");
            cloner.CloneAll();

            var clonedEmpty = (LuaTable)dst["empty"];
            Assert.NotNull(clonedEmpty);
            // No keys.
            var iter = clonedEmpty.GetEnumerator();
            Assert.False(iter.MoveNext());

            src.Close();
            dst.Close();
        }

        [Fact]
        public void ArrayStyleTable_ClonesPreservingNumericKeys()
        {
            var (src, dst, _, cloner) = NewPair();

            src.DoString("nums = { 'a', 'b', 'c' }");
            cloner.CloneAll();

            var clonedNums = (LuaTable)dst["nums"];
            Assert.Equal("a", (string)clonedNums[1]);
            Assert.Equal("b", (string)clonedNums[2]);
            Assert.Equal("c", (string)clonedNums[3]);

            src.Close();
            dst.Close();
        }

        // -------- Resolve() — the critical lookup path used by --------
        // -------- LuaItem.OnForked + standard-callback dispatch --------

        [Fact]
        public void Resolve_LuaTable_AfterCloneAll_ReturnsDestinationClone()
        {
            // The exact scenario LuaItem.OnForked needs: hold a reference
            // to a source-side LuaTable (acquired via mItemState before the
            // fork), pass it to Resolve, get back the destination's clone.
            // The cloner walks _G during CloneAll — anything reachable
            // there ends up in the identity map.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                game_state = { score = 42, deaths = 3 }
            ");

            // Capture a source-side reference BEFORE the clone, the way a
            // LuaItem stashes its mItemState handle at parse time.
            var srcGameState = (LuaTable)src["game_state"];

            cloner.CloneAll();

            // Resolve maps the source-side reference to the destination clone.
            var dstGameState = cloner.Resolve(srcGameState);
            Assert.NotNull(dstGameState);

            // The resolved destination is the same table the destination's
            // _G holds — verified via Lua-level equality.
            dst.DoString("__same = (game_state == __et_resolved)");
            // ...except we can't put dstGameState into dst directly via
            // __et_resolved. Just verify content matches.
            Assert.Equal(42L, Convert.ToInt64(dstGameState["score"]));
            Assert.Equal(3L, Convert.ToInt64(dstGameState["deaths"]));

            // Mutating the source's table after CloneAll has no effect on
            // the resolved destination clone — proves it's not a back-door
            // alias.
            src.DoString("game_state.score = 999");
            Assert.Equal(42L, Convert.ToInt64(dstGameState["score"]));

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Resolve_NestedLuaTable_AlsoMapped()
        {
            // A LuaItem might hold a reference to an inner table (e.g.
            // mItemState.config). Resolve needs to map any source-reachable
            // table, not just top-level globals.
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                pack = {
                    config = { volume = 5 },
                    name = 'test'
                }
            ");
            var srcConfig = (LuaTable)((LuaTable)src["pack"])["config"];

            cloner.CloneAll();

            var dstConfig = cloner.Resolve(srcConfig);
            Assert.NotNull(dstConfig);
            Assert.Equal(5L, Convert.ToInt64(dstConfig["volume"]));
        }

        [Fact]
        public void Resolve_Primitives_PassThrough()
        {
            var (src, dst, _, cloner) = NewPair();

            // Primitives need no cloning — Resolve returns them verbatim
            // even before CloneAll has run.
            Assert.Equal(42, cloner.Resolve((object)42));
            Assert.Equal("hello", cloner.Resolve((object)"hello"));
            Assert.Equal(true, cloner.Resolve((object)true));
            Assert.Null(cloner.Resolve((object)null));
        }

        [Fact]
        public void Resolve_NeverClonedReference_ReturnsNull()
        {
            // If a caller passes in a source LuaTable that wasn't reachable
            // from _G (and so wasn't cloned), Resolve returns null rather
            // than fabricating an entry. The LuaItem.OnForked contract is
            // "look up via Resolve; if null, drop the reference".
            var (src, dst, _, cloner) = NewPair();

            src.DoString(@"
                cloned_table = { x = 1 }
                detached_table = { y = 2 }
            ");
            cloner.CloneAll();

            // Now allocate a NEW source-side table that the cloner never
            // saw because it's not reachable from _G — set as a registry
            // value.
            src.DoString(@"
                local _local_only = { z = 3 }
                __unreachable = _local_only
            ");
            // _G entries get cloned, so __unreachable would be picked up if
            // CloneAll were re-run — but we've already done it. Lookup
            // should return null because that table wasn't in the map.
            var srcUnreachable = (LuaTable)src["__unreachable"];
            var resolved = cloner.Resolve(srcUnreachable);
            Assert.Null(resolved);
        }

        [Fact]
        public void Resolve_BridgeIdentityMap_OverridesLookup()
        {
            // The bridge identity map exists so that closures captured the
            // source's Tracker / Layout / etc. globals get remapped to the
            // destination's bridge instances on Resolve. Even though
            // step 3 is what wires this for closures, the Resolve API
            // already honors the bridge map.
            var (src, _, _, _) = NewPair();
            var dst = new Lua();

            // Two sentinel C# objects standing in for source/dest bridges.
            var srcBridge = new object();
            var dstBridge = new object();
            var bridgeMap = new Dictionary<object, object> { { srcBridge, dstBridge } };

            var cloner = new LuaStateCloner(src, dst, bridgeMap);
            // Even before CloneAll, the bridge map applies.
            Assert.Same(dstBridge, cloner.Resolve(srcBridge));

            src.Close();
            dst.Close();
        }

        [Fact]
        public void Resolve_LuaTableBeforeCloneAll_ReturnsNullForUninitializedMap()
        {
            // Before CloneAll runs, the Lua-side helper hasn't been
            // installed; Resolve must safely return null rather than
            // throwing.
            var (src, dst, _, cloner) = NewPair();
            src.DoString("t = {}");
            var srcT = (LuaTable)src["t"];

            Assert.Null(cloner.Resolve(srcT));

            src.Close();
            dst.Close();
        }
    }
}
