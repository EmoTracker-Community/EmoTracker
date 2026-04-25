using EmoTracker.Core.DataModel;
using System.Collections.Generic;

namespace EmoTracker.Data.Core.DataModel.SmokeTest
{
    /// <summary>
    /// Phase 1 smoke-test model type. Exercises the entire data-model-v2 stack at
    /// once: <see cref="ModelTypeBase"/>, <see cref="TransactableModelTypeBase"/>, the
    /// <c>[KVImmutable]</c> / <c>[KVMutable]</c> / <c>[KVTransactable]</c> generator
    /// emissions, the <c>[OnChanged]</c> callback, <see cref="IDeepCopyable"/>
    /// round-trips, COW <see cref="ModelTypeBase.Fork"/>, and the
    /// <see cref="ModelTypeBase.OnForked"/> hook.
    ///
    /// <para>
    /// This type is deliberately not connected to any other model in the data layer;
    /// nothing in the running app references it. It exists to be exercised by the
    /// generator-tests project, and to serve as a known-good reference if the
    /// data-model-v2 plumbing is ever suspect during later phases.
    /// </para>
    /// </summary>
    public partial class Phase1SmokeModelType : TransactableModelTypeBase
    {
        // -------- Definition data (KVImmutable) --------------------------------------

        /// <summary>Definition-time identifier for this kind of smoke test instance.</summary>
        [KVImmutable]
        public partial string DefinitionTag { get; }

        /// <summary>A reference-typed definition value; exercises IDeepCopyable round-trip.</summary>
        [KVImmutable]
        public partial Phase1SmokeNote SeedNote { get; }

        // -------- Per-state data (KVMutable, non-transactable) ----------------------

        [KVMutable]
        public partial string Label { get; set; }

        [KVMutable]
        [OnChanged(nameof(OnQuantityChanged))]
        public partial int Quantity { get; set; }

        // -------- Per-state data (KVTransactable, undo-tracked) ---------------------

        [KVTransactable]
        public partial bool Active { get; set; }

        [KVTransactable]
        [OnChanged(nameof(OnSelectedColorChanged))]
        public partial string SelectedColor { get; set; }

        // -------- Diagnostics for OnChanged callbacks -------------------------------

        /// <summary>Increments every time <see cref="Quantity"/>'s setter runs an OnChanged tick.</summary>
        public int QuantityChangedCount { get; private set; }

        /// <summary>Increments every time <see cref="SelectedColor"/>'s setter completes its transaction.</summary>
        public int SelectedColorChangedCount { get; private set; }

        /// <summary>The <see cref="OnForked"/> hook recorded source instance, for fork tests.</summary>
        public Phase1SmokeModelType ForkedFrom { get; private set; }

        protected void OnQuantityChanged()
        {
            QuantityChangedCount++;
        }

        protected void OnSelectedColorChanged()
        {
            SelectedColorChangedCount++;
        }

        // -------- Construction ------------------------------------------------------

        /// <summary>
        /// Constructs a fresh definition with seeded immutable values. Used by the test
        /// harness; in real model types the equivalent step is run by the pack-load code
        /// during ParseDataInternal.
        /// </summary>
        public static Phase1SmokeModelType CreateDefinition(string tag, Phase1SmokeNote seedNote)
        {
            var inst = new Phase1SmokeModelType();

            // Carry the auto-generated DefinitionId across into the seeded immutable
            // store: forks should observe the same DefinitionId no matter how many
            // times we replace ImmutableData on the original.
            var seed = new Dictionary<string, object>
            {
                { ModelTypeBase.DefinitionIdKey, inst.DefinitionId },
                { nameof(DefinitionTag), tag },
                { nameof(SeedNote), seedNote },
            };
            inst.ImmutableData = new ImmutableKeyValueStore(seed);
            return inst;
        }

        // -------- Fork --------------------------------------------------------------

        public override ModelTypeBase Fork()
        {
            var copy = new Phase1SmokeModelType();
            copy.InitializeAsForkOf(this);
            return copy;
        }

        /// <summary>Strongly-typed convenience wrapper around <see cref="Fork"/>.</summary>
        public Phase1SmokeModelType ForkAs() => (Phase1SmokeModelType)Fork();

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            ForkedFrom = source as Phase1SmokeModelType;
            // Reset OnChanged counters on the fork — this is per-instance state, not
            // copied through MutableData. (If a real model type wants per-state
            // counters, it stores them in MutableData via [KVMutable].)
            QuantityChangedCount = 0;
            SelectedColorChangedCount = 0;
        }
    }

    /// <summary>
    /// A reference-typed value used by the smoke test to verify IDeepCopyable round-trips.
    /// </summary>
    public sealed class Phase1SmokeNote : IDeepCopyable
    {
        public string Body { get; set; }
        public List<int> Numbers { get; set; }

        public object DeepCopy()
        {
            return new Phase1SmokeNote
            {
                Body = this.Body,
                Numbers = this.Numbers == null ? null : new List<int>(this.Numbers),
            };
        }
    }
}
