using System;

namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Internal helpers shared by <see cref="ImmutableKeyValueStore"/> and
    /// <see cref="MutableKeyValueStore"/>: the trivially-copyable type predicate and the
    /// "deep-copy at the boundary" helper that picks between as-is storage and
    /// <see cref="IDeepCopyable.DeepCopy"/> based on the value's runtime type.
    /// </summary>
    internal static class KeyValueStoreInternal
    {
        /// <summary>
        /// Returns true for types that are safe to store and return as-is: primitives
        /// (including <see cref="bool"/>), enums, <see cref="string"/>, and a small set
        /// of framework-known immutable value types. These types either copy on
        /// assignment (value types) or are themselves immutable (string), so passing
        /// the same reference to multiple consumers cannot cause cross-state mutation.
        /// </summary>
        public static bool IsTriviallyCopyable(Type type)
        {
            if (type == null) return true;
            // Treat Nullable<T> like T for this check.
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsPrimitive) return true;
            if (type.IsEnum) return true;
            if (type == typeof(string)) return true;
            if (type == typeof(decimal)) return true;
            if (type == typeof(DateTime)) return true;
            if (type == typeof(DateTimeOffset)) return true;
            if (type == typeof(Guid)) return true;
            if (type == typeof(TimeSpan)) return true;
            return false;
        }

        /// <summary>
        /// Either returns <paramref name="value"/> as-is (when its runtime type is
        /// trivially copyable) or returns the result of <see cref="IDeepCopyable.DeepCopy"/>.
        /// Throws <see cref="InvalidOperationException"/> if the value is a mutable
        /// reference type that does not implement <see cref="IDeepCopyable"/>.
        /// <c>null</c> is always returned as-is.
        /// </summary>
        public static object DeepCopyForStore(object value)
        {
            if (value == null) return null;
            Type t = value.GetType();
            if (IsTriviallyCopyable(t)) return value;
            if (value is IDeepCopyable copyable) return copyable.DeepCopy();
            throw new InvalidOperationException(
                "Cannot store a value of type '" + t.FullName + "' in a key-value store: " +
                "it is a non-trivial reference type and does not implement IDeepCopyable. " +
                "Either implement IDeepCopyable on the type or use a primitive/string/enum/" +
                "DateTime/DateTimeOffset/Guid/TimeSpan/decimal value.");
        }
    }
}
