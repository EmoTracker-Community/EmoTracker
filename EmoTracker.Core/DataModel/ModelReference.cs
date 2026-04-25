using System;

namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Cross-model reference held *by identity* (a stable
    /// <see cref="ModelTypeBase.DefinitionId"/>) rather than by direct C# reference.
    /// The same <see cref="ModelReference{T}"/>, held by a model and any of its forks,
    /// resolves to each state's own concrete instance via that state's
    /// <see cref="IModelResolver"/>.
    ///
    /// <para>
    /// Resolution flows through the holder's <see cref="ModelTypeBase.GetModelResolver"/>
    /// (which returns the holder's state's resolver, or the global ambient resolver
    /// if no state context is set yet). Results are cached per-instance — repeat
    /// reads return the cached target without re-querying the resolver. Cache is
    /// dropped explicitly via <see cref="Set(T)"/>, <see cref="Set(Guid)"/>,
    /// <see cref="Clear"/>, <see cref="InvalidateCache"/>, or <see cref="ForFork"/>.
    /// </para>
    /// <para>
    /// <see cref="ModelReference{T}"/> is intentionally a class with mutable cache
    /// state, and is intentionally stored as a private field on the holder model
    /// rather than inside the KV stores: storing it in a KV store would deep-copy
    /// the reference (and reset the cache) on every read, defeating the cache.
    /// Holder <c>OnForked</c> overrides carry the reference across via
    /// <see cref="ForFork"/>.
    /// </para>
    /// </summary>
    public sealed class ModelReference<T> : IDeepCopyable, IEquatable<ModelReference<T>>
        where T : class
    {
        // Holder back-reference. Resolution asks the holder for its current
        // IModelResolver, which will eventually vary by state. Null means
        // "fall back to ModelResolver.Current" — used by tests and tooling
        // that construct references outside a holder.
        readonly ModelTypeBase mHolder;
        Guid mDefinitionId;
        T mCached;

        /// <summary>Creates an empty reference (no target) bound to the given holder.</summary>
        public ModelReference(ModelTypeBase holder = null)
        {
            mHolder = holder;
        }

        /// <summary>Creates a reference to the model with the given DefinitionId.</summary>
        public ModelReference(ModelTypeBase holder, Guid definitionId)
        {
            mHolder = holder;
            mDefinitionId = definitionId;
        }

        /// <summary>
        /// Creates a reference to <paramref name="target"/>, capturing its
        /// DefinitionId and seeding the cache with the target itself (so the first
        /// <see cref="Target"/> read returns it without invoking the resolver).
        /// </summary>
        public ModelReference(ModelTypeBase holder, T target)
        {
            mHolder = holder;
            Set(target);
        }

        /// <summary>The DefinitionId of the referenced model. <see cref="Guid.Empty"/> if unset.</summary>
        public Guid DefinitionId => mDefinitionId;

        /// <summary>True iff <see cref="DefinitionId"/> is <see cref="Guid.Empty"/>.</summary>
        public bool IsEmpty => mDefinitionId == Guid.Empty;

        /// <summary>
        /// Resolves and returns the referenced model. Uses the holder's resolver
        /// (or <see cref="ModelResolver.Current"/> when no holder was given);
        /// caches the result. Returns <c>null</c> if the reference is empty,
        /// no resolver is available, or the resolver returns null.
        /// </summary>
        public T Target
        {
            get
            {
                if (IsEmpty) return null;
                if (mCached != null) return mCached;
                var resolver = (mHolder != null) ? mHolder.GetModelResolver() : ModelResolver.Current;
                if (resolver == null) return null;
                mCached = resolver.Resolve<T>(mDefinitionId);
                return mCached;
            }
        }

        /// <summary>
        /// Replaces this reference with one pointing at <paramref name="target"/>.
        /// Captures the DefinitionId from the target (via cast to
        /// <see cref="ModelTypeBase"/>) and seeds the cache with the target itself.
        /// Passing <c>null</c> clears the reference.
        /// </summary>
        public void Set(T target)
        {
            if (target == null)
            {
                Clear();
                return;
            }

            mDefinitionId = (target as ModelTypeBase)?.DefinitionId ?? Guid.Empty;
            mCached = target;
        }

        /// <summary>
        /// Replaces this reference with one pointing at the model whose DefinitionId
        /// is <paramref name="definitionId"/>. Drops the cached target (the next
        /// <see cref="Target"/> read will re-resolve).
        /// </summary>
        public void Set(Guid definitionId)
        {
            mDefinitionId = definitionId;
            mCached = null;
        }

        /// <summary>Clears the reference (DefinitionId becomes Empty) and drops the cache.</summary>
        public void Clear()
        {
            mDefinitionId = Guid.Empty;
            mCached = null;
        }

        /// <summary>
        /// Drops the cached target without changing the DefinitionId. The next
        /// <see cref="Target"/> read goes back to the resolver. Useful when an
        /// observer detects that the resolver's graph has changed and the cached
        /// target may be stale.
        /// </summary>
        public void InvalidateCache()
        {
            mCached = null;
        }

        /// <summary>
        /// Returns a fresh <see cref="ModelReference{T}"/> bound to
        /// <paramref name="newHolder"/>, carrying the same DefinitionId but no
        /// cached target. Holder <c>OnForked</c> overrides invoke this when copying
        /// a reference from the source so the new fork's first read resolves through
        /// its own resolver.
        /// </summary>
        public ModelReference<T> ForFork(ModelTypeBase newHolder)
        {
            return new ModelReference<T>(newHolder, mDefinitionId);
        }

        /// <summary>
        /// <see cref="IDeepCopyable.DeepCopy"/>: returns a fresh reference with the
        /// same holder and DefinitionId, no cache. Equivalent to
        /// <c>ForFork(mHolder)</c>; provided so the KV-store boundary contract is
        /// satisfied for the (atypical) case of a <see cref="ModelReference{T}"/>
        /// stored inside a KV store.
        /// </summary>
        object IDeepCopyable.DeepCopy()
        {
            return new ModelReference<T>(mHolder, mDefinitionId);
        }

        // ------------------------------------------------------------ Equality

        public bool Equals(ModelReference<T> other)
        {
            return other != null && mDefinitionId == other.mDefinitionId;
        }

        public override bool Equals(object obj)
        {
            return obj is ModelReference<T> r && Equals(r);
        }

        public override int GetHashCode()
        {
            return mDefinitionId.GetHashCode();
        }

        public static bool operator ==(ModelReference<T> a, ModelReference<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }

        public static bool operator !=(ModelReference<T> a, ModelReference<T> b)
        {
            return !(a == b);
        }
    }
}
