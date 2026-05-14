using EmoTracker.Core.DataModel;
using EmoTracker.Core.Services;
using EmoTracker.Data.Scripting;
using NLua;
using System;

namespace EmoTracker.Data.AutoTracking
{
    /// <summary>
    /// A <see cref="MemorySegment"/> whose "fresh data available"
    /// notification routes through a pack-supplied <see cref="LuaFunction"/>.
    /// Created by <see cref="ScriptManager.AddMemoryWatch"/> on behalf of
    /// pack init scripts that call <c>ScriptHost:AddMemoryWatch(...)</c>.
    ///
    /// <para>
    /// <b>Forking:</b> the LuaFunction callback (<see cref="Callback"/>)
    /// is per-state — each fork must point at its own interpreter's
    /// version of the same Lua function. The segment shares
    /// <see cref="ModelTypeBase.ImmutableData"/> (and thus
    /// DefinitionId) with its source via the base
    /// <see cref="MemorySegment.OnForked"/>; the callback itself is
    /// re-cloned through the fork's <see cref="LuaStateCloner"/> by
    /// <see cref="ScriptManager.RewireForkedLuaSegment"/> after the main
    /// clone walk completes (same shape used for <see cref="LuaItem"/>
    /// callbacks).
    /// </para>
    ///
    /// <para>
    /// The Lua callback is dispatched onto the UI thread (so pack code
    /// can safely manipulate model objects) and wrapped in a
    /// <see cref="LocationDatabase.SuspendRefreshScope"/> so a single
    /// memory-segment update invalidates accessibility once at the end
    /// rather than per-mutation.
    /// </para>
    /// </summary>
    public class LuaMemorySegment : MemorySegment
    {
        // The Lua function the pack registered as this segment's update
        // callback. Holds a reference into the OWNING state's Lua
        // interpreter — must be re-cloned through the fork's cloner when
        // the segment forks (see ScriptManager.RewireForkedLuaSegment).
        LuaFunction mCallback;

        public LuaFunction Callback
        {
            get { return mCallback; }
            set { mCallback = value; }
        }

        public LuaMemorySegment(string name, ulong startAddress, ulong length, int period = 500)
            : base(name, startAddress, length, period)
        {
        }

        public LuaMemorySegment() : base()
        {
        }

        protected override void OnSegmentDataUpdated()
        {
            var state = this.OwnerState as Sessions.TrackerState;
            if (state == null) return;
            var scripts = state.Scripts;
            if (scripts == null) return;
            var callback = mCallback;
            if (callback == null) return;

            // Memory polling fires on a worker thread. The callback may
            // mutate items / locations, both of which expect UI-thread
            // affinity for INPC + accessibility-refresh dispatch. Marshal
            // through the dispatcher and wrap in a SuspendRefreshScope so
            // a single batch of mutations triggers one refresh.
            Dispatch.BeginInvoke(() =>
            {
                try
                {
                    object[] result;
                    using (new LocationDatabase.SuspendRefreshScope(state.Locations))
                    {
                        result = scripts.SafeCall(callback, this);
                    }
                    bool succeeded = result != null && result.Length > 0
                        && result[0] != null && !(result[0] is bool b && !b);
                    if (succeeded)
                        Dirty = false;
                }
                catch (Exception ex)
                {
                    scripts.OutputException(ex);
                }
            });
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            // mCallback is intentionally NOT copied here — it's a
            // LuaFunction bound to the SOURCE's interpreter. The fork's
            // ScriptManager calls RewireForkedLuaSegment after the
            // LuaStateCloner runs to clone the callback onto the fork's
            // interpreter and assign it via the Callback property.
        }

        public override void Dispose()
        {
            try { mCallback?.Dispose(); } catch { }
            mCallback = null;
            base.Dispose();
        }
    }
}
