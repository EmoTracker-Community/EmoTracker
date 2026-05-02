using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Application-scoped extension: one instance per application
    /// lifetime, discovered + auto-instantiated by the
    /// <see cref="ExtensionManager"/> via <c>TypedObjectRegistry</c>.
    /// The instance is shared across every window and every loaded
    /// package; its <see cref="StatusBarControl"/> appears in every
    /// window's status bar.
    ///
    /// <para>
    /// Examples: MCP dev server (single HTTP host), Twitch chat client
    /// (single connection), voice recognition (single mic stream + ML
    /// model).
    /// </para>
    /// </summary>
    public interface IApplicationExtension : IExtension
    {
        /// <summary>
        /// Called once on startup after every <see cref="IApplicationExtension"/>
        /// has been instantiated. Implementations should defer expensive
        /// work (network listeners, model loads) until this hook.
        /// </summary>
        void Start(IApplicationContext app);

        /// <summary>Called once on application shutdown.</summary>
        void Stop();

        /// <summary>
        /// Fresh status-bar control instance per call (Avalonia visuals
        /// are single-parent, so each window's binding gets its own).
        /// May return null to opt out of status-bar surfacing.
        /// </summary>
        object StatusBarControl { get; }

        /// <summary>Persist app-wide extension state. May return null.</summary>
        JToken SerializeToJson();

        /// <summary>Restore app-wide extension state.</summary>
        bool DeserializeFromJson(JToken token);
    }
}
