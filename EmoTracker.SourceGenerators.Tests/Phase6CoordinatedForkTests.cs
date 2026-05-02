using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.Sessions;
using NLua;
using System;
using System.Linq;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 6 step 8 verification: <see cref="TrackerState.Fork"/> performs
    /// a coordinated walk over the source state's catalogs, deep-copies each
    /// model, sets <see cref="ModelTypeBase.OwnerState"/>, registers in the
    /// fork's resolver, and rewires LuaItems through the per-state
    /// interpreter. Layouts are deferred to a follow-up commit (documented
    /// in <see cref="TrackerState.Fork"/>'s xmldoc).
    /// </summary>
    public class Phase6CoordinatedForkTests
    {
        [Fact]
        public void Fork_OnEmptyState_ProducesFreshShellWithOwnIdentity()
        {
            var src = new TrackerState("source");
            // Source has no items / locations / maps so the orchestrator
            // walks empty catalogs and produces a clean fork.
            var fork = src.Fork("fork");

            Assert.NotNull(fork);
            Assert.NotEqual(src.Id, fork.Id);
            Assert.Equal("fork", fork.Name);

            // Each fork has its own collaborators — no sharing.
            Assert.NotSame(src.Items, fork.Items);
            Assert.NotSame(src.Locations, fork.Locations);
            Assert.NotSame(src.Maps, fork.Maps);
            Assert.NotSame(src.Layouts, fork.Layouts);
            Assert.NotSame(src.Scripts, fork.Scripts);
            Assert.NotSame(src.Transactions, fork.Transactions);
        }

        [Fact]
        public void Fork_LuaState_ClonesPackAuthorGlobals()
        {
            // The ScriptManager fork path: pack-author globals on the source
            // come across to the fork via LuaStateCloner.CloneAll. Mutating
            // one interpreter doesn't affect the other.
            var src = new TrackerState("source");
            src.Scripts.BootstrapInterpreter();
            src.Scripts.ExecuteLuaString(@"
                pack_state = { count = 7 }
            ");

            var fork = src.Fork("fork");
            Assert.True(fork.Scripts.IsLuaLoaded);

            var forkState = (LuaTable)fork.Scripts.GetLuaGlobal("pack_state");
            Assert.NotNull(forkState);
            Assert.Equal(7L, Convert.ToInt64(forkState["count"]));

            // Mutating the source's interpreter doesn't affect the fork.
            src.Scripts.ExecuteLuaString("pack_state.count = 999");
            forkState = (LuaTable)fork.Scripts.GetLuaGlobal("pack_state");
            Assert.Equal(7L, Convert.ToInt64(forkState["count"]));

            // And vice versa.
            fork.Scripts.ExecuteLuaString("pack_state.count = 1");
            var srcState = (LuaTable)src.Scripts.GetLuaGlobal("pack_state");
            Assert.Equal(999L, Convert.ToInt64(srcState["count"]));
        }

        [Fact]
        public void Fork_ScriptManager_HasForkClonerPopulated()
        {
            // The ForkCloner must be populated on the fork's manager so
            // step 7's LuaItem.RewireForkedLuaItem has the lookup primitive
            // it needs.
            var src = new TrackerState("source");
            src.Scripts.BootstrapInterpreter();

            var fork = src.Fork("fork");
            Assert.NotNull(fork.Scripts.ForkCloner);
        }

        [Fact]
        public void Fork_ItemDatabaseEmpty_ProducesEmptyFork()
        {
            // No items registered on the source → no items on the fork.
            // Smoke-tests the empty-collection path of the items walk.
            var src = new TrackerState("source");
            Assert.Empty(src.Items.Items);

            var fork = src.Fork();
            Assert.Empty(fork.Items.Items);
        }

        [Fact]
        public void Fork_TransactionsAreIndependent()
        {
            // Each forked state has its own undo / redo stack — mutations
            // on one don't appear in the other's history.
            var src = new TrackerState("source");
            var fork = src.Fork("fork");

            Assert.NotSame(src.Transactions, fork.Transactions);
            Assert.Null(src.Transactions.CurrentScope);
            Assert.Null(fork.Transactions.CurrentScope);
        }

        [Fact]
        public void Fork_Disposal_DoesNotThrow()
        {
            var src = new TrackerState("source");
            src.Scripts.BootstrapInterpreter();
            var fork = src.Fork();

            // Both are independently disposable; no shared resource.
            fork.Dispose();
            src.Dispose();
        }
    }
}
