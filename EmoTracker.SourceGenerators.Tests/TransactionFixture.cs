using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Installs a real <see cref="LocalTransactionProcessorWithUndo"/> as the global
    /// <see cref="TransactionProcessor"/> for the duration of the test process so
    /// transactable-property tests have a working write/undo path. xUnit collects
    /// fixture types annotated as a <c>CollectionDefinition</c>; tests that need it
    /// reference the <see cref="TransactionCollection"/> in <c>[Collection]</c>.
    ///
    /// The processor is shared because <see cref="TransactionProcessor"/> is itself
    /// a static singleton — replacing it under tests running in parallel would race.
    /// </summary>
    public sealed class TransactionFixture
    {
        public IUndoableTransactionProcessor Processor { get; }

        public TransactionFixture()
        {
            // Replace whatever (likely null) processor is currently installed.
            var local = new LocalTransactionProcessorWithUndo();
            TransactionProcessor.SetTransactionProcessor(local);
            Processor = local;
        }
    }

    [Xunit.CollectionDefinition(nameof(TransactionCollection))]
    public sealed class TransactionCollection : Xunit.ICollectionFixture<TransactionFixture> { }
}
