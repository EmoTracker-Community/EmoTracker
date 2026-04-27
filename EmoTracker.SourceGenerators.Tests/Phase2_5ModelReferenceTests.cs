using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel.SmokeTest;
using System;
using System.Collections.Generic;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 2.5 verification: <see cref="ModelReference{T}"/> framework and
    /// <see cref="IModelResolver"/> wiring on <see cref="ModelTypeBase"/>.
    ///
    /// <para>
    /// These tests use a counting fake resolver to assert caching behavior
    /// without depending on the global <see cref="ModelResolver.Current"/>
    /// (which the integration smoke covers separately via the running app).
    /// </para>
    /// </summary>
    public class Phase2_5ModelReferenceTests
    {
        // -------------------------------------------------- Counting fake resolver

        sealed class CountingResolver : IModelResolver
        {
            readonly Dictionary<Guid, object> mGraph = new Dictionary<Guid, object>();
            public int CallCount { get; private set; }

            public void Add(Guid id, object instance) { mGraph[id] = instance; }

            public T Resolve<T>(Guid definitionId) where T : class
            {
                CallCount++;
                if (mGraph.TryGetValue(definitionId, out var raw))
                    return raw as T;
                return null;
            }
        }

        // Holder that returns a specific resolver instance; isolates tests from
        // the static ModelResolver.Current.
        sealed class HolderWithResolver : ModelTypeBase
        {
            readonly IModelResolver mResolver;
            public HolderWithResolver(IModelResolver resolver) { mResolver = resolver; }
            public override IModelResolver GetModelResolver() => mResolver;
            public override ModelTypeBase Fork(ITrackerStateContext destOwnerState) => throw new NotImplementedException();
        }

        // -------------------------------------------------- Construction

        [Fact]
        public void ConstructWithTarget_CapturesDefinitionIdAndSeedsCache()
        {
            var resolver = new CountingResolver();
            var holder = new HolderWithResolver(resolver);
            var target = Phase1SmokeModelType.CreateDefinition("d", null);

            var reference = new ModelReference<Phase1SmokeModelType>(holder, target);

            Assert.Equal(target.DefinitionId, reference.DefinitionId);
            Assert.False(reference.IsEmpty);
            // Cache is seeded with target — reading Target should NOT call the resolver.
            var read = reference.Target;
            Assert.Same(target, read);
            Assert.Equal(0, resolver.CallCount);
        }

        [Fact]
        public void ConstructWithoutTarget_IsEmpty_AndTargetReturnsNull()
        {
            var resolver = new CountingResolver();
            var holder = new HolderWithResolver(resolver);

            var reference = new ModelReference<Phase1SmokeModelType>(holder);

            Assert.True(reference.IsEmpty);
            Assert.Null(reference.Target);
            Assert.Equal(0, resolver.CallCount);
        }

        // -------------------------------------------------- Caching

        [Fact]
        public void Target_FirstReadCallsResolver_SubsequentReadsHitCache()
        {
            var resolver = new CountingResolver();
            var holder = new HolderWithResolver(resolver);
            var target = Phase1SmokeModelType.CreateDefinition("d", null);
            resolver.Add(target.DefinitionId, target);

            var reference = new ModelReference<Phase1SmokeModelType>(holder, target.DefinitionId);

            // First read: hits the resolver.
            var first = reference.Target;
            Assert.Same(target, first);
            Assert.Equal(1, resolver.CallCount);

            // Second + third reads: hit the cache.
            var second = reference.Target;
            var third = reference.Target;
            Assert.Same(target, second);
            Assert.Same(target, third);
            Assert.Equal(1, resolver.CallCount);
        }

        [Fact]
        public void InvalidateCache_ForcesNextResolutionThroughResolver()
        {
            var resolver = new CountingResolver();
            var holder = new HolderWithResolver(resolver);
            var target = Phase1SmokeModelType.CreateDefinition("d", null);
            resolver.Add(target.DefinitionId, target);

            var reference = new ModelReference<Phase1SmokeModelType>(holder, target.DefinitionId);
            _ = reference.Target;
            Assert.Equal(1, resolver.CallCount);

            reference.InvalidateCache();

            _ = reference.Target;
            Assert.Equal(2, resolver.CallCount);
        }

        // -------------------------------------------------- Set / Clear

        [Fact]
        public void Set_T_ReplacesIdentityAndSeedsCache()
        {
            var resolver = new CountingResolver();
            var holder = new HolderWithResolver(resolver);
            var first = Phase1SmokeModelType.CreateDefinition("first", null);
            var second = Phase1SmokeModelType.CreateDefinition("second", null);

            var reference = new ModelReference<Phase1SmokeModelType>(holder, first);
            Assert.Equal(first.DefinitionId, reference.DefinitionId);

            reference.Set(second);
            Assert.Equal(second.DefinitionId, reference.DefinitionId);
            Assert.Same(second, reference.Target);
            Assert.Equal(0, resolver.CallCount);
        }

        [Fact]
        public void Set_Guid_DropsCache_ResolveOnNextRead()
        {
            var resolver = new CountingResolver();
            var holder = new HolderWithResolver(resolver);
            var target = Phase1SmokeModelType.CreateDefinition("d", null);
            resolver.Add(target.DefinitionId, target);

            var reference = new ModelReference<Phase1SmokeModelType>(holder, target);
            _ = reference.Target;
            Assert.Equal(0, resolver.CallCount);

            // Re-set via Guid: cache must be dropped, next read goes to resolver.
            reference.Set(target.DefinitionId);
            _ = reference.Target;
            Assert.Equal(1, resolver.CallCount);
        }

        [Fact]
        public void Clear_EmptiesReference()
        {
            var holder = new HolderWithResolver(new CountingResolver());
            var target = Phase1SmokeModelType.CreateDefinition("d", null);

            var reference = new ModelReference<Phase1SmokeModelType>(holder, target);
            reference.Clear();

            Assert.True(reference.IsEmpty);
            Assert.Equal(Guid.Empty, reference.DefinitionId);
            Assert.Null(reference.Target);
        }

        [Fact]
        public void Set_Null_EmptiesReference()
        {
            var holder = new HolderWithResolver(new CountingResolver());
            var target = Phase1SmokeModelType.CreateDefinition("d", null);

            var reference = new ModelReference<Phase1SmokeModelType>(holder, target);
            reference.Set((Phase1SmokeModelType)null);

            Assert.True(reference.IsEmpty);
            Assert.Null(reference.Target);
        }

        // -------------------------------------------------- ForFork

        [Fact]
        public void ForFork_ReturnsFreshReference_SameDefinitionId_NoCache_NewHolder()
        {
            var srcResolver = new CountingResolver();
            var dstResolver = new CountingResolver();
            var srcHolder = new HolderWithResolver(srcResolver);
            var dstHolder = new HolderWithResolver(dstResolver);
            var target = Phase1SmokeModelType.CreateDefinition("d", null);
            srcResolver.Add(target.DefinitionId, target);
            dstResolver.Add(target.DefinitionId, target);

            var srcRef = new ModelReference<Phase1SmokeModelType>(srcHolder, target);
            // Ensure srcRef has a cached target.
            _ = srcRef.Target;
            Assert.Equal(0, srcResolver.CallCount);

            // ForFork: same DefinitionId, no cache, holder = dstHolder.
            var forkRef = srcRef.ForFork(dstHolder);
            Assert.Equal(target.DefinitionId, forkRef.DefinitionId);
            Assert.NotSame(srcRef, forkRef);

            // First read on the fork's reference goes through dstHolder's resolver.
            _ = forkRef.Target;
            Assert.Equal(1, dstResolver.CallCount);
            Assert.Equal(0, srcResolver.CallCount); // src untouched
        }

        // -------------------------------------------------- DeepCopy

        [Fact]
        public void DeepCopy_ProducesFreshReference_SameDefinitionId_NoCache()
        {
            var resolver = new CountingResolver();
            var holder = new HolderWithResolver(resolver);
            var target = Phase1SmokeModelType.CreateDefinition("d", null);
            resolver.Add(target.DefinitionId, target);

            var original = new ModelReference<Phase1SmokeModelType>(holder, target);
            var copy = (ModelReference<Phase1SmokeModelType>)((IDeepCopyable)original).DeepCopy();

            Assert.NotSame(original, copy);
            Assert.Equal(original.DefinitionId, copy.DefinitionId);

            // The copy has no cache, so its first Target read calls the resolver.
            _ = copy.Target;
            Assert.Equal(1, resolver.CallCount);
        }

        // -------------------------------------------------- Equality

        [Fact]
        public void Equality_IsByDefinitionIdOnly()
        {
            var holderA = new HolderWithResolver(new CountingResolver());
            var holderB = new HolderWithResolver(new CountingResolver());
            var target = Phase1SmokeModelType.CreateDefinition("d", null);

            var refA = new ModelReference<Phase1SmokeModelType>(holderA, target);
            var refB = new ModelReference<Phase1SmokeModelType>(holderB, target);
            var refOther = new ModelReference<Phase1SmokeModelType>(holderA,
                Phase1SmokeModelType.CreateDefinition("other", null));

            Assert.True(refA.Equals(refB));
            Assert.True(refA == refB);
            Assert.Equal(refA.GetHashCode(), refB.GetHashCode());

            Assert.False(refA.Equals(refOther));
            Assert.True(refA != refOther);
        }

        // Phase 7.1+: the static ModelResolver.Current fallback was removed —
        // every model is expected to belong to a TrackerState, which IS its
        // resolver. Tests that exercised null-holder + ambient-resolver and
        // GetModelResolver default-to-Current are no longer applicable.
    }
}
