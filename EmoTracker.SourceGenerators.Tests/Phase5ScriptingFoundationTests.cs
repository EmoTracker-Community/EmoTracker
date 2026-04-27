using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel.SmokeTest;
using System.Collections.Generic;
using Xunit;

// Phase 6 step 11: this test file directly exercises the (now-obsolete)
// ScriptManager.Current accessor to verify the static-current pattern.
#pragma warning disable CS0618

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Phase 5 step 1 verification: <see cref="ScriptManager"/> is no longer
    /// strictly singleton-only (now a regular instantiable class with a
    /// static-current accessor), <see cref="IScriptManager"/> /
    /// <see cref="ScriptManagerHost"/> are in place in Core, and
    /// <see cref="ModelTypeBase.GetScriptManager"/> resolves through them.
    /// </summary>
    public class Phase5ScriptingFoundationTests
    {
        sealed class StubScriptManager : IScriptManager
        {
            public List<StandardCallback> Calls = new List<StandardCallback>();
            public void InvokeStandardCallback(StandardCallback callback, params object[] args)
            {
                Calls.Add(callback);
            }
        }

        [Fact]
        public void StandardCallback_EnumValuesMatchScriptManagerNestedEnum()
        {
            // The Core enum and the Data-side ScriptManager.StandardCallback
            // nested enum must have identical values so the explicit
            // IScriptManager.InvokeStandardCallback shim's cast is a no-op.
            // (Adding new callbacks needs to keep both lists in sync until
            // Phase 6 retires the nested enum.)
            Assert.Equal((int)StandardCallback.AccessibilityUpdating, (int)EmoTracker.Data.ScriptManager.StandardCallback.AccessibilityUpdating);
            Assert.Equal((int)StandardCallback.AccessibilityUpdated, (int)EmoTracker.Data.ScriptManager.StandardCallback.AccessibilityUpdated);
            Assert.Equal((int)StandardCallback.StartLoadingSaveFile, (int)EmoTracker.Data.ScriptManager.StandardCallback.StartLoadingSaveFile);
            Assert.Equal((int)StandardCallback.FinishLoadingSaveFile, (int)EmoTracker.Data.ScriptManager.StandardCallback.FinishLoadingSaveFile);
            Assert.Equal((int)StandardCallback.PackReady, (int)EmoTracker.Data.ScriptManager.StandardCallback.PackReady);
            Assert.Equal((int)StandardCallback.AutoTrackerStarted, (int)EmoTracker.Data.ScriptManager.StandardCallback.AutoTrackerStarted);
            Assert.Equal((int)StandardCallback.AutoTrackerStopped, (int)EmoTracker.Data.ScriptManager.StandardCallback.AutoTrackerStopped);
            Assert.Equal((int)StandardCallback.LocationUpdating, (int)EmoTracker.Data.ScriptManager.StandardCallback.LocationUpdating);
            Assert.Equal((int)StandardCallback.LocationUpdated, (int)EmoTracker.Data.ScriptManager.StandardCallback.LocationUpdated);
        }

        // Phase 7.1: ScriptManager.Current / .Instance retired. The test
        // that asserted they aliased each other (Phase 5 era) is no longer
        // applicable — ScriptManager is now per-state via TrackerState.

        [Fact]
        public void ScriptManager_Reset_ClearsForkCloner()
        {
            // ForkCloner holds Lua-side helpers on the source's mLua. After
            // Reset, mLua is closed — calling cloner.Resolve() would NRE
            // on the closed interpreter. Reset must drop the cloner so
            // post-reset state is consistent.
            var srcState = new EmoTracker.Data.Sessions.TrackerState("src");
            var src = srcState.Scripts;
            src.BootstrapInterpreter();
            src.ExecuteLuaString("test = { x = 1 }");

            var fork = (EmoTracker.Data.ScriptManager)src.Fork(ForkTestHelpers.NewDestState());
            Assert.NotNull(fork.ForkCloner);

            fork.Reset();
            Assert.Null(fork.ForkCloner);
            Assert.False(fork.IsLuaLoaded);
        }
    }
}
