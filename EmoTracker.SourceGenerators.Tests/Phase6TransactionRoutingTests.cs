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
    /// <see cref="TrackerState.Transactions"/> when the model has been
    /// claimed by a state. Falls through to the global
    /// <see cref="TransactionProcessor.Current"/> for unowned models —
    /// the path every Phase 0–5 model uses today.
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
        public void TransactableWrite_OnUnownedModel_FallsThroughToAmbient()
        {
            // Phase 0–5 contract preserved: a model with no OwnerState
            // routes through TransactionProcessor.Current — the same
            // singleton it always has. No regression for legacy callers.
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            Assert.Null(smoke.OwnerState);

            using (TransactionProcessor.Current.OpenTransaction())
                smoke.Active = true;
            Assert.True(smoke.Active);

            ((IUndoableTransactionProcessor)TransactionProcessor.Current).Undo();
            Assert.False(smoke.Active);
        }

        [Fact]
        public void OpenTransaction_OnModel_ReturnsScopeBoundToOwnerProcessor()
        {
            // model.OpenTransaction() opens on the owner's processor, not
            // the singleton's. Verifies by checking the resolved scope's
            // currency on each side.
            var state = new TrackerState();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            smoke.OwnerState = state;

            using (smoke.OpenTransaction())
            {
                // The owner's processor sees the open scope.
                Assert.NotNull(state.Transactions.CurrentScope);
                // The singleton (assuming it's a different processor) doesn't.
                if (!ReferenceEquals(state.Transactions, TransactionProcessor.Current))
                    Assert.Null(TransactionProcessor.Current.CurrentScope);
            }

            // Scope closes on dispose.
            Assert.Null(state.Transactions.CurrentScope);

            smoke.OwnerState = null;
        }
    }
}
