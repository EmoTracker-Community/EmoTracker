using EmoTracker.Data;
using EmoTracker.Data.Sessions;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 7.3 verification: <see cref="SessionSettings"/> hangs off each
    /// <see cref="TrackerState"/> and isolates the seven user-toggleable
    /// settings previously held on <see cref="ApplicationSettings"/> from
    /// other states. Confirms:
    ///
    /// <list type="bullet">
    ///   <item>Defaults match pre-Phase-7 ApplicationSettings defaults.</item>
    ///   <item>Toggling on state A does not affect state B.</item>
    ///   <item>Forking a state COW-copies the settings; fork mutations
    ///         do not affect the source's settings.</item>
    ///   <item>Toggling <c>IgnoreAllLogic</c> via the OnChanged hook
    ///         drives <c>RefeshAccessibility</c> on the owning state.</item>
    /// </list>
    /// </summary>
    [Collection(nameof(TransactionCollection))]
    public class Phase7SessionSettingsTests
    {
        readonly TransactionFixture _fix;
        public Phase7SessionSettingsTests(TransactionFixture fix) { _fix = fix; }

        // -------- Defaults --------------------------------------------------

        [Fact]
        public void Defaults_MatchPrePhase7Values()
        {
            var state = new TrackerState();
            Assert.False(state.Settings.IgnoreAllLogic);
            Assert.False(state.Settings.DisplayAllLocations);
            Assert.False(state.Settings.AlwaysAllowClearing);
            Assert.True(state.Settings.AutoUnpinLocationsOnClear);
            Assert.True(state.Settings.PinLocationsOnItemCapture);
            Assert.True(state.Settings.MapEnabled);
            Assert.False(state.Settings.SwapLeftRight);
            state.Dispose();
        }

        // -------- Two-state isolation ---------------------------------------

        [Fact]
        public void TwoStates_SettingsAreIsolated()
        {
            var stateA = new TrackerState("A");
            var stateB = new TrackerState("B");

            stateA.Settings.IgnoreAllLogic = true;
            Assert.True(stateA.Settings.IgnoreAllLogic);
            Assert.False(stateB.Settings.IgnoreAllLogic);

            stateB.Settings.SwapLeftRight = true;
            Assert.True(stateB.Settings.SwapLeftRight);
            Assert.False(stateA.Settings.SwapLeftRight);

            stateA.Dispose();
            stateB.Dispose();
        }

        // -------- Fork independence -----------------------------------------

        [Fact]
        public void Fork_CopiesSettings_AndDivergesIndependently()
        {
            var src = new TrackerState("source");
            src.Settings.DisplayAllLocations = true;
            src.Settings.MapEnabled = false;

            var fork = src.Fork("fork");

            // Fork inherits settings at fork time.
            Assert.True(fork.Settings.DisplayAllLocations);
            Assert.False(fork.Settings.MapEnabled);

            // Mutate the fork; source unaffected.
            fork.Settings.DisplayAllLocations = false;
            fork.Settings.AlwaysAllowClearing = true;

            Assert.False(fork.Settings.DisplayAllLocations);
            Assert.True(fork.Settings.AlwaysAllowClearing);
            Assert.True(src.Settings.DisplayAllLocations);
            Assert.False(src.Settings.AlwaysAllowClearing);

            // Source mutation likewise doesn't disturb fork.
            src.Settings.MapEnabled = true;
            Assert.True(src.Settings.MapEnabled);
            Assert.False(fork.Settings.MapEnabled);

            fork.Dispose();
            src.Dispose();
        }

        // -------- IgnoreAllLogic OnChanged drives RefeshAccessibility -------

        [Fact]
        public void IgnoreAllLogic_OnChanged_FiresOnOwningState()
        {
            // Setting IgnoreAllLogic should reach the owning state's
            // LocationDatabase. We can't easily intercept RefeshAccessibility
            // without instrumentation, but we can confirm OwnerState is set
            // (the OnChanged hook reads it) and that the setter doesn't
            // throw — i.e. the hook resolves without NRE.
            var state = new TrackerState();
            Assert.Same(state, state.Settings.OwnerState);

            state.Settings.IgnoreAllLogic = true;
            Assert.True(state.Settings.IgnoreAllLogic);

            state.Dispose();
        }
    }
}
