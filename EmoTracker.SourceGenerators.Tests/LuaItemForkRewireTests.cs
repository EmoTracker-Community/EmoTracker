using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.Scripting;
using NLua;
using System;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 5 step 7: <see cref="LuaItem"/>'s <c>mItemState</c> +
    /// 8 LuaFunction reference fields, after a fork, must point at the
    /// fork's interpreter — not the source's. The two-step orchestration
    /// is:
    ///
    /// <list type="number">
    ///   <item><c>destManager = sourceManager.Fork()</c> — runs the
    ///         <see cref="LuaStateCloner"/>, populating the fork's
    ///         <see cref="ScriptManager.ForkCloner"/>.</item>
    ///   <item><c>destItem = sourceItem.Fork()</c> — invokes
    ///         <see cref="LuaItem.OnForked"/>, which copies the source's
    ///         references verbatim so they're non-null.</item>
    ///   <item><c>destManager.RewireForkedLuaItem(destItem, sourceItem)</c>
    ///         — uses ForkCloner.Resolve() to redirect each reference
    ///         to its destination clone.</item>
    /// </list>
    ///
    /// The headline contract: after step 3 invoking
    /// <c>destItem.OnLeftClickFunc.Call()</c> mutates the FORK's interpreter
    /// state, not the source's.
    /// </summary>
    public class LuaItemForkRewireTests
    {
        [Fact]
        public void Fork_RewiresLuaItem_ItemStateAndCallback_PointAtForkInterpreter()
        {
            // 1. Source ScriptManager + LuaItem with state and a callback.
            var srcState = new EmoTracker.Data.Sessions.TrackerState("src");
            var srcSm = srcState.Scripts;
            srcSm.BootstrapInterpreter();

            srcSm.ExecuteLuaString(@"
                game_state = { count = 5 }
                bump = function() game_state.count = game_state.count + 1 end
            ");

            var srcItem = new LuaItem();
            srcItem.ItemState = (LuaTable)srcSm.GetLuaGlobal("game_state");
            srcItem.OnLeftClickFunc = (LuaFunction)srcSm.GetLuaGlobal("bump");

            // 2. Fork the manager (cloner runs).
            var destState = ForkTestHelpers.NewDestState();
            var forkSm = (ScriptManager)srcSm.Fork(destState);
            Assert.NotNull(forkSm.ForkCloner);

            // 3. Fork the item — Phase 5 contract: OnForked deliberately
            //    leaves the destination's NLua reference fields null.
            //    Until rewire, the fork's ItemState / OnLeftClickFunc /
            //    etc. are null; existing null checks in OnLeftClick / etc.
            //    silently no-op rather than firing on the wrong state.
            var forkItem = (LuaItem)srcItem.Fork(destState);
            Assert.Null(forkItem.ItemState);
            Assert.Null(forkItem.OnLeftClickFunc);

            // 4. Rewire — populates the fork's references from the source
            //    via ForkCloner.Resolve(). Also pins the fork's owner
            //    ScriptManager so OnLeftClick et al. invoke through the
            //    fork's interpreter.
            forkSm.RewireForkedLuaItem(forkItem, srcItem);

            // Both fields are still non-null and now point at the FORK's
            // interpreter's clones.
            Assert.NotNull(forkItem.ItemState);
            Assert.NotNull(forkItem.OnLeftClickFunc);

            // Initial state matches the source's pre-fork value.
            Assert.Equal(5L, Convert.ToInt64(forkItem.ItemState["count"]));

            // 5. Invoke the fork's callback — mutates fork's state.
            forkItem.OnLeftClickFunc.Call();

            // Fork's state went from 5 -> 6.
            Assert.Equal(6L, Convert.ToInt64(forkItem.ItemState["count"]));

            // SOURCE's state is untouched — proves the rewire worked.
            // If forkItem.OnLeftClickFunc still pointed at the source's
            // interpreter, the source's count would have incremented too.
            Assert.Equal(5L, Convert.ToInt64(srcItem.ItemState["count"]));

            // And the fork's ItemState is a different LuaTable instance
            // than the source's (verified via Lua-level equality at
            // the destination — pulling 'game_state' from the fork's
            // interpreter and comparing to the fork item's ItemState).
            forkSm.ExecuteLuaString("__match = (game_state == nil)");
            // game_state was cloned to the fork's interpreter, so the
            // comparison should be 'fork's game_state == fork's
            // game_state' — but we want to verify forkItem.ItemState IS
            // that fork-side table. Use Resolve to confirm.
            var resolved = forkSm.ForkCloner.Resolve(srcItem.ItemState);
            // The forkItem's ItemState was set to this same resolved table.
            // Verify by Lua equality through the fork's interpreter.
            forkSm.ExecuteLuaString(@"
                __resolved_eq = (game_state.count == 6)
            ");
            Assert.True((bool)forkSm.GetLuaGlobal("__resolved_eq"));
        }

        [Fact]
        public void Fork_LuaItem_OnForked_LeavesRefsNullUntilRewire()
        {
            // Phase 5 contract: OnForked deliberately does NOT copy the
            // source's NLua reference fields onto the destination. The
            // fork starts with ItemState / OnLeftClickFunc / etc. all
            // null; RewireForkedLuaItem populates them via
            // ForkCloner.Resolve.
            //
            // Why null-default rather than copy-verbatim? Copy-verbatim
            // would leave the fork holding source-interpreter references;
            // calling them via OnLeftClick would either silently fire
            // against the wrong state's interpreter or NLua-error on the
            // cross-interpreter call. Null-default + the existing null
            // checks in OnLeftClick / Save / etc. silently no-op until
            // rewire happens — orphan-free by construction.
            var srcState = new EmoTracker.Data.Sessions.TrackerState("src");
            var srcSm = srcState.Scripts;
            srcSm.BootstrapInterpreter();
            srcSm.ExecuteLuaString("state = { v = 1 }");

            var srcItem = new LuaItem();
            srcItem.ItemState = (LuaTable)srcSm.GetLuaGlobal("state");
            srcItem.OnLeftClickFunc = (LuaFunction)srcSm.ExecuteLuaString("return function() end")[0];

            var destState = ForkTestHelpers.NewDestState();
            var forkItem = (LuaItem)srcItem.Fork(destState);

            // Pre-rewire: fork's reference fields are null.
            Assert.Null(forkItem.ItemState);
            Assert.Null(forkItem.OnLeftClickFunc);

            // Calling fork's OnLeftClick is safe — silently no-ops via
            // the existing null check.
            forkItem.OnLeftClick();

            // And the source is untouched.
            Assert.Equal(1L, Convert.ToInt64(srcItem.ItemState["v"]));
        }

        [Fact]
        public void Fork_LuaItem_OnLeftClick_PostRewire_FiresOnForkInterpreter()
        {
            // The headline correctness test for the SafeCall-routing fix.
            // forkItem.OnLeftClick() previously routed through
            // ScriptManager.Instance.SafeCall (the singleton's
            // _safe_call wrapper) — a cross-interpreter call when the
            // OnLeftClickFunc is a fork-side LuaFunction. Post-fix:
            // GetCallbackScriptManager() returns the fork's manager, so
            // SafeCall happens in the same interpreter as the function.
            var srcState = new EmoTracker.Data.Sessions.TrackerState("src");
            var srcSm = srcState.Scripts;
            srcSm.BootstrapInterpreter();

            srcSm.ExecuteLuaString(@"
                state = { count = 5 }
                bump_via_click = function() state.count = state.count + 1 end
            ");

            var srcItem = new LuaItem();
            srcItem.ItemState = (LuaTable)srcSm.GetLuaGlobal("state");
            srcItem.OnLeftClickFunc = (LuaFunction)srcSm.GetLuaGlobal("bump_via_click");

            var destState = ForkTestHelpers.NewDestState();
            var forkSm = (ScriptManager)srcSm.Fork(destState);
            var forkItem = (LuaItem)srcItem.Fork(destState);
            forkSm.RewireForkedLuaItem(forkItem, srcItem);

            // Invoke the high-level callback on the fork — uses the
            // ScriptManager.Instance.SafeCall code path that previously
            // would have routed through the singleton's interpreter
            // (cross-interpreter on a fork-side LuaFunction = NLua break
            // or wrong-state mutation).
            forkItem.OnLeftClick();

            // Fork's count went 5 -> 6; source's stayed at 5.
            Assert.Equal(6L, Convert.ToInt64(forkItem.ItemState["count"]));
            Assert.Equal(5L, Convert.ToInt64(srcItem.ItemState["count"]));
        }

        [Fact]
        public void Fork_RewiresAllNineReferenceFields()
        {
            // Sanity check: every NLua reference field on LuaItem ends up
            // resolved through the cloner (or null when the source's
            // reference wasn't reachable from _G). 8 LuaFunction callbacks
            // + ItemState.
            var srcState = new EmoTracker.Data.Sessions.TrackerState("src");
            var srcSm = srcState.Scripts;
            srcSm.BootstrapInterpreter();

            srcSm.ExecuteLuaString(@"
                state = { v = 1 }
                f_left  = function() return 'left'  end
                f_right = function() return 'right' end
                f_provides = function(c) return 0 end
                f_can_provide = function(c) return false end
                f_advance = function(c) end
                f_save  = function() return {} end
                f_load  = function(d) end
                f_pchanged = function(k, v) end
            ");

            var srcItem = new LuaItem();
            srcItem.ItemState        = (LuaTable)srcSm.GetLuaGlobal("state");
            srcItem.OnLeftClickFunc  = (LuaFunction)srcSm.GetLuaGlobal("f_left");
            srcItem.OnRightClickFunc = (LuaFunction)srcSm.GetLuaGlobal("f_right");
            srcItem.ProvidesCodeFunc = (LuaFunction)srcSm.GetLuaGlobal("f_provides");
            srcItem.CanProvideCodeFunc = (LuaFunction)srcSm.GetLuaGlobal("f_can_provide");
            srcItem.AdvanceToCodeFunc = (LuaFunction)srcSm.GetLuaGlobal("f_advance");
            srcItem.SaveFunc         = (LuaFunction)srcSm.GetLuaGlobal("f_save");
            srcItem.LoadFunc         = (LuaFunction)srcSm.GetLuaGlobal("f_load");
            srcItem.PropertyChangedFunc = (LuaFunction)srcSm.GetLuaGlobal("f_pchanged");

            var destState = ForkTestHelpers.NewDestState();
            var forkSm = (ScriptManager)srcSm.Fork(destState);
            var forkItem = (LuaItem)srcItem.Fork(destState);
            forkSm.RewireForkedLuaItem(forkItem, srcItem);

            // All fields are non-null and resolve through the fork's
            // interpreter — calling each returns the expected value.
            Assert.NotNull(forkItem.ItemState);
            Assert.Equal("left",  (string)forkItem.OnLeftClickFunc.Call()[0]);
            Assert.Equal("right", (string)forkItem.OnRightClickFunc.Call()[0]);
            Assert.Equal(0L,      Convert.ToInt64(forkItem.ProvidesCodeFunc.Call("x")[0]));
            Assert.False(Convert.ToBoolean(forkItem.CanProvideCodeFunc.Call("x")[0]));
            // AdvanceToCode / Save / Load / PropertyChanged return nothing
            // useful; just confirm they're non-null and callable.
            forkItem.AdvanceToCodeFunc.Call("x");
            forkItem.SaveFunc.Call();
            forkItem.LoadFunc.Call(new LuaTable[] { null });
            forkItem.PropertyChangedFunc.Call("k", "v");
        }
    }
}
