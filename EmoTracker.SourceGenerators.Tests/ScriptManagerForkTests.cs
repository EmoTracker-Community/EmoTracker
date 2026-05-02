using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using NLua;
using System;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 5 step 4: <see cref="ScriptManager.Fork"/> integrates the
    /// <see cref="EmoTracker.Data.Scripting.LuaStateCloner"/>. After Fork:
    ///
    /// <list type="bullet">
    ///   <item>The fork has its own <see cref="Lua"/> interpreter (no
    ///     interpreter sharing with the source).</item>
    ///   <item>The fork's interpreter has been bootstrapped (system Lua,
    ///     bridges, sandbox).</item>
    ///   <item>Pack-author globals from the source's <c>_G</c> have been
    ///     deep-cloned into the fork's interpreter (no Save/Load round
    ///     trip — direct deep copy).</item>
    ///   <item>Mutations on the source's interpreter after the fork do
    ///     not affect the fork's interpreter, and vice versa.</item>
    ///   <item><see cref="ScriptManager.ForkCloner"/> on the fork holds
    ///     the cloner with the populated identity map, so the next
    ///     Phase 5 step (LuaItem.OnForked field rewire) has the lookup
    ///     primitive it needs.</item>
    /// </list>
    /// </summary>
    public class ScriptManagerForkTests
    {
        // Each test uses fresh ScriptManager instances; we don't touch the
        // process-wide ScriptManager.Current singleton.

        [Fact]
        public void Fork_AllocatesFreshInterpreter_NotSharedWithSource()
        {
            var src = new ScriptManager();
            // Bootstrap the source's interpreter through the public API
            // surface; ExecuteLuaString needs IsLuaLoaded to be true.
            // BootstrapInterpreter() is internal — drive through the
            // expected production path: Load(package). For tests we hand
            // in a stub package.
            BootstrapWithStub(src);

            // Source has a Lua interpreter; fork before is null.
            Assert.True(src.IsLuaLoaded);

            var fork = (ScriptManager)src.Fork(ForkTestHelpers.NewDestState());
            Assert.True(fork.IsLuaLoaded);
            Assert.NotSame(src, fork);

            // The two interpreters are distinct: setting a global on src's
            // interpreter does NOT make it visible on fork's interpreter.
            src.ExecuteLuaString("__src_marker = 'src-only'");
            Assert.Null(fork.GetLuaGlobal("__src_marker"));
        }

        [Fact]
        public void Fork_ClonesPackAuthorGlobals_AcrossInterpreters()
        {
            var src = new ScriptManager();
            BootstrapWithStub(src);

            // Pack-author state on the source: a table that pack scripts
            // would have populated during init.lua.
            src.ExecuteLuaString(@"
                pack_state = { score = 7, name = 'alpha' }
                pack_helpers = {
                    bump = function(n) pack_state.score = pack_state.score + n end
                }
            ");

            var fork = (ScriptManager)src.Fork(ForkTestHelpers.NewDestState());

            // The fork's interpreter has its own pack_state with the same
            // values.
            var forkState = (LuaTable)fork.GetLuaGlobal("pack_state");
            Assert.NotNull(forkState);
            Assert.Equal(7L, Convert.ToInt64(forkState["score"]));
            Assert.Equal("alpha", (string)forkState["name"]);

            // Mutating the fork's state via its cloned helpers doesn't
            // disturb the source.
            fork.ExecuteLuaString("pack_helpers.bump(100)");
            var forkStateAfter = (LuaTable)fork.GetLuaGlobal("pack_state");
            Assert.Equal(107L, Convert.ToInt64(forkStateAfter["score"]));

            var srcStateAfter = (LuaTable)src.GetLuaGlobal("pack_state");
            Assert.Equal(7L, Convert.ToInt64(srcStateAfter["score"]));
        }

        [Fact]
        public void Fork_ExposesClonerForDownstreamLuaItemRewire()
        {
            var src = new ScriptManager();
            BootstrapWithStub(src);
            src.ExecuteLuaString("test_table = { hello = 'world' }");

            var srcTable = (LuaTable)src.GetLuaGlobal("test_table");

            var fork = (ScriptManager)src.Fork(ForkTestHelpers.NewDestState());

            // ForkCloner is populated and Resolve maps source-side
            // references to destination clones — exactly the lookup
            // shape LuaItem.OnForked will use in step 7.
            Assert.NotNull(fork.ForkCloner);
            var resolved = fork.ForkCloner.Resolve(srcTable);
            Assert.NotNull(resolved);
            Assert.Equal("world", (string)resolved["hello"]);
        }

        [Fact]
        public void Fork_StandardCallback_InvokedThroughForkUsesItsOwnInterpreter()
        {
            // The headline contract for steps 5/7: a standard callback
            // dispatched through the fork's ScriptManager fetches the
            // function from the FORK's _G, not the source's. This test
            // demonstrates the principle by installing different
            // implementations of the same callback name on each manager
            // and observing which one runs.
            var src = new ScriptManager();
            BootstrapWithStub(src);

            // Both managers will have a 'tracker_on_pack_ready' but they'll
            // mark different globals to prove which one ran.
            src.ExecuteLuaString(@"
                tracker_on_pack_ready = function() src_called = true end
            ");

            var fork = (ScriptManager)src.Fork(ForkTestHelpers.NewDestState());

            // Replace the fork's callback with one that marks a fork-local.
            fork.ExecuteLuaString(@"
                tracker_on_pack_ready = function() fork_called = true end
                src_called = nil
                fork_called = nil
            ");

            // Same on source — clean both flags so we measure THIS test's run.
            src.ExecuteLuaString("src_called = nil; fork_called = nil");

            // Invoke through the IScriptManager surface on each.
            ((IScriptManager)src).InvokeStandardCallback(StandardCallback.PackReady);
            ((IScriptManager)fork).InvokeStandardCallback(StandardCallback.PackReady);

            // Source's Lua saw its own callback fire; fork's saw its own.
            // No cross-contamination of either way.
            Assert.True(Convert.ToBoolean(src.GetLuaGlobal("src_called")));
            Assert.True(Convert.ToBoolean(fork.GetLuaGlobal("fork_called")));
            Assert.Null(src.GetLuaGlobal("fork_called"));
            Assert.Null(fork.GetLuaGlobal("src_called"));
        }

        [Fact]
        public void Fork_PreservesDefinitionId_AcrossFork()
        {
            // ScriptManager is now a ModelTypeBase; a fork shares its
            // ImmutableData by reference and therefore its DefinitionId.
            var src = new ScriptManager();
            BootstrapWithStub(src);

            var fork = (ScriptManager)src.Fork(ForkTestHelpers.NewDestState());

            Assert.NotEqual(Guid.Empty, src.DefinitionId);
            Assert.Equal(src.DefinitionId, fork.DefinitionId);
        }

        [Fact]
        public void Fork_OnUnInitializedSource_LeavesForkBootstrappedButEmpty()
        {
            // Edge case: forking a ScriptManager that never had Load called
            // (no interpreter). The fork still bootstraps its own
            // interpreter (so callers don't get null mLua surprises) but
            // there's nothing to clone — the cloner just doesn't run.
            var srcState = new EmoTracker.Data.Sessions.TrackerState("src-uninit");
            var src = srcState.Scripts;
            Assert.False(src.IsLuaLoaded);

            var fork = (ScriptManager)src.Fork(ForkTestHelpers.NewDestState());
            Assert.True(fork.IsLuaLoaded);
            Assert.Null(fork.ForkCloner); // no clone happened
        }

        // ---- Helpers --------------------------------------------------------

        // Drives the ScriptManager into its bootstrapped state without
        // invoking pack-load (which would hit a package's filesystem and
        // attempt to run scripts/init.lua). BootstrapInterpreter is exposed
        // at internal scope so test code (with InternalsVisibleTo) can
        // drive the same scaffolding the production Load() path uses.
        // Wraps the manager in a fresh TrackerState since BootstrapInterpreter
        // requires OwnerState to construct the Tracker / Layout bridge globals.
        static void BootstrapWithStub(ScriptManager sm)
        {
            if (sm.OwnerState == null)
            {
                var hostState = new EmoTracker.Data.Sessions.TrackerState("test-host");
                // Replace the host state's auto-allocated ScriptManager
                // with sm so OwnerState wiring matches the test's manager.
                sm.OwnerState = hostState;
            }
            sm.BootstrapInterpreter();
        }
    }
}
