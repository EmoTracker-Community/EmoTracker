using EmoTracker.Data.Sessions;
using System.Collections.Generic;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 7.4 framework verification: <see cref="PackageInstance"/>
    /// notifies the installed <see cref="IStateLifecycleObserver"/> on
    /// state lifecycle events (CreateState / AdoptAsPrimary / RemoveState
    /// / Dispose). The actual <c>ExtensionManager</c> implementation
    /// lives in the EmoTracker app project and is exercised by runtime
    /// smoke; here we use a stub observer to confirm the data-layer
    /// contract.
    /// </summary>
    [Collection(nameof(TransactionCollection))]
    public class Phase7StateLifecycleTests
    {
        readonly TransactionFixture _fix;
        public Phase7StateLifecycleTests(TransactionFixture fix) { _fix = fix; }

        sealed class StubObserver : IStateLifecycleObserver
        {
            public List<TrackerState> Registered = new List<TrackerState>();
            public List<TrackerState> Unregistered = new List<TrackerState>();
            public void OnStateRegistered(TrackerState state) => Registered.Add(state);
            public void OnStateUnregistered(TrackerState state) => Unregistered.Add(state);
        }

        [Fact]
        public void CreateState_FiresOnStateRegistered()
        {
            var prev = StateLifecycle.Observer;
            var stub = new StubObserver();
            StateLifecycle.Observer = stub;
            try
            {
                using var pi = new PackageInstance();
                var s1 = pi.CreateState("a");
                Assert.Single(stub.Registered);
                Assert.Same(s1, stub.Registered[0]);

                var s2 = pi.CreateState("b");
                Assert.Equal(2, stub.Registered.Count);
                Assert.Same(s2, stub.Registered[1]);
            }
            finally
            {
                StateLifecycle.Observer = prev;
            }
        }

        [Fact]
        public void AdoptAsPrimary_FiresOnStateRegistered()
        {
            var prev = StateLifecycle.Observer;
            var stub = new StubObserver();
            StateLifecycle.Observer = stub;
            try
            {
                using var pi = new PackageInstance();
                var s = new TrackerState("adopted");
                pi.AdoptAsPrimary(s);

                Assert.Single(stub.Registered);
                Assert.Same(s, stub.Registered[0]);
            }
            finally
            {
                StateLifecycle.Observer = prev;
            }
        }

        [Fact]
        public void RemoveState_FiresOnStateUnregistered()
        {
            var prev = StateLifecycle.Observer;
            var stub = new StubObserver();
            StateLifecycle.Observer = stub;
            try
            {
                using var pi = new PackageInstance();
                var s = pi.CreateState("a");
                pi.RemoveState(s.Id);

                Assert.Single(stub.Unregistered);
                Assert.Same(s, stub.Unregistered[0]);
            }
            finally
            {
                StateLifecycle.Observer = prev;
            }
        }

        [Fact]
        public void DisposePackageInstance_FiresOnStateUnregistered_PerState()
        {
            var prev = StateLifecycle.Observer;
            var stub = new StubObserver();
            StateLifecycle.Observer = stub;
            try
            {
                var pi = new PackageInstance();
                var s1 = pi.CreateState("a");
                var s2 = pi.CreateState("b");
                pi.Dispose();

                Assert.Equal(2, stub.Unregistered.Count);
                Assert.Contains(s1, stub.Unregistered);
                Assert.Contains(s2, stub.Unregistered);
            }
            finally
            {
                StateLifecycle.Observer = prev;
            }
        }

        [Fact]
        public void NoObserverInstalled_LifecycleEventsAreNoOps()
        {
            // Resetting observer to null should not break PackageInstance
            // lifecycle methods.
            var prev = StateLifecycle.Observer;
            StateLifecycle.Observer = null;
            try
            {
                using var pi = new PackageInstance();
                var s = pi.CreateState("a");
                pi.RemoveState(s.Id);
                // No exception → pass.
            }
            finally
            {
                StateLifecycle.Observer = prev;
            }
        }
    }
}
