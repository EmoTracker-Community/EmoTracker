using System;

namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 7.1.g: ambient resolver for the few app-wide infrastructure
    /// callsites (<see cref="Packages.PackageManager"/>,
    /// <see cref="Packages.PackageRepository"/>,
    /// <see cref="Packages.GamePackage"/>,
    /// <see cref="Media.ImageReference"/>) that genuinely need to ask
    /// "what's the currently-active <see cref="TrackerState"/>?" — these
    /// are pieces of infrastructure that have no model holder and so
    /// cannot route through <see cref="Core.DataModel.ModelTypeBase.OwnerState"/>.
    ///
    /// <para>
    /// The resolver is <i>installed</i> at app startup by
    /// <c>ApplicationModel</c> (which knows how to find its
    /// <c>PrimaryState</c>). The Data assembly never imports
    /// <c>ApplicationModel</c>; the inversion-of-control here keeps the
    /// dependency arrow pointing the right way.
    /// </para>
    ///
    /// <para>
    /// Use sparingly. Anything that has access to a model with an
    /// <see cref="Core.DataModel.ModelTypeBase.OwnerState"/> should route
    /// through that state directly rather than reaching for
    /// <see cref="Primary"/>.
    /// </para>
    /// </summary>
    public static class ActiveSession
    {
        /// <summary>
        /// Installed by <c>ApplicationModel</c> at startup. Returns the
        /// active primary <see cref="TrackerState"/>, or null if no pack
        /// is loaded yet.
        /// </summary>
        public static Func<TrackerState> PrimaryStateResolver { get; set; }

        /// <summary>
        /// The active primary <see cref="TrackerState"/>, or null if no
        /// pack is loaded yet (or the resolver isn't installed).
        /// </summary>
        public static TrackerState Primary => PrimaryStateResolver?.Invoke();
    }
}
