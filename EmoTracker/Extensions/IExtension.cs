namespace EmoTracker.Extensions
{
    /// <summary>
    /// Marker base for all extension types. Provides identity + ordering
    /// metadata; lifecycle / status-surface members live on the four
    /// scoped sub-interfaces (<see cref="IApplicationExtension"/>,
    /// <see cref="IWindowExtension"/>, <see cref="IPackageExtension"/>,
    /// <see cref="ITrackerExtension"/>).
    ///
    /// <para>
    /// The <see cref="ExtensionManager"/> aggregates instances of all
    /// four scopes for status-bar display via
    /// <c>GetActiveExtensionsFor(WindowContext)</c>; results are grouped
    /// by scope (app -> window -> package -> tracker) and within each
    /// scope sorted by <see cref="Priority"/>.
    /// </para>
    /// </summary>
    public interface IExtension
    {
        /// <summary>Human-readable display name (settings dialog, etc.).</summary>
        string Name { get; }

        /// <summary>Stable unique identifier for persistence + lookups.</summary>
        string UID { get; }

        /// <summary>
        /// Sort key within the extension's scope group. Lower values
        /// surface earlier in the status bar.
        /// </summary>
        int Priority { get; }
    }
}
