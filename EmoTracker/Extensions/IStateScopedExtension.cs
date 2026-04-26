using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Phase 7.4: per-state extension instance. Created on demand by an
    /// <see cref="IStateScopedExtensionFactory"/> when a
    /// <see cref="TrackerState"/> is registered with the
    /// <see cref="ExtensionManager"/>; disposed when the state is
    /// unregistered.
    ///
    /// <para>
    /// Examples (delivered in Phase 7.4 cont):
    /// <list type="bullet">
    ///   <item><c>AutoTrackerInstance</c> — owns the per-state provider
    ///         connection + memory watches + connection state.</item>
    ///   <item><c>NoteTakingInstance</c> — owns the per-state status-bar
    ///         indicator. (Notes themselves live on <c>Location.NoteTakingSite</c>;
    ///         this is just the indicator surface.)</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IStateScopedExtension
    {
        /// <summary>The unique id of the corresponding factory / app-wide extension.</summary>
        string ExtensionUID { get; }

        /// <summary>Called once when the extension is bound to a state.</summary>
        void OnAttachedToState(TrackerState state);

        /// <summary>
        /// Called once when the state is being unregistered (or the
        /// PackageInstance is being torn down). Implementations should
        /// release any resources tied to the state.
        /// </summary>
        void OnDetachedFromState(TrackerState state);

        /// <summary>JSON serialization of this per-state extension's state. May return null if there's nothing to persist.</summary>
        JToken SerializeToJson();

        /// <summary>Restore this per-state extension's state from JSON. Returns true on successful round-trip.</summary>
        bool DeserializeFromJson(JToken token);

        /// <summary>
        /// Optional per-state status-bar indicator (rendered in the per-window
        /// status bar surface alongside the app-wide extension's indicator,
        /// or replacing it depending on UI policy). May return null.
        /// </summary>
        object StatusBarControl { get; }
    }

    /// <summary>
    /// Phase 7.4: factory for <see cref="IStateScopedExtension"/> instances.
    /// Itself an app-wide <see cref="Extension"/> so it's discovered by
    /// <see cref="ExtensionManager"/>'s reflection scan; on each new state,
    /// the manager calls <see cref="CreateForState"/> to allocate that
    /// state's instance.
    /// </summary>
    public interface IStateScopedExtensionFactory : Extension
    {
        IStateScopedExtension CreateForState(TrackerState state);
    }
}
