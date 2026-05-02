using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel.SmokeTest;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Sessions;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 6 step 4: transactable property writes route through the
    /// owning <see cref="TrackerState"/>'s
    /// <see cref="TrackerState.Transactions"/>. Phase 7 retired the
    /// fallback to a process-wide processor — every transactable model
    /// must have an OwnerState, otherwise the write throws.
    ///
    /// <para>
    /// The headline contract: each TrackerState has its own undo stack;
    /// undoing on state A doesn't affect state B's pending writes.
    /// </para>
    /// </summary>
    [Collection(nameof(TransactionCollection))]
    public class Phase6TransactionRoutingTests
    {
        readonly TransactionFixture _fix;

        public Phase6TransactionRoutingTests(TransactionFixture fix)
        {
            _fix = fix;
        }

        [Fact]
        public void TrackerState_HasOwnTransactionProcessor()
        {
            var stateA = new TrackerState();
            var stateB = new TrackerState();

            Assert.NotNull(stateA.Transactions);
            Assert.NotNull(stateB.Transactions);
            Assert.NotSame(stateA.Transactions, stateB.Transactions);
            Assert.IsAssignableFrom<IUndoableTransactionProcessor>(stateA.Transactions);
        }

        [Fact]
        public void TransactableWrite_OnStateOwnedModel_RoutesThroughStateProcessor()
        {
            var state = new TrackerState();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            smoke.OwnerState = state;

            // Write through the model's OpenTransaction — opens a scope on
            // the OWNER's processor, not the singleton's.
            using (smoke.OpenTransaction())
                smoke.Active = true;

            Assert.True(smoke.Active);

            // Undo on the state's processor reverts the change. The global
            // processor's undo stack is untouched.
            state.Transactions.Undo();
            Assert.False(smoke.Active);

            smoke.OwnerState = null;
        }

        [Fact]
        public void TwoStates_UndoStacksAreIndependent()
        {
            // The headline contract: per-state undo stacks. Mutate models in
            // state A and state B separately; undoing on A doesn't disturb
            // B's pending or committed writes.
            var stateA = new TrackerState();
            var stateB = new TrackerState();

            var smokeA = Phase1SmokeModelType.CreateDefinition("a", null);
            smokeA.OwnerState = stateA;
            var smokeB = Phase1SmokeModelType.CreateDefinition("b", null);
            smokeB.OwnerState = stateB;

            // Mutate each model in its own state's scope.
            using (smokeA.OpenTransaction())
                smokeA.Active = true;
            using (smokeB.OpenTransaction())
                smokeB.SelectedColor = "blue";

            Assert.True(smokeA.Active);
            Assert.Equal("blue", smokeB.SelectedColor);

            // Undo on stateA reverts smokeA only.
            stateA.Transactions.Undo();
            Assert.False(smokeA.Active);
            Assert.Equal("blue", smokeB.SelectedColor);

            // Undo on stateB reverts smokeB.
            stateB.Transactions.Undo();
            Assert.Null(smokeB.SelectedColor);

            smokeA.OwnerState = null;
            smokeB.OwnerState = null;
        }

        [Fact]
        public void TransactableWrite_OnUnownedModel_Throws()
        {
            // Phase 7 contract: every transactable model must have an
            // OwnerState (and therefore a per-state processor). Writes on
            // an unowned model throw rather than silently falling through
            // to a global slot.
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            Assert.Null(smoke.OwnerState);

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
            {
                using (smoke.OpenTransaction())
                    smoke.Active = true;
            });
            Assert.Contains("OwnerState", ex.Message);
        }

        [Fact]
        public void OpenTransaction_OnModel_ReturnsScopeBoundToOwnerProcessor()
        {
            // model.OpenTransaction() opens on the owner's processor — there
            // is no other place a scope can be opened.
            var state = new TrackerState();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            smoke.OwnerState = state;

            using (smoke.OpenTransaction())
            {
                // The owner's processor sees the open scope.
                Assert.NotNull(state.Transactions.CurrentScope);
            }

            // Scope closes on dispose.
            Assert.Null(state.Transactions.CurrentScope);

            smoke.OwnerState = null;
        }
    }
}
