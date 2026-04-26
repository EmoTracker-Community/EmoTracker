using EmoTracker.Data;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Sessions;
using Xunit;

// Phase 6 step 11: this test file directly exercises the (now-obsolete)
// static catalog surface to verify the static-current-instance plumbing.
// Once that plumbing is fully retired (post-Tracker.Reload refactor),
// these tests are deleted along with the static surface.
#pragma warning disable CS0618

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 6 step 5 verification: <see cref="ItemDatabase"/>,
    /// <see cref="LocationDatabase"/>, <see cref="MapDatabase"/>, and
    /// <see cref="LayoutManager"/> are no longer strict singletons —
    /// each <see cref="TrackerState"/> owns its own. The static
    /// <c>Instance</c> alias still works for pre-Phase-6 callers; the
    /// new <c>Current</c> + <c>SetCurrent</c> surface lets Phase 6 step 7
    /// flip between states.
    /// </summary>
    public class Phase6PerStateCatalogTests
    {
        [Fact]
        public void ItemDatabase_Constructible_AndIndependent()
        {
            // Each ItemDatabase is independent; mutating one doesn't
            // disturb the other. The pre-Phase-6 Singleton<T> base only
            // exposed Instance; the new pattern allows direct
            // construction so each state can hold its own.
            var dbA = new ItemDatabase();
            var dbB = new ItemDatabase();
            Assert.NotSame(dbA, dbB);
            Assert.Empty(dbA.Items);
            Assert.Empty(dbB.Items);
        }

        [Fact]
        public void ItemDatabase_InstanceAliasesCurrent_LazyCreates()
        {
            // Verify the back-compat Instance/Current shim. Pre-Phase-6
            // callers that wrote ItemDatabase.Instance keep working.
            var first = ItemDatabase.Instance;
            Assert.NotNull(first);
            Assert.Same(ItemDatabase.Current, first);

            // Setting Current to null lets the next access lazy-recreate.
            ItemDatabase.SetCurrent(null);
            var second = ItemDatabase.Current;
            Assert.NotNull(second);
            // Either the same (if SetCurrent's null was respected with no
            // re-init in between) or a fresh instance. Either way, Instance
            // still aliases Current.
            Assert.Same(second, ItemDatabase.Instance);
        }

        [Fact]
        public void LocationDatabase_Constructible_AndIndependent()
        {
            var dbA = new LocationDatabase();
            var dbB = new LocationDatabase();
            Assert.NotSame(dbA, dbB);
            // Each has empty collections by default.
            Assert.Empty(dbA.AllLocations);
            Assert.Empty(dbB.AllLocations);
        }

        [Fact]
        public void MapDatabase_Constructible_AndIndependent()
        {
            var dbA = new MapDatabase();
            var dbB = new MapDatabase();
            Assert.NotSame(dbA, dbB);
            Assert.Empty(dbA.Maps);
        }

        [Fact]
        public void LayoutManager_Constructible_AndIndependent()
        {
            var lmA = new LayoutManager();
            var lmB = new LayoutManager();
            Assert.NotSame(lmA, lmB);
            // Empty registries on construction — FindLayout/FindElement return null.
            Assert.Null(lmA.FindLayout("foo"));
            Assert.Null(lmA.FindElement("bar"));
        }

        [Fact]
        public void TrackerState_OwnsAllFourCatalogs_FreshInstancesPerState()
        {
            var stateA = new TrackerState("A");
            var stateB = new TrackerState("B");

            Assert.NotNull(stateA.Items);
            Assert.NotNull(stateA.Locations);
            Assert.NotNull(stateA.Maps);
            Assert.NotNull(stateA.Layouts);

            // Each state has its OWN catalog instances — no sharing.
            Assert.NotSame(stateA.Items, stateB.Items);
            Assert.NotSame(stateA.Locations, stateB.Locations);
            Assert.NotSame(stateA.Maps, stateB.Maps);
            Assert.NotSame(stateA.Layouts, stateB.Layouts);
        }

        [Fact]
        public void SuspendRefreshScope_TargetCapturedAtConstruction()
        {
            // The audit fix: SuspendRefreshScope captures its target at
            // construction so a SetCurrent swap mid-scope doesn't push
            // on one db and pop on another.
            var dbA = new LocationDatabase();
            var dbB = new LocationDatabase();

            using (new LocationDatabase.SuspendRefreshScope(dbA))
            {
                Assert.True(dbA.SuspendRefresh);
                Assert.False(dbB.SuspendRefresh);
            }

            Assert.False(dbA.SuspendRefresh);
            Assert.False(dbB.SuspendRefresh);
        }

        [Fact]
        public void SuspendRefreshScope_DefaultCtor_TargetsCurrent()
        {
            // The pre-Phase-6 single-arg ctor shape is preserved: a
            // parameterless construction targets whatever Current is at
            // construction time. Existing callsites unchanged.
            var prior = LocationDatabase.Current;
            try
            {
                var db = new LocationDatabase();
                LocationDatabase.SetCurrent(db);

                using (new LocationDatabase.SuspendRefreshScope())
                    Assert.True(db.SuspendRefresh);

                Assert.False(db.SuspendRefresh);
            }
            finally
            {
                LocationDatabase.SetCurrent(prior);
            }
        }

        [Fact]
        public void SetCurrent_RoutesInstanceToNewTarget()
        {
            // SetCurrent on each catalog flips what Instance returns. Phase
            // 6 step 7 uses this on state-switch.
            var prior = ItemDatabase.Current;
            try
            {
                var newDb = new ItemDatabase();
                ItemDatabase.SetCurrent(newDb);
                Assert.Same(newDb, ItemDatabase.Instance);
                Assert.Same(newDb, ItemDatabase.Current);
            }
            finally
            {
                ItemDatabase.SetCurrent(prior);
            }
        }

        [Fact]
        public void TrackerState_Dispose_DoesNotThrow()
        {
            // Step 5 audit: Dispose intentionally avoids the catalog
            // teardown until step 7 introduces proper Dispose() on each
            // catalog. Verify it doesn't blow up.
            var state = new TrackerState();
            state.Dispose();
            // Idempotent — calling again should also not throw.
            state.Dispose();
        }
    }
}
