using EmoTracker.Data.Packages;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data
{
    public interface IGamePackage
    {
        string DisplayName { get; }

        string UniqueID { get; }

        string Game { get; }

        string GameVariant { get; }

        GamePlatform Platform { get; }

        string Author { get; }

        Version Version { get; }

        Version LayoutEngineVersion { get; }

        bool FlaggedAsUnsafe { get; }

        IReadOnlyList<string> AutoTrackerProviders { get; }

        IGamePackageSource Source { get; }

        IEnumerable<IGamePackageVariant> AvailableVariants { get; }

        IGamePackageVariant FindVariant(string uid);

        string OverridePath { get; }

        /// <summary>
        /// Open <paramref name="path"/> from the pack, optionally with
        /// variant-aware resolution. Pass the active <see cref="IGamePackageVariant"/>
        /// for the calling context (typically the
        /// <see cref="Sessions.PackageInstance.ActiveVariant"/> of the
        /// state on whose behalf the open runs); pass null for non-variant
        /// resolution. Resolution order: variant override path → variant
        /// base path → pack override path → pack base path.
        /// </summary>
        System.IO.Stream Open(string path, IGamePackageVariant variant = null, bool ignoreVariants = false, bool ignoreOverrides = false);

        /// <summary>Like <see cref="Open"/> but only checks existence.</summary>
        bool Exists(string path, IGamePackageVariant variant = null, bool ignoreVariants = false, bool ignoreOverrides = false);

        void ExportUserOverride(string filename);

        void ResetUserOverrides();
    }

    public interface IGamePackageVariant
    {
        string UniqueID { get; }

        string DisplayName { get; }

        IGamePackage Package { get; }
    }
}
