using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel.SmokeTest;
using System.Collections.Generic;
using Xunit;

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
        public void ScriptManagerHost_Current_IsSettableAndReturnedByGetScriptManager()
        {
            var prior = ScriptManagerHost.Current;
            try
            {
                var stub = new StubScriptManager();
                ScriptManagerHost.Current = stub;

                // Any ModelTypeBase-derived holder routes through the host.
                var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
                Assert.Same(stub, smoke.GetScriptManager());

                // The dispatch path actually flows through the stub.
                smoke.GetScriptManager().InvokeStandardCallback(StandardCallback.LocationUpdating);
                Assert.Single(stub.Calls);
                Assert.Equal(StandardCallback.LocationUpdating, stub.Calls[0]);
            }
            finally
            {
                ScriptManagerHost.Current = prior;
            }
        }

        [Fact]
        public void GetScriptManager_VirtualDefault_FollowsScriptManagerHost()
        {
            // Subclasses can override GetScriptManager() to return their
            // state's manager (Phase 6); the default implementation flows
            // through the static host.
            var prior = ScriptManagerHost.Current;
            try
            {
                var a = new StubScriptManager();
                var b = new StubScriptManager();

                ScriptManagerHost.Current = a;
                var smoke = Phase1SmokeModelType.CreateDefinition("d", null);
                Assert.Same(a, smoke.GetScriptManager());

                ScriptManagerHost.Current = b;
                Assert.Same(b, smoke.GetScriptManager());
            }
            finally
            {
                ScriptManagerHost.Current = prior;
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

        [Fact]
        public void ScriptManager_InstanceAliasesCurrent()
        {
            // Pre-Phase-5 callers used ScriptManager.Instance; Phase 5 makes
            // ScriptManager constructible (no longer a strict
            // ObservableSingleton<>) but the Instance alias forwards to
            // Current so all ~97 existing callsites keep working.
            Assert.Same(EmoTracker.Data.ScriptManager.Current, EmoTracker.Data.ScriptManager.Instance);
        }
    }
}
