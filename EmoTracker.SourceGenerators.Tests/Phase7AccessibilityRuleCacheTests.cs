using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Sessions;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 7.2 verification: <see cref="AccessibilityRule"/>'s evaluation
    /// cache is now per-state — held on each <see cref="EmoTracker.Data.LocationDatabase"/>.
    /// These tests confirm:
    ///
    /// <list type="bullet">
    ///   <item>Two states evaluating the same rule expression with
    ///         different item state produce different results — no
    ///         cross-state cache contamination.</item>
    ///   <item>Forking a state with a populated cache produces a fork
    ///         whose cache is a snapshot of the source's at fork time.</item>
    ///   <item>Mutating an item in the fork (after clearing its cache)
    ///         re-evaluates against the fork's items only; the source's
    ///         cache is untouched.</item>
    ///   <item>The process-wide <see cref="AccessibilityRule.EnableCache"/>
    ///         toggle propagates to live per-state caches via the
    ///         <c>EnableCacheChanged</c> hook.</item>
    /// </list>
    /// </summary>
    [Collection(nameof(TransactionCollection))]
    public class Phase7AccessibilityRuleCacheTests
    {
        readonly TransactionFixture _fix;

        public Phase7AccessibilityRuleCacheTests(TransactionFixture fix)
        {
            _fix = fix;
        }

        // -------- Helpers ---------------------------------------------------

        // Build a state with a single ToggleItem providing the given code.
        // Returns (state, item) so callers can flip Active to drive
        // ProviderCountForCode("code") between 0 and 1.
        static (TrackerState state, ToggleItem item) MakeStateWithToggle(string code, bool initiallyActive = false)
        {
            var state = new TrackerState();
            var item = new ToggleItem { Name = code };
            item.OwnerState = state;
            // ToggleItem providers come from its mCodeProvider; we
            // configure via the standard add path. The test provides the
            // code by setting item.Active and using the provider chain
            // through ItemDatabase.ProviderCountForCode (which queries
            // every registered item's ProvidesCode).
            item.AddProvidedCodes(code);
            item.Active = initiallyActive;
            state.Items.RegisterItem(item);
            return (state, item);
        }

        // -------- Two-state isolation ---------------------------------------

        [Fact]
        public void TwoStates_RuleEvaluation_IsIsolated()
        {
            // Same rule expression, two states with different item state.
            // Each state's cache holds its own evaluation; the two states
            // see different AccessibilityLevels.
            var (stateA, itemA) = MakeStateWithToggle("hookshot", initiallyActive: true);
            var (stateB, itemB) = MakeStateWithToggle("hookshot", initiallyActive: false);

            var rule = new AccessibilityRule("hookshot");

            // First evaluation populates each state's cache.
            Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(stateA));
            Assert.Equal(AccessibilityLevel.None, rule.GetAccessibilityLevel(stateB));

            // Cache populated independently per state.
            Assert.Equal(1, stateA.Locations.RuleCache.Count);
            Assert.Equal(1, stateB.Locations.RuleCache.Count);

            // Flip stateB's item — but until the cache is cleared, the
            // cached "None" persists. This matches pre-Phase-7 behavior:
            // RefeshAccessibility clears the cache at the start of each
            // sweep before re-evaluating.
            itemB.Active = true;
            Assert.Equal(AccessibilityLevel.None, rule.GetAccessibilityLevel(stateB));

            // Clear stateB's cache (simulating the start of a refresh
            // sweep) — re-evaluation now sees the new state.
            stateB.Locations.RuleCache.Clear();
            Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(stateB));

            // stateA's cache was not disturbed by stateB's clear.
            Assert.Equal(1, stateA.Locations.RuleCache.Count);

            stateA.Dispose();
            stateB.Dispose();
        }

        // -------- Fork-time snapshot ----------------------------------------

        [Fact]
        public void Fork_DeepCopiesCache_FromSource()
        {
            // Pre-warm the source's cache by evaluating a rule, then fork.
            // The fork's cache contains the same entries as the source's
            // at fork time — fork starts pre-warmed, no cold-start cost.
            var (src, item) = MakeStateWithToggle("lamp", initiallyActive: true);
            var rule = new AccessibilityRule("lamp");
            Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(src));
            Assert.Equal(1, src.Locations.RuleCache.Count);

            var fork = src.Fork("fork");
            Assert.Equal(1, fork.Locations.RuleCache.Count);

            // Verify the fork's cache contains the lamp entry as a hit
            // (i.e. evaluating without clearing returns the cached value).
            Assert.True(fork.Locations.RuleCache.TryGet("lamp", out var level, out var count));
            Assert.Equal(AccessibilityLevel.Normal, level);
            Assert.Equal(1u, count);

            fork.Dispose();
            src.Dispose();
        }

        // -------- Fork independence -----------------------------------------

        [Fact]
        public void Fork_CacheMutations_DoNotAffectSource()
        {
            var (src, srcItem) = MakeStateWithToggle("bow", initiallyActive: true);
            var rule = new AccessibilityRule("bow");
            Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(src));

            var fork = src.Fork("fork");

            // Find the fork's bow item and flip it.
            ToggleItem forkItem = null;
            foreach (var i in fork.Items.Items)
            {
                if (i is ToggleItem t && t.DefinitionId == srcItem.DefinitionId)
                {
                    forkItem = t;
                    break;
                }
            }
            Assert.NotNull(forkItem);
            forkItem.Active = false;

            // Clear the fork's cache (simulating refresh sweep) and
            // re-evaluate.
            fork.Locations.RuleCache.Clear();
            Assert.Equal(AccessibilityLevel.None, rule.GetAccessibilityLevel(fork));

            // Source's cache still has its original entry — clearing
            // the fork's cache did NOT touch the source's.
            Assert.True(src.Locations.RuleCache.TryGet("bow", out var srcLevel, out var srcCount));
            Assert.Equal(AccessibilityLevel.Normal, srcLevel);
            Assert.Equal(1u, srcCount);

            // Source's evaluation still reports Normal.
            Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(src));

            fork.Dispose();
            src.Dispose();
        }

        // -------- Process-wide enable-cache propagation ---------------------

        [Fact]
        public void EnableCache_PropagatesToLivePerStateCaches()
        {
            // The process-wide AccessibilityRule.EnableCache flag fires
            // an event that LocationDatabase subscribes to so each live
            // state's per-state cache gets re-enabled / disabled in lock-
            // step. Flip it off, verify caches stop populating; flip back
            // on, verify they resume.
            var (state, item) = MakeStateWithToggle("hookshot", initiallyActive: true);
            var rule = new AccessibilityRule("hookshot");

            // Sanity: cache is populated by an evaluation while enabled.
            Assert.True(AccessibilityRule.EnableCache);
            Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(state));
            Assert.Equal(1, state.Locations.RuleCache.Count);

            // Flip global off → cache cleared, no further population.
            AccessibilityRule.EnableCache = false;
            try
            {
                Assert.Equal(0, state.Locations.RuleCache.Count);
                Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(state));
                Assert.Equal(0, state.Locations.RuleCache.Count);
            }
            finally
            {
                // Restore for other tests.
                AccessibilityRule.EnableCache = true;
            }

            // Re-enable: subsequent evaluations populate again.
            Assert.Equal(AccessibilityLevel.Normal, rule.GetAccessibilityLevel(state));
            Assert.Equal(1, state.Locations.RuleCache.Count);

            state.Dispose();
        }

        // -------- Null-state safety -----------------------------------------

        [Fact]
        public void GetAccessibilityLevel_NullState_TreatsCodesAsUnprovided()
        {
            // A rule evaluated outside any state (state = null) should
            // not throw. With no provider, codes are not provided, so a
            // non-sequence-breakable rule reports None.
            var rule = new AccessibilityRule("anything");
            Assert.Equal(AccessibilityLevel.None, rule.GetAccessibilityLevel(null));
        }
    }
}
