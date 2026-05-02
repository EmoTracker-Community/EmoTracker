using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Tracker-state-scoped extension: one instance per
    /// <see cref="TrackerState"/>. Allocated when a state is registered
    /// with its <see cref="PackageInstance"/>; disposed when the state is
    /// unregistered.
    ///
    /// <para>
    /// Discovery is via <c>TypeRegistry</c>: types are registered once
    /// at app start, then <c>Activator.CreateInstance(type)</c> spawns a
    /// new instance per state. Multiple primary states across windows
    /// each get their own instance; <see cref="StatusBarControl"/>
    /// surfaces only when its owning state is the active tab.
    /// </para>
    ///
    /// <para>
    /// Examples: AutoTracker (per-state memory watches, provider
    /// connection, polling timer), NoteTaking (per-state location-notes
    /// indicator).
    /// </para>
    ///
    /// <para>
    /// <b>Fork support.</b> When a <see cref="TrackerState"/> is forked,
    /// its tracker-extensions fork with it via <see cref="Fork"/>. The
    /// fork must produce a destination-state-bound instance carrying a
    /// snapshot of any per-state data the source holds. Implementations
    /// that own <see cref="EmoTracker.Core.DataModel.ModelTypeBase"/>-derived data
    /// (e.g. memory segments referenced by item auto-track configs)
    /// must ensure the forked data is registered with
    /// <paramref name="destState"/>'s
    /// <see cref="EmoTracker.Core.DataModel.IModelResolver"/> so cross-references
    /// (<c>ModelReference&lt;T&gt;</c>) resolve to the fork's instances.
    /// Implementations with no per-state data (status indicators only)
    /// may simply allocate a fresh instance.
    /// </para>
    /// </summary>
    public interface ITrackerExtension : IExtension
    {
        /// <summary>Called when this instance is bound to <paramref name="state"/>.</summary>
        void OnAttachedToState(TrackerState state);

        /// <summary>Called when the state is being torn down.</summary>
        void OnDetachedFromState(TrackerState state);

        /// <summary>Fresh status-bar control instance per call. May return null.</summary>
        object StatusBarControl { get; }

        /// <summary>Persist this tracker-extension instance's state.</summary>
        JToken SerializeToJson();

        /// <summary>Restore this tracker-extension instance's state.</summary>
        bool DeserializeFromJson(JToken token);

        /// <summary>
        /// Produce a new instance bound to <paramref name="destState"/>
        /// carrying a snapshot of this instance's per-state data. The
        /// returned instance has NOT yet had
        /// <see cref="OnAttachedToState"/> called on it; the
        /// <see cref="ExtensionManager"/> performs the attach after the
        /// fork pipeline completes.
        /// </summary>
        ITrackerExtension Fork(TrackerState destState);
    }
}
