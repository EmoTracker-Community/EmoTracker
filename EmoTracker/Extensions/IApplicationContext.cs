using EmoTracker.Data.Sessions;
using System.Collections.Generic;
using System.ComponentModel;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Thin facade onto <see cref="ApplicationModel"/> exposing the
    /// surfaces an <see cref="IApplicationExtension"/> needs to operate
    /// without taking a hard dependency on the concrete app-model type.
    /// Implements <see cref="INotifyPropertyChanged"/> so extensions can
    /// subscribe to high-level changes (active window, package list,
    /// etc.).
    /// </summary>
    public interface IApplicationContext : INotifyPropertyChanged
    {
        /// <summary>Currently-loaded packages (one per active pack-load).</summary>
        IReadOnlyList<PackageInstance> PackageInstances { get; }

        /// <summary>All open application windows.</summary>
        IReadOnlyList<WindowContext> Windows { get; }

        /// <summary>
        /// The window the user most recently activated, or null if no
        /// window has focus yet. Extensions that need to operate on
        /// "the user's current view" route through here.
        /// </summary>
        WindowContext CurrentlyActiveWindowContext { get; }
    }
}
