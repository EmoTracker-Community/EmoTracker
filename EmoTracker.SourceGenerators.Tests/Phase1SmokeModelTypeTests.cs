using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel.SmokeTest;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// End-to-end behavior tests for the Phase 1 stack: <see cref="ModelTypeBase"/>,
    /// <see cref="TransactableModelTypeBase"/>, the source-generated property
    /// implementations on <see cref="Phase1SmokeModelType"/>, copy-on-write forking,
    /// and the <see cref="IDeepCopyable"/> boundary.
    ///
    /// The behavior of the source generator is exercised indirectly here — every
    /// property access on the smoke type runs generator-emitted code. Failures in
    /// these tests can mean either a runtime bug in the new infrastructure or a
    /// regression in the generator output.
    /// </summary>
    [Collection(nameof(TransactionCollection))]
    public class Phase1SmokeModelTypeTests
    {
        readonly TransactionFixture _fix;

        public Phase1SmokeModelTypeTests(TransactionFixture fix)
        {
            _fix = fix;
        }

        // ---------- KVImmutable -----------------------------------------------------

        [Fact]
        public void KVImmutable_ReadsFromImmutableDataAfterDefinitionSeed()
        {
            var note = new Phase1SmokeNote { Body = "hi", Numbers = new List<int> { 1, 2 } };
            var def = Phase1SmokeModelType.CreateDefinition("def-tag", note);

            Assert.Equal("def-tag", def.DefinitionTag);

            var seenNote = def.SeedNote;
            Assert.NotNull(seenNote);
            Assert.Equal("hi", seenNote.Body);
            Assert.Equal(new[] { 1, 2 }, seenNote.Numbers);
        }

        [Fact]
        public void KVImmutable_DeepCopiesAcrossReads_NotAliased()
        {
            var note = new Phase1SmokeNote { Body = "hi", Numbers = new List<int> { 1, 2 } };
            var def = Phase1SmokeModelType.CreateDefinition("def", note);

            // Two reads return distinct objects (deep-copied at the boundary).
            var a = def.SeedNote;
            var b = def.SeedNote;
            Assert.NotSame(a, b);

            // Mutating the read result does not pollute future reads.
            a.Body = "changed";
            a.Numbers.Add(99);
            Assert.Equal("hi", def.SeedNote.Body);
            Assert.Equal(new[] { 1, 2 }, def.SeedNote.Numbers);
        }

        // ---------- KVMutable -------------------------------------------------------

        [Fact]
        public void KVMutable_RoundTrip_RaisesINPC()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            def.Label = "alpha";
            Assert.Equal("alpha", def.Label);
            Assert.Contains("Label", changes);
        }

        [Fact]
        public void KVMutable_NoOpWrite_DoesNotRaiseINPC()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            def.Label = "alpha";

            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            def.Label = "alpha"; // identical, should be filtered by the generated equality check
            Assert.DoesNotContain("Label", changes);
        }

        [Fact]
        public void KVMutable_OnChanged_CallbackFiresOnEachWrite()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            Assert.Equal(0, def.QuantityChangedCount);

            def.Quantity = 1;
            def.Quantity = 2;
            def.Quantity = 2; // no-op, should not invoke the callback
            def.Quantity = 3;

            Assert.Equal(3, def.QuantityChangedCount);
            Assert.Equal(3, def.Quantity);
        }

        // ---------- KVTransactable --------------------------------------------------

        [Fact]
        public void KVTransactable_RoundTrip_IsUndoable()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);

            using (TransactionProcessor.Current.OpenTransaction())
                def.Active = true;

            Assert.True(def.Active);

            ((IUndoableTransactionProcessor)TransactionProcessor.Current).Undo();
            Assert.False(def.Active);
        }

        [Fact]
        public void KVTransactable_OnChanged_CallbackFiresOnCommit()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            Assert.Equal(0, def.SelectedColorChangedCount);

            using (TransactionProcessor.Current.OpenTransaction())
                def.SelectedColor = "red";
            using (TransactionProcessor.Current.OpenTransaction())
                def.SelectedColor = "red"; // no-op, should not double-fire
            using (TransactionProcessor.Current.OpenTransaction())
                def.SelectedColor = "blue";

            Assert.Equal("blue", def.SelectedColor);
            Assert.Equal(2, def.SelectedColorChangedCount);
        }

        [Fact]
        public void KVTransactable_RaisesINPC()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            using (TransactionProcessor.Current.OpenTransaction())
                def.Active = true;

            Assert.Contains("Active", changes);
        }

        // ---------- DefinitionId / fork --------------------------------------------

        [Fact]
        public void DefinitionId_IsStableAcrossForks()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            var defId = def.DefinitionId;
            Assert.NotEqual(Guid.Empty, defId);

            var fork = def.ForkAs();
            Assert.Equal(defId, fork.DefinitionId);

            // And further forks of forks share the same DefinitionId.
            var forkOfFork = fork.ForkAs();
            Assert.Equal(defId, forkOfFork.DefinitionId);
        }

        [Fact]
        public void OnForked_HookRunsOnFork()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            Assert.Null(def.ForkedFrom);

            var fork = def.ForkAs();
            Assert.Same(def, fork.ForkedFrom);
        }

        [Fact]
        public void Fork_KVMutable_IsCOWIsolated()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            def.Label = "original";

            var fork = def.ForkAs();
            // Reads through to the parent for a not-yet-locally-set key.
            Assert.Equal("original", fork.Label);

            // Writing on the fork shadows the parent without affecting it.
            fork.Label = "fork-only";
            Assert.Equal("fork-only", fork.Label);
            Assert.Equal("original", def.Label);

            // The reverse direction also stays isolated.
            def.Label = "newly-original";
            Assert.Equal("newly-original", def.Label);
            Assert.Equal("fork-only", fork.Label);
        }

        [Fact]
        public void Fork_KVTransactable_IsCOWIsolated()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            using (TransactionProcessor.Current.OpenTransaction())
                def.Active = true;

            var fork = def.ForkAs();
            Assert.True(fork.Active);

            using (TransactionProcessor.Current.OpenTransaction())
                fork.Active = false;
            Assert.False(fork.Active);
            Assert.True(def.Active);
        }

        [Fact]
        public void Fork_OnChangedCounters_AreInstanceLocal()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null);
            def.Quantity = 5;
            Assert.Equal(1, def.QuantityChangedCount);

            var fork = def.ForkAs();
            // Counters reset on the fork (per-instance state).
            Assert.Equal(0, fork.QuantityChangedCount);
            // Original is unaffected.
            Assert.Equal(1, def.QuantityChangedCount);
        }

        // ---------- IDeepCopyable boundary -----------------------------------------

        [Fact]
        public void IDeepCopyable_OnReferenceTypeStoredInImmutableData()
        {
            var note = new Phase1SmokeNote { Body = "imm", Numbers = new List<int> { 7 } };
            var def = Phase1SmokeModelType.CreateDefinition("d", note);

            // Mutating the original outside the store does not propagate inside.
            note.Body = "mutated";
            note.Numbers.Add(99);

            Assert.Equal("imm", def.SeedNote.Body);
            Assert.Equal(new[] { 7 }, def.SeedNote.Numbers);
        }

        // ---------- MutableKeyValueStore: tombstone semantics ----------------------

        [Fact]
        public void MutableKeyValueStore_RemoveOnFork_TombstonesParentKey()
        {
            var parent = new MutableKeyValueStore();
            parent.SetValue("k", "from-parent");

            var child = new MutableKeyValueStore(parent);
            // Pre-removal: child reads through to parent.
            Assert.Equal("from-parent", child.GetValue<string>("k", "absent"));

            child.Remove("k");
            // Tombstone short-circuits the walk.
            Assert.Equal("absent", child.GetValue<string>("k", "absent"));
            // Parent is unaffected.
            Assert.Equal("from-parent", parent.GetValue<string>("k", "absent"));
        }

        [Fact]
        public void MutableKeyValueStore_FlattenAfterRemove_DropsTombstone()
        {
            var parent = new MutableKeyValueStore();
            parent.SetValue("k", "from-parent");

            var child = new MutableKeyValueStore(parent);
            child.Remove("k");

            // AsImmutable triggers a Flatten on a copy; the resulting snapshot must
            // not surface the tombstone, and must not see the parent value.
            var snapshot = child.AsImmutable();
            Assert.False(snapshot.ContainsKey("k"));
            Assert.Equal("absent", snapshot.GetValue<string>("k", "absent"));
        }

        [Fact]
        public void MutableKeyValueStore_SetValue_RejectsNonCopyableReferenceType()
        {
            var store = new MutableKeyValueStore();
            // List<int> is a reference type and does not implement IDeepCopyable; it
            // must be rejected at the store boundary so callers can't accidentally
            // share mutable references between forks.
            Assert.Throws<InvalidOperationException>(() => store.SetValue("k", new List<int>()));
        }

        // ---------- AsImmutable: round-trip definition data ------------------------

        [Fact]
        public void AsImmutable_FlattenedSnapshot_ContainsAllInheritedKeys()
        {
            var parent = new MutableKeyValueStore();
            parent.SetValue("a", 1);
            parent.SetValue("b", 2);

            var child = new MutableKeyValueStore(parent);
            child.SetValue("b", 22);
            child.SetValue("c", 3);

            var snapshot = child.AsImmutable();
            Assert.Equal(1, snapshot.GetValue<int>("a", -1));
            Assert.Equal(22, snapshot.GetValue<int>("b", -1));
            Assert.Equal(3, snapshot.GetValue<int>("c", -1));
        }
    }
}
