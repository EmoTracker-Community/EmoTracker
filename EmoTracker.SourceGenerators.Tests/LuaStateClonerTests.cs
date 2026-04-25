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

        [Fact]
        public void Closures_AreSkippedWithWarning_Step2()
        {
            // Step 2 doesn't yet handle closures — they're skipped with a
            // logged warning. Step 3 will add the upvalue-aware clone.
            var (src, dst, warnings, cloner) = NewPair();

            src.DoString(@"
                my_closure = function() return 1 end
                also_table = { x = 1 }
            ");

            cloner.CloneAll();

            // Closure not cloned.
            Assert.Null(dst["my_closure"]);
            // Table beside it still cloned.
            Assert.NotNull(dst["also_table"]);

            // Warning emitted for the skipped closure.
            Assert.Contains(warnings, w => w.Contains("LuaFunction"));

            src.Close();
            dst.Close();
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
    }
}
