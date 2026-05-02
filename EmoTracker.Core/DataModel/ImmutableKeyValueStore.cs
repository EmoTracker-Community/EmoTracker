using System.Collections.Generic;

namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Read-only key-value store. Definition data lives here; instances are intended to
    /// be shared by reference across forks (each fork holds the same
    /// <see cref="ImmutableKeyValueStore"/> reference rather than a copy).
    ///
    /// Returned reference values are deep-copied at the boundary (via
    /// <see cref="IDeepCopyable"/> when applicable) so the store's interior state cannot
    /// be mutated through caller-held references.
    /// </summary>
    public class ImmutableKeyValueStore : IReadOnlyKeyValueStore
    {
        protected Dictionary<string, object> KV;

        /// <summary>Creates an empty immutable store.</summary>
        public ImmutableKeyValueStore()
        {
            KV = new Dictionary<string, object>();
        }

        /// <summary>
        /// Creates an immutable store populated from <paramref name="initial"/>. Each
        /// value is deep-copied through the same boundary the read path uses, so the
        /// caller cannot retain a mutating reference to anything inside the store.
        /// </summary>
        public ImmutableKeyValueStore(Dictionary<string, object> initial)
        {
            if (initial == null)
            {
                KV = new Dictionary<string, object>();
                return;
            }
            KV = new Dictionary<string, object>(initial.Count);
            foreach (var kvp in initial)
            {
                KV[kvp.Key] = (kvp.Value == null)
                    ? null
                    : KeyValueStoreInternal.DeepCopyForStore(kvp.Value);
            }
        }

        // Internal trusted factory used by MutableKeyValueStore.AsImmutable, which
        // flattens with deep-copies on absorption and so hands us a dictionary that
        // is already isolated from any external aliases. Skipping the public
        // ctor's redundant per-value deep-copy avoids a double-copy on that path.
        internal static ImmutableKeyValueStore TakeOwnershipOfDictionary(Dictionary<string, object> alreadyDeepCopied)
        {
            var inst = new ImmutableKeyValueStore();
            inst.KV = alreadyDeepCopied ?? new Dictionary<string, object>();
            return inst;
        }

        public T GetValue<T>(string key, T defaultValue)
        {
            if (KV.TryGetValue(key, out var raw))
            {
                if (raw == null) return default(T);
                return (T)KeyValueStoreInternal.DeepCopyForStore(raw);
            }
            return defaultValue;
        }

        public bool TryGetValue<T>(string key, ref T valueOut)
        {
            if (KV.TryGetValue(key, out var raw))
            {
                if (raw == null) { valueOut = default(T); return true; }
                valueOut = (T)KeyValueStoreInternal.DeepCopyForStore(raw);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Indicates whether a value is present at <paramref name="key"/>. Useful for
        /// callers that need to distinguish "absent" from "stored as default" without
        /// performing a deep-copy.
        /// </summary>
        public bool ContainsKey(string key)
        {
            return KV.ContainsKey(key);
        }
    }
}
