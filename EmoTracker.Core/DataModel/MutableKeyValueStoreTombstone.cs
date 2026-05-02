namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Sentinel value stored in a <see cref="MutableKeyValueStore"/>'s local KV
    /// dictionary to mark a key as deleted at this level, shadowing any value
    /// inherited from <c>CopyOnWriteParent</c>. Reads that hit a tombstone treat the
    /// key as "absent" — they do not fall through to the parent and do not surface the
    /// sentinel itself to callers.
    /// </summary>
    public sealed class MutableKeyValueStoreTombstone
    {
        public static readonly MutableKeyValueStoreTombstone Instance = new MutableKeyValueStoreTombstone();
        private MutableKeyValueStoreTombstone() { }
    }
}
