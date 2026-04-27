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
            var def = Phase1SmokeModelType.CreateDefinition("def-tag", note, _fix.State);

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
            var def = Phase1SmokeModelType.CreateDefinition("def", note, _fix.State);

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
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            def.Label = "alpha";
            Assert.Equal("alpha", def.Label);
            Assert.Contains("Label", changes);
        }

        [Fact]
        public void KVMutable_NoOpWrite_DoesNotRaiseINPC()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            def.Label = "alpha";

            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            def.Label = "alpha"; // identical, should be filtered by the generated equality check
            Assert.DoesNotContain("Label", changes);
        }

        [Fact]
        public void KVMutable_OnChanged_CallbackFiresOnEachWrite()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
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
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);

            using (_fix.Processor.OpenTransaction())
                def.Active = true;

            Assert.True(def.Active);

            _fix.Processor.Undo();
            Assert.False(def.Active);
        }

        [Fact]
        public void KVTransactable_OnChanged_CallbackFiresOnCommit()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            Assert.Equal(0, def.SelectedColorChangedCount);

            using (_fix.Processor.OpenTransaction())
                def.SelectedColor = "red";
            using (_fix.Processor.OpenTransaction())
                def.SelectedColor = "red"; // no-op, should not double-fire
            using (_fix.Processor.OpenTransaction())
                def.SelectedColor = "blue";

            Assert.Equal("blue", def.SelectedColor);
            Assert.Equal(2, def.SelectedColorChangedCount);
        }

        [Fact]
        public void KVTransactable_RaisesINPC()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            using (_fix.Processor.OpenTransaction())
                def.Active = true;

            Assert.Contains("Active", changes);
        }

        // ---------- KVOverridable ---------------------------------------------------

        [Fact]
        public void KVOverridable_ReturnsDefinitionDefault_WhenNoOverridePresent()
        {
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 42.0, defaultBackground: "navy", ownerState: _fix.State);

            // No override has been written; getter falls through to the __def key
            // in ImmutableData.
            Assert.Equal(42.0, def.Width);
            Assert.Equal("navy", def.Background);
        }

        [Fact]
        public void KVOverridable_SetWritesOverride_GetterSeesOverrideValue()
        {
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 42.0, defaultBackground: "navy", ownerState: _fix.State);

            def.Width = 100.0;
            Assert.Equal(100.0, def.Width);

            def.Background = "red";
            Assert.Equal("red", def.Background);
        }

        [Fact]
        public void KVOverridable_SetRaisesINPC_AndInvokesOnChanged()
        {
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 0.0, defaultBackground: null, ownerState: _fix.State);

            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            def.Width = 7.0;
            def.Background = "amber";

            Assert.Contains("Width", changes);
            Assert.Contains("Background", changes);
            Assert.Equal(1, def.BackgroundChangedCount);
        }

        [Fact]
        public void KVOverridable_ExplicitNullOverride_HonoredAsForceNull()
        {
            // Definition default is "navy"; explicitly overriding to null must be
            // honored (because the discriminator is MutableData.ContainsKey, not
            // "value is null"). This is the contract that distinguishes a layout
            // element saying "force no background" from "no override is set".
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 0.0, defaultBackground: "navy");
            Assert.Equal("navy", def.Background);

            def.Background = null;
            Assert.Null(def.Background);
        }

        [Fact]
        public void KVOverridable_RemoveOverride_FallsBackToDefinitionDefault()
        {
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 42.0, defaultBackground: "navy", ownerState: _fix.State);

            def.Width = 100.0;
            def.Background = "red";
            Assert.Equal(100.0, def.Width);
            Assert.Equal("red", def.Background);

            // Drop the per-state override; getter must fall back to ImmutableData.
            def.ClearWidthOverride();
            def.ClearBackgroundOverride();
            Assert.Equal(42.0, def.Width);
            Assert.Equal("navy", def.Background);
        }

        [Fact]
        public void KVOverridable_NoOpWrite_DoesNotRaiseINPC_OrInvokeOnChanged()
        {
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 0.0, defaultBackground: "navy");

            // First write installs the override and counts as a change relative to the
            // definition default.
            def.Background = "red";
            Assert.Equal(1, def.BackgroundChangedCount);

            // Second identical write must be a no-op: no INPC, no OnChanged.
            var changes = new List<string>();
            ((INotifyPropertyChanged)def).PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            def.Background = "red";

            Assert.DoesNotContain("Background", changes);
            Assert.Equal(1, def.BackgroundChangedCount);
        }

        [Fact]
        public void KVOverridable_Fork_OverrideIsCOWIsolated()
        {
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 42.0, defaultBackground: "navy", ownerState: _fix.State);
            def.Width = 100.0;

            // Fork inherits the override through copy-on-write.
            var fork = def.ForkAs(ForkTestHelpers.NewDestState());
            Assert.Equal(100.0, fork.Width);

            // Fork-side write shadows the parent without mutating it.
            fork.Width = 200.0;
            Assert.Equal(200.0, fork.Width);
            Assert.Equal(100.0, def.Width);
        }

        [Fact]
        public void KVOverridable_Fork_DefinitionDefaultIsSharedThroughImmutableData()
        {
            // Fork shares ImmutableData by reference, so the __def fallback survives
            // forks and stays consistent across the family.
            var def = Phase1SmokeModelType.CreateDefinition(
                "d", null, defaultWidth: 42.0, defaultBackground: "navy", ownerState: _fix.State);

            var fork = def.ForkAs(ForkTestHelpers.NewDestState());

            // Without a local override on the fork, both the source and fork report the
            // same definition default.
            Assert.Equal(42.0, fork.Width);
            Assert.Equal("navy", fork.Background);

            // Setting the override on one side does not change the other's fall-through
            // behavior (per-key COW on MutableData).
            fork.Width = 9999.0;
            Assert.Equal(42.0, def.Width);
            Assert.Equal(9999.0, fork.Width);
        }

        // ---------- DefinitionId / fork --------------------------------------------

        [Fact]
        public void DefinitionId_IsStableAcrossForks()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            var defId = def.DefinitionId;
            Assert.NotEqual(Guid.Empty, defId);

            var fork = def.ForkAs(ForkTestHelpers.NewDestState());
            Assert.Equal(defId, fork.DefinitionId);

            // And further forks of forks share the same DefinitionId.
            var forkOfFork = fork.ForkAs(ForkTestHelpers.NewDestState());
            Assert.Equal(defId, forkOfFork.DefinitionId);
        }

        [Fact]
        public void OnForked_HookRunsOnFork()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            Assert.Null(def.ForkedFrom);

            var fork = def.ForkAs(ForkTestHelpers.NewDestState());
            Assert.Same(def, fork.ForkedFrom);
        }

        [Fact]
        public void Fork_KVMutable_IsCOWIsolated()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            def.Label = "original";

            var fork = def.ForkAs(ForkTestHelpers.NewDestState());
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
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            using (_fix.Processor.OpenTransaction())
                def.Active = true;

            var fork = def.ForkAs(ForkTestHelpers.NewDestState());
            Assert.True(fork.Active);

            using (_fix.Processor.OpenTransaction())
                fork.Active = false;
            Assert.False(fork.Active);
            Assert.True(def.Active);
        }

        [Fact]
        public void Fork_OnChangedCounters_AreInstanceLocal()
        {
            var def = Phase1SmokeModelType.CreateDefinition("d", null, _fix.State);
            def.Quantity = 5;
            Assert.Equal(1, def.QuantityChangedCount);

            var fork = def.ForkAs(ForkTestHelpers.NewDestState());
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
