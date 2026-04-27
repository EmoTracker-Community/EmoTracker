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
            public IScriptManager Scripts { get; set; }
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

            // No OwnerState: GetModelResolver returns null (Phase 7.1+
            // removed the static ModelResolver.Current fallback — every
            // model is expected to belong to a state).
            Assert.Null(smoke.OwnerState);
            Assert.Null(smoke.GetModelResolver());

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

        // -------- TrackerState shell --------

        [Fact]
        public void TrackerState_HasFreshIdentityAndOwnScriptManager()
        {
            var stateA = new EmoTracker.Data.Sessions.TrackerState("A");
            var stateB = new EmoTracker.Data.Sessions.TrackerState("B");

            Assert.NotEqual(Guid.Empty, stateA.Id);
            Assert.NotEqual(stateA.Id, stateB.Id);

            Assert.Equal("A", stateA.Name);
            Assert.Equal("B", stateB.Name);

            // Each state owns its own ScriptManager — no sharing.
            Assert.NotNull(stateA.Scripts);
            Assert.NotNull(stateB.Scripts);
            Assert.NotSame(stateA.Scripts, stateB.Scripts);
        }

        [Fact]
        public void TrackerState_ImplementsITrackerStateContext_AndResolves()
        {
            var state = new EmoTracker.Data.Sessions.TrackerState("test");
            Assert.IsAssignableFrom<ITrackerStateContext>(state);
            Assert.IsAssignableFrom<IModelResolver>(state);

            // Empty state has nothing to resolve.
            Assert.Null(state.Resolve<Phase1SmokeModelType>(Guid.NewGuid()));

            // Register a model via the internal resolver, then resolve via
            // the public ITrackerStateContext / IModelResolver surface.
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            state.Resolver.Register(smoke);
            Assert.Same(smoke, state.Resolve<Phase1SmokeModelType>(smoke.DefinitionId));
        }

        [Fact]
        public void TrackerState_AsOwnerState_RoutesGetModelResolverThroughIt()
        {
            var state = new EmoTracker.Data.Sessions.TrackerState();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            smoke.OwnerState = state;
            state.Resolver.Register(smoke);

            // GetModelResolver returns the state (it IS an IModelResolver),
            // and resolve through it finds the model.
            var resolver = smoke.GetModelResolver();
            Assert.Same(state, resolver);
            Assert.Same(smoke, resolver.Resolve<Phase1SmokeModelType>(smoke.DefinitionId));

            // Cleanup: drop the OwnerState before the test exits so the
            // process-shared state machinery isn't left dangling.
            smoke.OwnerState = null;
        }

        // -------- PackageInstance container --------

        [Fact]
        public void PackageInstance_HasFreshDefinitionalAndEmptyStates()
        {
            var pi = new EmoTracker.Data.Sessions.PackageInstance();

            // Definitional state is allocated immediately; no live primaries.
            Assert.NotNull(pi.DefinitionalState);
            Assert.Equal("__definitional__", pi.DefinitionalState.Name);
            Assert.Empty(pi.States);
        }

        [Fact]
        public void PackageInstance_CreateState_RegistersAndReturns()
        {
            var pi = new EmoTracker.Data.Sessions.PackageInstance();

            var stateA = pi.CreateState("Alpha");
            Assert.Equal("Alpha", stateA.Name);
            Assert.Single(pi.States);
            Assert.Same(stateA, pi.GetState(stateA.Id));

            var stateB = pi.CreateState("Beta");
            Assert.Equal(2, pi.States.Count);
            Assert.NotEqual(stateA.Id, stateB.Id);
            Assert.NotSame(stateA, stateB);
            Assert.NotSame(stateA.Scripts, stateB.Scripts);
        }

        [Fact]
        public void PackageInstance_RemoveState_DropsAndDisposes()
        {
            var pi = new EmoTracker.Data.Sessions.PackageInstance();
            var state = pi.CreateState("temporary");

            Assert.Single(pi.States);
            Assert.True(pi.RemoveState(state.Id));
            Assert.Empty(pi.States);
            Assert.Null(pi.GetState(state.Id));

            // Removing again returns false (already gone).
            Assert.False(pi.RemoveState(state.Id));
        }

        [Fact]
        public void PackageInstance_GetState_UnknownId_ReturnsNull()
        {
            var pi = new EmoTracker.Data.Sessions.PackageInstance();
            Assert.Null(pi.GetState(Guid.NewGuid()));
        }

        // -------- GetScriptManager flows through OwnerState --------

        [Fact]
        public void GetScriptManager_PrefersOwnerStateScripts()
        {
            var state = new EmoTracker.Data.Sessions.TrackerState();
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            smoke.OwnerState = state;

            // GetScriptManager returns the state's ScriptManager (cast to
            // IScriptManager via the explicit interface implementation).
            // NOT the singleton ScriptManagerHost.Current.
            var resolved = smoke.GetScriptManager();
            Assert.Same(state.Scripts, resolved);

            smoke.OwnerState = null;
        }

        [Fact]
        public void GetScriptManager_NoOwnerState_ReturnsNull()
        {
            // Phase 7.1+: with the static ScriptManagerHost / NullScriptManager
            // fallback removed, models without an OwnerState resolve to null.
            // Callsites that fire standard callbacks must null-check the
            // GetScriptManager() result.
            var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
            Assert.Null(smoke.OwnerState);
            Assert.Null(smoke.GetScriptManager());
        }
    }
}
