namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Read-only view of a key-value store. Both <see cref="ImmutableKeyValueStore"/>
    /// (definition data) and <see cref="MutableKeyValueStore"/> (per-state data) expose
    /// this surface, so consumers can be polymorphic over either kind.
    ///
    /// Returned reference values are deep-copied at the boundary (via
    /// <see cref="IDeepCopyable"/> when applicable) so the store's interior state
    /// cannot be mutated through caller-held references.
    /// </summary>
    public interface IReadOnlyKeyValueStore
    {
        /// <summary>Returns the value stored at <paramref name="key"/>, or <paramref name="defaultValue"/> if no value is present.</summary>
        T GetValue<T>(string key, T defaultValue);

        /// <summary>
        /// Attempts to read the value stored at <paramref name="key"/>. Returns true and writes through <paramref name="valueOut"/>
        /// if a value is present; otherwise returns false and leaves <paramref name="valueOut"/> unchanged.
        /// </summary>
        bool TryGetValue<T>(string key, ref T valueOut);
    }
}
