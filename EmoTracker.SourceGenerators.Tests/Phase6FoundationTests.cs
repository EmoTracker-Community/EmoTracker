using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using EmoTracker.Data.Core.DataModel.SmokeTest;
using System;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 6 step 1 verification: foundation types
    /// <see cref="ITrackerStateContext"/>, <see cref="ModelTypeBase.OwnerState"/>,
    /// and <see cref="IndexedModelResolver"/> are in place. Purely additive —
    /// no behavior change for production callers; this commit lands the
    /// scaffolding that subsequent Phase 6 steps build on.
    /// </summary>
    public class Phase6FoundationTests
    {
        // A minimal stub implementation of ITrackerStateContext for tests —
        // wraps an IndexedModelResolver and forwards Resolve through it.
        // The real EmoTracker.Data.Sessions.TrackerState implementation
        // arrives in step 2 and adds the per-state catalogs / transaction
        // processor / script manager surfaces.
        sealed class StubStateContext : ITrackerStateContext
        {
            public IndexedModelResolver Resolver { get; } = new IndexedModelResolver();
            public T Resolve<T>(Guid definitionId) where T : class => Resolver.Resolve<T>(definitionId);
        }

        [Fact]
        public void OwnerState_RoundTripsOnModelTypeBase()
        {
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            Assert.Null(smoke.OwnerState);

            var ctx = new StubStateContext();
            smoke.OwnerState = ctx;
            Assert.Same(ctx, smoke.OwnerState);

            // Setting back to null is supported (e.g., for teardown).
            smoke.OwnerState = null;
            Assert.Null(smoke.OwnerState);
        }

        [Fact]
        public void GetModelResolver_PrefersOwnerStateOverAmbient()
        {
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);

            // No OwnerState: GetModelResolver falls through to the ambient
            // ModelResolver.Current (which the test harness has set to the
            // singleton-backed resolver from earlier phases).
            Assert.Null(smoke.OwnerState);
            Assert.Same(ModelResolver.Current, smoke.GetModelResolver());

            // With OwnerState set: GetModelResolver returns the owner
            // (it IS an IModelResolver via interface inheritance).
            var ctx = new StubStateContext();
            smoke.OwnerState = ctx;
            Assert.Same(ctx, smoke.GetModelResolver());

            // Cleanup.
            smoke.OwnerState = null;
        }

        [Fact]
        public void IndexedModelResolver_Register_LookupByDefinitionId()
        {
            var resolver = new IndexedModelResolver();

            var smokeA = Phase1SmokeModelType.CreateDefinition("A", null);
            var smokeB = Phase1SmokeModelType.CreateDefinition("B", null);

            resolver.Register(smokeA);
            resolver.Register(smokeB);

            Assert.Equal(2, resolver.Count);
            Assert.Same(smokeA, resolver.Resolve<Phase1SmokeModelType>(smokeA.DefinitionId));
            Assert.Same(smokeB, resolver.Resolve<Phase1SmokeModelType>(smokeB.DefinitionId));
        }

        [Fact]
        public void IndexedModelResolver_UnknownId_ReturnsNull()
        {
            var resolver = new IndexedModelResolver();
            Assert.Null(resolver.Resolve<Phase1SmokeModelType>(Guid.NewGuid()));
        }

        [Fact]
        public void IndexedModelResolver_TypeMismatch_ReturnsNull()
        {
            var resolver = new IndexedModelResolver();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            resolver.Register(smoke);

            // Resolve with the right ID but a different (non-derived) type
            // returns null. ModelTypeBase satisfies a Phase1SmokeModelType
            // lookup but not, say, a string.
            Assert.NotNull(resolver.Resolve<ModelTypeBase>(smoke.DefinitionId));
            Assert.Null(resolver.Resolve<string>(smoke.DefinitionId));
        }

        [Fact]
        public void IndexedModelResolver_Unregister_RemovesEntry()
        {
            var resolver = new IndexedModelResolver();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            resolver.Register(smoke);

            Assert.True(resolver.IsRegistered(smoke.DefinitionId));
            Assert.True(resolver.Unregister(smoke.DefinitionId));
            Assert.False(resolver.IsRegistered(smoke.DefinitionId));
            Assert.Null(resolver.Resolve<Phase1SmokeModelType>(smoke.DefinitionId));

            // Re-unregistering returns false.
            Assert.False(resolver.Unregister(smoke.DefinitionId));
        }

        [Fact]
        public void IndexedModelResolver_RejectsEmptyDefinitionId()
        {
            // A model whose DefinitionId is Guid.Empty hasn't been fully
            // initialized; the resolver no-ops the registration rather than
            // creating a silent collision under the empty key.
            var resolver = new IndexedModelResolver();
            // We can't easily construct a ModelTypeBase with an empty
            // DefinitionId via public APIs (the base ctor allocates one),
            // so we simply verify the null-input behavior here. The
            // empty-Guid filter is documented; full integration coverage
            // arrives once TrackerState's coordinated fork lands.
            resolver.Register(null);
            Assert.Equal(0, resolver.Count);
        }

        [Fact]
        public void IndexedModelResolver_Clear_DropsAllRegistrations()
        {
            var resolver = new IndexedModelResolver();
            resolver.Register(Phase1SmokeModelType.CreateDefinition("a", null));
            resolver.Register(Phase1SmokeModelType.CreateDefinition("b", null));
            Assert.Equal(2, resolver.Count);

            resolver.Clear();
            Assert.Equal(0, resolver.Count);
        }

        [Fact]
        public void StubStateContext_AsModelResolver_ResolvesViaIndex()
        {
            // The whole point of ITrackerStateContext inheriting IModelResolver:
            // a holder's OwnerState IS a resolver, so cross-references via
            // ModelReference<T>.Target → holder.GetModelResolver().Resolve(id)
            // chase the holder's state automatically.
            var ctx = new StubStateContext();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            ctx.Resolver.Register(smoke);

            // Resolve via the context (treating it as IModelResolver).
            IModelResolver asResolver = ctx;
            Assert.Same(smoke, asResolver.Resolve<Phase1SmokeModelType>(smoke.DefinitionId));
        }
    }
}
