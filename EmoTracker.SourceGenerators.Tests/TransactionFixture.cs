using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Sessions;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Provides a per-state-host TrackerState for transactable-property tests.
    /// The static <c>TransactionProcessor.Current</c> slot was retired; every
    /// transactable model gets its processor from <c>OwnerState.Transactions</c>,
    /// so a TrackerState fixture is the new single source of a working
    /// write/undo path. Tests that need it reference the
    /// <see cref="TransactionCollection"/> in <c>[Collection]</c>.
    /// </summary>
    public sealed class TransactionFixture
    {
        public TrackerState State { get; }
        public IUndoableTransactionProcessor Processor => (IUndoableTransactionProcessor)State.Transactions;

        public TransactionFixture()
        {
            State = new TrackerState("test-fixture-state");
        }
    }

    [Xunit.CollectionDefinition(nameof(TransactionCollection))]
    public sealed class TransactionCollection : Xunit.ICollectionFixture<TransactionFixture> { }
}
