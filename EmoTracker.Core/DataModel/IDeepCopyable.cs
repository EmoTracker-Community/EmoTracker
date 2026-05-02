namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Implemented by reference-typed values that may be stored in a key-value store
    /// (<see cref="ImmutableKeyValueStore"/> / <see cref="MutableKeyValueStore"/>).
    /// The store invokes <see cref="DeepCopy"/> at every read/write boundary to enforce
    /// copy-on-write isolation between stored state and caller-held references.
    ///
    /// Primitives, strings, enums, and a small set of framework-known immutable types
    /// (<see cref="System.DateTime"/>, <see cref="System.DateTimeOffset"/>,
    /// <see cref="System.Guid"/>, <see cref="System.TimeSpan"/>, <see cref="decimal"/>)
    /// are recognized by the store and stored as-is without invoking this interface.
    /// Any other reference type stored without implementing
    /// <see cref="IDeepCopyable"/> causes a write to throw on the store.
    /// </summary>
    public interface IDeepCopyable
    {
        /// <summary>
        /// Returns a deep copy of this value: transitively independent from the source
        /// such that mutating the copy never observably affects the original, and
        /// vice-versa.
        /// </summary>
        object DeepCopy();
    }
}
