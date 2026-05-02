using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Window-scoped extension: one instance per <see cref="WindowContext"/>,
    /// constructed by the <see cref="ExtensionManager"/> when a window is
    /// opened and disposed when it closes. Each instance's
    /// <see cref="StatusBarControl"/> appears only in its owning window's
    /// status bar.
    ///
    /// <para>
    /// Examples: NDI broadcast (per-window broadcast surface; each window
    /// publishes its active tab as a distinct NDI source), variant
    /// switcher (operates on the active tab in this window).
    /// </para>
    ///
    /// <para>
    /// Discovery is via <c>TypeRegistry</c>: the type is registered once
    /// at app start, then <c>Activator.CreateInstance(type)</c> spawns a
    /// new instance for each window.
    /// </para>
    /// </summary>
    public interface IWindowExtension : IExtension
    {
        /// <summary>
        /// Called once when this instance is bound to <paramref name="window"/>
        /// (after the window's <see cref="WindowContext"/> exists, before
        /// its first paint).
        /// </summary>
        void OnAttachedToWindow(WindowContext window);

        /// <summary>
        /// Called once when the window is closing. Implementations should
        /// release any window-tied resources (broadcast streams, overlays,
        /// timers).
        /// </summary>
        void OnDetachedFromWindow(WindowContext window);

        /// <summary>
        /// Fresh status-bar control instance per call. Hosted in this
        /// instance's owning window only. May return null.
        /// </summary>
        object StatusBarControl { get; }

        /// <summary>Persist this window-extension instance's state.</summary>
        JToken SerializeToJson();

        /// <summary>Restore this window-extension instance's state.</summary>
        bool DeserializeFromJson(JToken token);
    }
}
