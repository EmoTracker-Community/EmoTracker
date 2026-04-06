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

        IGamePackageVariant ActiveVariant { get; set; }

        IGamePackageVariant FindVariant(string uid);

        string OverridePath { get; }

        System.IO.Stream Open(string path, bool ignoreVariants = false, bool ignoreOverrides = false);

        bool Exists(string path, bool ignoreVariants = false, bool ignoreOverrides = false);

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
