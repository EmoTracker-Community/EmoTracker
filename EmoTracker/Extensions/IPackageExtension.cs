using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// Package-scoped extension: one instance per loaded
    /// <see cref="PackageInstance"/>. Allocated when a pack is activated;
    /// disposed when the pack is unloaded.
    ///
    /// <para>
    /// Use this scope for state tied to the pack identity (cached pack
    /// metadata, parsed config, indices) that doesn't need to be
    /// duplicated per tracker state. The instance's
    /// <see cref="StatusBarControl"/> surfaces in any window whose active
    /// tab is hosted by the same <see cref="PackageInstance"/>.
    /// </para>
    ///
    /// <para>
    /// Discovery is via <c>TypeRegistry</c>: types are registered once at
    /// app start, then <c>Activator.CreateInstance(type)</c> spawns a new
    /// instance per package.
    /// </para>
    /// </summary>
    public interface IPackageExtension : IExtension
    {
        /// <summary>Called when this instance is bound to <paramref name="package"/>.</summary>
        void OnAttachedToPackage(PackageInstance package);

        /// <summary>Called when the package is being torn down.</summary>
        void OnDetachedFromPackage(PackageInstance package);

        /// <summary>Fresh status-bar control instance per call. May return null.</summary>
        object StatusBarControl { get; }

        /// <summary>Persist this package-extension instance's state.</summary>
        JToken SerializeToJson();

        /// <summary>Restore this package-extension instance's state.</summary>
        bool DeserializeFromJson(JToken token);
    }
}
