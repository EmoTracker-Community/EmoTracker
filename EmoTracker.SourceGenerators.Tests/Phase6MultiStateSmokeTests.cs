using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Sessions;
using NLua;
using System;
using System.Linq;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 6 step 10: end-to-end multi-state smoke tests. The earlier
    /// Phase 6 test files cover individual invariants (foundation,
    /// per-state catalogs, transaction routing, coordinated fork). This
    /// file integrates them into the runtime scenarios called out in
    /// plan §6.8 verification:
    ///
    /// <list type="bullet">
    ///   <item>Multi-state coexistence: mutations on state A leave state B
    ///         untouched (and vice versa).</item>
    ///   <item>Fork-then-discard: forking a state, mutating, disposing,
    ///         and verifying the source state is unaffected.</item>
    ///   <item>DefinitionId invariants: forks preserve DefinitionId across
    ///         instances so cross-state references keep resolving.</item>
    ///   <item>PrimaryStateModelResolver routing: the step-9 ambient
    ///         resolver delegates correctly through SessionContext.</item>
    /// </list>
    ///
    /// <para>
    /// These tests construct items and locations directly (no pack load).
    /// Pack-level integration is exercised by the EmoTracker app's MCP
    /// smoke against ALttPR, run by hand outside the test harness.
    /// </para>
    /// </summary>
    [Collection(nameof(TransactionCollection))]
    public class Phase6MultiStateSmokeTests
    {
        readonly TransactionFixture _fix;

        public Phase6MultiStateSmokeTests(TransactionFixture fix)
        {
            _fix = fix;
        }

        // -------- Multi-state coexistence: items ----------------------------

        [Fact]
        public void TwoStates_ToggleItemMutations_AreIsolated()
        {
            // Build two independent states, each with its own ToggleItem.
            // Toggle one — the other stays at its initial value.
            var stateA = new TrackerState("A");
            var stateB = new TrackerState("B");

            var itemA = new ToggleItem { Name = "boomerang" };
            itemA.OwnerState = stateA;
            stateA.Items.RegisterItem(itemA);

            var itemB = new ToggleItem { Name = "boomerang" };
            itemB.OwnerState = stateB;
            stateB.Items.RegisterItem(itemB);

            Assert.False(itemA.Active);
            Assert.False(itemB.Active);

            using (itemA.OpenTransaction())
                itemA.Active = true;

            Assert.True(itemA.Active);
            Assert.False(itemB.Active);  // independent state untouched

            // The state's own undo stack reverts only its own change.
            stateA.Transactions.Undo();
            Assert.False(itemA.Active);
            Assert.False(itemB.Active);

            stateA.Dispose();
            stateB.Dispose();
        }

        [Fact]
        public void ForkedState_ToggleItemMutations_ArePerState()
        {
            // Fork a state and verify the fork's items are COW-isolated:
            // mutating the fork doesn't disturb the source.
            var src = new TrackerState("source");
            var item = new ToggleItem { Name = "hookshot" };
            item.OwnerState = src;
            src.Items.RegisterItem(item);

            var fork = src.Fork("fork");
            Assert.NotSame(src.Items, fork.Items);
            Assert.Single(fork.Items.Items);

            // The fork's hookshot is a separate ToggleItem instance.
            ToggleItem forkItem = null;
            foreach (var t in fork.Items.Items)
            {
                if (t is ToggleItem ti && ti.DefinitionId == item.DefinitionId)
                {
                    forkItem = ti;
                    break;
                }
            }
            Assert.NotNull(forkItem);
            Assert.NotSame(item, forkItem);
            Assert.Equal(item.DefinitionId, forkItem.DefinitionId);
            Assert.Same(fork, forkItem.OwnerState);

            // Mutate the fork's item; source's stays at the inherited value.
            using (forkItem.OpenTransaction())
                forkItem.Active = true;

            Assert.True(forkItem.Active);
            Assert.False(item.Active);  // source unaffected

            // Mutate the source's item; fork's stays at its own value.
            using (item.OpenTransaction())
                item.Active = true;

            Assert.True(item.Active);
            Assert.True(forkItem.Active);  // fork still its own value

            // Drive the source down to false; fork's value still its own.
            src.Transactions.Undo();
            Assert.False(item.Active);
            Assert.True(forkItem.Active);

            fork.Dispose();
            src.Dispose();
        }

        // -------- Fork-then-discard cleanup ---------------------------------

        [Fact]
        public void ForkThenDispose_LeavesSourceFunctional()
        {
            // The plan §6.8 fork-then-discard scenario: take a fork, mutate
            // destructively, dispose. The source state continues to operate
            // normally afterward.
            var src = new TrackerState("source");
            var item = new ToggleItem { Name = "lamp" };
            item.OwnerState = src;
            src.Items.RegisterItem(item);

            // Drive source's lamp to active before forking.
            using (item.OpenTransaction())
                item.Active = true;
            Assert.True(item.Active);

            // Fork. Mutate the fork's lamp aggressively.
            var fork = src.Fork("fork");
            ToggleItem forkLamp = null;
            foreach (var t in fork.Items.Items)
                if (t is ToggleItem tt && tt.DefinitionId == item.DefinitionId) forkLamp = tt;
            Assert.NotNull(forkLamp);
            Assert.True(forkLamp.Active);  // inherited via COW

            // Toggle the fork off, mutate the fork's name, then discard.
            using (forkLamp.OpenTransaction())
                forkLamp.Active = false;
            forkLamp.Name = "discarded";
            Assert.False(forkLamp.Active);

            fork.Dispose();

            // Source state still works — lamp's state unchanged.
            Assert.True(item.Active);
            Assert.Equal("lamp", item.Name);

            // Source can still be mutated post-fork-disposal.
            using (item.OpenTransaction())
                item.Active = false;
            Assert.False(item.Active);

            src.Dispose();
        }

        // -------- DefinitionId invariants -----------------------------------

        [Fact]
        public void Fork_PreservesDefinitionId_AcrossInstances()
        {
            // The fork pipeline must preserve DefinitionId so that cross-
            // references that captured the source's DefinitionId still
            // resolve in the fork (to the fork's instance with the same id).
            var src = new TrackerState("source");
            var itemA = new ToggleItem { Name = "a" };
            var itemB = new ToggleItem { Name = "b" };
            src.Items.RegisterItem(itemA);
            src.Items.RegisterItem(itemB);
            // Production adoption flow: stamp OwnerState + populate resolver.
            src.StampOwnerStateOnAdoptedModels();

            var fork = src.Fork();

            // Look up each fork-side instance by source DefinitionId; the
            // fork has its own ToggleItem with the same DefinitionId.
            var forkA = fork.Resolve<ToggleItem>(itemA.DefinitionId);
            var forkB = fork.Resolve<ToggleItem>(itemB.DefinitionId);

            Assert.NotNull(forkA);
            Assert.NotNull(forkB);
            Assert.NotSame(itemA, forkA);
            Assert.NotSame(itemB, forkB);
            Assert.Equal(itemA.DefinitionId, forkA.DefinitionId);
            Assert.Equal(itemB.DefinitionId, forkB.DefinitionId);

            // Source resolver still returns source instances.
            Assert.Same(itemA, src.Resolve<ToggleItem>(itemA.DefinitionId));
            Assert.Same(itemB, src.Resolve<ToggleItem>(itemB.DefinitionId));

            fork.Dispose();
            src.Dispose();
        }

        [Fact]
        public void IndexedModelResolver_OnFork_Distinguishes_PerStateInstances()
        {
            // Cross-check: each state's IndexedModelResolver returns its
            // own state's instance for the same DefinitionId. The plan §6.6
            // contract: "each TrackerState is itself an IModelResolver".
            var src = new TrackerState();
            var item = new ToggleItem();
            src.Items.RegisterItem(item);
            // Production adoption flow: stamp OwnerState + populate resolver.
            src.StampOwnerStateOnAdoptedModels();

            var fork = src.Fork();
            var forkItem = fork.Resolve<ToggleItem>(item.DefinitionId);

            // Both states resolve to their own instance.
            Assert.Same(item, ((IModelResolver)src).Resolve<ToggleItem>(item.DefinitionId));
            Assert.Same(forkItem, ((IModelResolver)fork).Resolve<ToggleItem>(item.DefinitionId));
            Assert.NotSame(item, forkItem);

            fork.Dispose();
            src.Dispose();
        }

        // -------- PrimaryStateModelResolver routing -------------------------

        [Fact]
        public void PrimaryStateModelResolver_RoutesThroughSessionContext()
        {
            // The step-9 resolver delegates to whichever state is installed
            // as SessionContext.ActiveState. Switching the active state
            // changes which IndexedModelResolver answers lookups.
            var prior = SessionContext.ActiveState;
            try
            {
                var stateA = new TrackerState("A");
                var stateB = new TrackerState("B");
                var itemA = new ToggleItem();
                var itemB = new ToggleItem();
                stateA.Items.RegisterItem(itemA);
                stateB.Items.RegisterItem(itemB);
                // Production adoption flow: stamp OwnerState + populate resolver.
                stateA.StampOwnerStateOnAdoptedModels();
                stateB.StampOwnerStateOnAdoptedModels();

                var resolver = new EmoTracker.Data.Core.DataModel.PrimaryStateModelResolver();

                // No active state → null lookup.
                SessionContext.ActiveState = null;
                Assert.Null(resolver.Resolve<ToggleItem>(itemA.DefinitionId));

                // Activate state A → resolver routes to state A's index.
                SessionContext.ActiveState = stateA;
                Assert.Same(itemA, resolver.Resolve<ToggleItem>(itemA.DefinitionId));
                // State A doesn't know about state B's items.
                Assert.Null(resolver.Resolve<ToggleItem>(itemB.DefinitionId));

                // Switch active state → lookups follow.
                SessionContext.ActiveState = stateB;
                Assert.Same(itemB, resolver.Resolve<ToggleItem>(itemB.DefinitionId));
                Assert.Null(resolver.Resolve<ToggleItem>(itemA.DefinitionId));
            }
            finally
            {
                SessionContext.ActiveState = prior;
            }
        }

        [Fact]
        public void GetModelResolver_PrefersOwnerState_OverPrimaryStateResolver()
        {
            // ModelTypeBase.GetModelResolver returns OwnerState directly
            // when present — it doesn't fall through to ModelResolver.Current
            // (which the app installs as PrimaryStateModelResolver). This
            // is the structural guarantee that a model's lookups stay in
            // its own state regardless of which state is active globally.
            var prior = SessionContext.ActiveState;
            try
            {
                var stateA = new TrackerState("A");
                var stateB = new TrackerState("B");
                var item = new ToggleItem();
                item.OwnerState = stateA;
                stateA.Items.RegisterItem(item);

                // Make state B the active state — a curveball.
                SessionContext.ActiveState = stateB;

                // The item's GetModelResolver returns stateA, not stateB,
                // because OwnerState wins.
                Assert.Same(stateA, item.GetModelResolver());
            }
            finally
            {
                SessionContext.ActiveState = prior;
            }
        }

        // -------- Per-state Lua isolation -----------------------------------

        [Fact]
        public void TwoStates_LuaGlobals_AreIsolated()
        {
            // Per plan §6.8: Set a Lua global on state A's interpreter;
            // confirm state B's interpreter does not see it.
            var stateA = new TrackerState("A");
            var stateB = new TrackerState("B");

            stateA.Scripts.BootstrapInterpreter();
            stateB.Scripts.BootstrapInterpreter();

            stateA.Scripts.ExecuteLuaString("game_state = { foo = 42 }");

            // State B's interpreter doesn't see A's global.
            var bGlobal = stateB.Scripts.GetLuaGlobal("game_state");
            Assert.Null(bGlobal);

            // Set a different value on B; A is unaffected.
            stateB.Scripts.ExecuteLuaString("game_state = { foo = 99 }");
            var aGlobal = (LuaTable)stateA.Scripts.GetLuaGlobal("game_state");
            Assert.NotNull(aGlobal);
            Assert.Equal(42L, Convert.ToInt64(aGlobal["foo"]));

            stateA.Dispose();
            stateB.Dispose();
        }

        // -------- Single-state parity smoke ---------------------------------

        [Fact]
        public void StateOwnedItem_TransactableWritesUndoRedoCleanly()
        {
            // Single-state parity: the Phase 0–5 transactable-write +
            // undo / redo cycle works on a state-owned item exactly as
            // it did on the singleton-rooted graph.
            var state = new TrackerState();
            var item = new ToggleItem { Name = "bow" };
            item.OwnerState = state;
            state.Items.RegisterItem(item);

            using (item.OpenTransaction())
                item.Active = true;
            using (item.OpenTransaction())
                item.Active = false;
            using (item.OpenTransaction())
                item.Active = true;

            // Three commits → three undos rewinds to the initial state.
            Assert.True(item.Active);
            state.Transactions.Undo();
            Assert.False(item.Active);
            state.Transactions.Undo();
            Assert.True(item.Active);
            state.Transactions.Undo();
            Assert.False(item.Active);

            state.Dispose();
        }

        // -------- Adoption disposal contract (the runtime regression) ------

        [Fact]
        public void AdoptedState_Dispose_DoesNotTearDownSharedCollaborators()
        {
            // Plan §6.7 invariant + step-10 runtime regression: when a
            // TrackerState is constructed via the adoption ctor (as
            // ApplicationModel.RebindActivePackageInstanceFromSingletons
            // does on every pack-load and reload), its collaborators are
            // shared with the legacy singletons + with subsequent adopted
            // states. Disposing such a state must NOT tear those
            // collaborators down — otherwise the next pack-load's primary
            // state adopts the same singleton ScriptManager whose
            // mLua got closed during the previous teardown, which
            // surfaces as NRE in InvokeStandardCallback / ProviderCountForCode
            // the moment a section fires its property-change callbacks.
            var sharedScripts = new ScriptManager();
            sharedScripts.BootstrapInterpreter();
            Assert.True(sharedScripts.IsLuaLoaded);

            var sharedItems = new ItemDatabase();
            var sharedLocations = new LocationDatabase();
            var sharedMaps = new MapDatabase();
            var sharedLayouts = new EmoTracker.Data.Layout.LayoutManager();
            var sharedTx = new EmoTracker.Data.Core.Transactions.Processors.LocalTransactionProcessorWithUndo();

            var adopted = new TrackerState(
                name: "primary",
                scripts: sharedScripts,
                transactions: sharedTx,
                items: sharedItems,
                locations: sharedLocations,
                maps: sharedMaps,
                layouts: sharedLayouts);

            adopted.Dispose();

            // The shared ScriptManager survives — its Lua interpreter
            // is still alive, ready for the next adoption cycle.
            Assert.True(sharedScripts.IsLuaLoaded);

            // Cleanup the actually-owned resource.
            sharedScripts.Dispose();
        }

        [Fact]
        public void NonAdoptedState_Dispose_TearsDownItsOwnScripts()
        {
            // The complement: when a TrackerState is constructed fresh
            // (no adoption parameters), it owns its collaborators and
            // Dispose tears them down — including closing the Lua
            // interpreter on its ScriptManager.
            var state = new TrackerState("fresh");
            state.Scripts.BootstrapInterpreter();
            Assert.True(state.Scripts.IsLuaLoaded);

            var ownedScripts = state.Scripts;
            state.Dispose();

            // Fresh state's Scripts was owned, so disposal closed its mLua.
            Assert.False(ownedScripts.IsLuaLoaded);
        }

        [Fact]
        public void StateOwnedItem_DependentPropertiesFire_OnTransactableWrite()
        {
            // The Phase 1 INPC plumbing still works on a state-owned model:
            // transactable property writes raise PropertyChanged for both
            // the property itself and its declared DependentProperty(s).
            var state = new TrackerState();
            var item = new ToggleItem { Name = "sword" };
            item.OwnerState = state;
            state.Items.RegisterItem(item);

            var changes = new System.Collections.Generic.List<string>();
            ((System.ComponentModel.INotifyPropertyChanged)item).PropertyChanged
                += (_, e) => changes.Add(e.PropertyName);

            using (item.OpenTransaction())
                item.Active = true;

            Assert.Contains("Active", changes);
            // ToggleItem declares PotentialIcon as DependentProperty of Active —
            // the icon-resolution chain consumed by Layout/Item rendering.
            Assert.Contains("PotentialIcon", changes);

            state.Dispose();
        }
    }
}
