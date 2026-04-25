using System.Collections.Generic;
using System.ComponentModel;

namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Mutable, copy-on-write key-value store with per-key write isolation.
    ///
    /// <para>
    /// Construction with a <c>copyOnWriteParent</c> creates a store that inherits
    /// reads from the parent but writes locally — see <see cref="SetValue{T}"/> /
    /// <see cref="Remove"/>. <see cref="Flatten"/> resolves the parent chain into a
    /// single local dictionary; it is explicit, not automatic.
    /// </para>
    /// <para>
    /// Values are deep-copied at the boundary (read and write) via
    /// <see cref="IDeepCopyable"/> for non-trivially-copyable reference types, so
    /// callers cannot mutate stored state through their references. Trivially-copyable
    /// types (primitives, strings, enums, and a small set of framework-known
    /// immutables) are stored and returned as-is.
    /// </para>
    /// </summary>
    public class MutableKeyValueStore : IReadOnlyKeyValueStore, INotifyPropertyChanged
    {
        protected Dictionary<string, object> KV;
        protected MutableKeyValueStore CopyOnWriteParent;
        bool mSuppressNotifications;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Creates an empty mutable store with no copy-on-write parent.</summary>
        public MutableKeyValueStore()
        {
            KV = new Dictionary<string, object>();
        }

        /// <summary>
        /// Creates a mutable store that reads through to <paramref name="copyOnWriteParent"/>
        /// for keys not present locally. Writes to this store shadow the parent on a
        /// per-key basis and do not affect the parent.
        /// </summary>
        public MutableKeyValueStore(MutableKeyValueStore copyOnWriteParent)
        {
            KV = new Dictionary<string, object>();
            CopyOnWriteParent = copyOnWriteParent;
        }

        // Walks local KV -> CopyOnWriteParent chain. A tombstone in any local dict
        // short-circuits the walk and is reported as "absent" to the caller.
        bool TryReadRaw(string key, out object value)
        {
            for (var cursor = this; cursor != null; cursor = cursor.CopyOnWriteParent)
            {
                if (cursor.KV.TryGetValue(key, out var raw))
                {
                    if (raw is MutableKeyValueStoreTombstone)
                    {
                        value = null;
                        return false;
                    }
                    value = raw;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public T GetValue<T>(string key, T defaultValue)
        {
            if (TryReadRaw(key, out var raw))
            {
                if (raw == null) return default(T);
                return (T)KeyValueStoreInternal.DeepCopyForStore(raw);
            }
            return defaultValue;
        }

        public bool TryGetValue<T>(string key, ref T valueOut)
        {
            if (TryReadRaw(key, out var raw))
            {
                if (raw == null) { valueOut = default(T); return true; }
                valueOut = (T)KeyValueStoreInternal.DeepCopyForStore(raw);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Per-key COW: deep-copies <paramref name="value"/> (using
        /// <see cref="IDeepCopyable"/> for non-trivially-copyable types) and writes it
        /// into the local dictionary, shadowing any value inherited from the parent
        /// chain for that single key. Other keys remain shared with the parent.
        /// Raises <see cref="PropertyChanged"/> with <paramref name="key"/> as the
        /// property name.
        /// </summary>
        public void SetValue<T>(string key, T value)
        {
            object stored = (value == null) ? null : KeyValueStoreInternal.DeepCopyForStore(value);
            KV[key] = stored;
            RaisePropertyChanged(key);
        }

        /// <summary>
        /// Per-key COW: writes a tombstone for <paramref name="key"/> into the local
        /// dictionary so subsequent reads short-circuit to "absent" without walking to
        /// the parent. Other keys inherited from the parent chain remain shared.
        ///
        /// Returns true if a value (local or inherited) was previously present and
        /// has been suppressed by this call.
        /// </summary>
        public bool Remove(string key)
        {
            bool wasPresent = TryReadRaw(key, out _);

            if (CopyOnWriteParent != null)
            {
                // Inherited value may exist; tombstone is the only way to suppress it.
                KV[key] = MutableKeyValueStoreTombstone.Instance;
            }
            else
            {
                // No parent: just drop the local entry. If it wasn't there, do nothing.
                if (!KV.Remove(key)) return false;
            }

            if (wasPresent) RaisePropertyChanged(key);
            return wasPresent;
        }

        /// <summary>
        /// Returns an <see cref="ImmutableKeyValueStore"/> snapshot of this store's
        /// flattened state. Conceptually: take a COW copy of <c>this</c> (so this
        /// store is unaffected), Flatten that copy, and wrap its KV.
        /// </summary>
        public ImmutableKeyValueStore AsImmutable()
        {
            var copy = new MutableKeyValueStore(this);
            copy.Flatten();
            // Flatten already deep-copied every absorbed value at its own boundary,
            // so we can hand the dictionary to the immutable store as-is rather than
            // pay for a second pass via the public ctor.
            return ImmutableKeyValueStore.TakeOwnershipOfDictionary(copy.KV);
        }

        /// <summary>
        /// Walks <c>CopyOnWriteParent</c> to the root, merging entries into a single
        /// flattened dictionary on <c>this</c>. Local entries win; inherited values
        /// are deep-copied on absorption so the flattened store does not alias any
        /// ancestor's stored references. Tombstones in the resulting dictionary are
        /// dropped before exit. Sets <c>CopyOnWriteParent</c> to null on exit.
        ///
        /// <para>
        /// Flatten is an explicit operation — it is NOT triggered automatically by
        /// <see cref="SetValue{T}"/> / <see cref="Remove"/>. Per-key COW is sufficient
        /// for write isolation. Used by <see cref="AsImmutable"/>, by serialization
        /// paths that need a single-dictionary view, and may be invoked externally to
        /// bound long parent chains when read cost becomes a concern.
        /// </para>
        /// <para>
        /// <see cref="PropertyChanged"/> events are suppressed for the duration of the
        /// merge so observers are not flooded with notifications for keys that are
        /// merely being locally materialized rather than semantically changing.
        /// </para>
        /// </summary>
        public void Flatten()
        {
            if (CopyOnWriteParent == null) return;

            mSuppressNotifications = true;
            try
            {
                // Walk root → leaf. Leaves win. Inherited values are deep-copied at the
                // moment of absorption so the flattened result doesn't alias ancestors.
                var stack = new Stack<MutableKeyValueStore>();
                for (var cursor = this; cursor != null; cursor = cursor.CopyOnWriteParent)
                    stack.Push(cursor);

                var flat = new Dictionary<string, object>();
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    bool isLeaf = (node == this);
                    foreach (var kvp in node.KV)
                    {
                        if (kvp.Value is MutableKeyValueStoreTombstone)
                        {
                            flat.Remove(kvp.Key);
                        }
                        else
                        {
                            object absorbed;
                            if (isLeaf || kvp.Value == null)
                                absorbed = kvp.Value;
                            else
                                absorbed = KeyValueStoreInternal.DeepCopyForStore(kvp.Value);
                            flat[kvp.Key] = absorbed;
                        }
                    }
                }

                KV = flat;
                CopyOnWriteParent = null;
            }
            finally
            {
                mSuppressNotifications = false;
            }
        }

        void RaisePropertyChanged(string key)
        {
            if (mSuppressNotifications) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
        }
    }
}
