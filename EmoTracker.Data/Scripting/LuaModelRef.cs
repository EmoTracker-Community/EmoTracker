using EmoTracker.Core.DataModel;
using System;

namespace EmoTracker.Data.Scripting
{
    /// <summary>
    /// C# wrapper exposed to Lua as the inner backing of a metatabled "model
    /// proxy" table. Carries a stable <see cref="ModelTypeBase.DefinitionId"/>
    /// plus the <see cref="IModelResolver"/> that should chase it; the
    /// metatable installed by <see cref="ScriptManager"/> calls
    /// <see cref="Resolve"/> on every property/method access so the proxy
    /// stays fork-safe.
    ///
    /// <para>
    /// Why a separate type from <see cref="ModelReference{T}"/>: forks are
    /// served by the <see cref="LuaStateCloner"/> walking the live Lua state.
    /// When the cloner encounters a LuaModelRef in a cloned proxy table, it
    /// synthesizes a fresh LuaModelRef bound to the destination state's
    /// resolver — so the fork's proxy table resolves into the fork's own
    /// model graph. <see cref="ModelReference{T}"/>'s holder back-reference
    /// model doesn't naturally express that "captured at proxy-creation
    /// time, swap on fork" semantic.
    /// </para>
    ///
    /// <para>
    /// Resolution is intentionally NOT cached on the wrapper itself: the
    /// metatable's <c>__index</c> / <c>__newindex</c> hooks call
    /// <see cref="Resolve"/> on every access. Caching here would be a
    /// micro-optimization invalidated on every pack reload; the resolver
    /// (typically a per-state <see cref="ITrackerStateContext"/>) is itself
    /// expected to be O(1) on the lookup so the per-access cost is bounded.
    /// </para>
    /// </summary>
    public sealed class LuaModelRef
    {
        readonly IModelResolver mResolver;
        readonly Guid mDefinitionId;

        /// <summary>
        /// Constructs a reference bound to <paramref name="resolver"/> and
        /// pointing at <paramref name="definitionId"/>. Both arguments may
        /// be defaults — an empty Guid yields <see cref="IsEmpty"/> true,
        /// a null resolver yields <see cref="Resolve"/> returning null.
        /// </summary>
        public LuaModelRef(IModelResolver resolver, Guid definitionId)
        {
            mResolver = resolver;
            mDefinitionId = definitionId;
        }

        /// <summary>
        /// Convenience factory: extracts the DefinitionId from
        /// <paramref name="target"/> and binds against
        /// <paramref name="resolver"/>. Returns null if <paramref name="target"/>
        /// is null. Used by <see cref="ScriptManager.WrapAsLuaProxy"/>.
        /// </summary>
        public static LuaModelRef ForTarget(IModelResolver resolver, ModelTypeBase target)
        {
            if (target == null) return null;
            return new LuaModelRef(resolver, target.DefinitionId);
        }

        /// <summary>The DefinitionId being chased.</summary>
        public Guid DefinitionId
        {
            get { return mDefinitionId; }
        }

        /// <summary>
        /// String form of <see cref="DefinitionId"/>, exposed to Lua so
        /// Lua-side equality (<c>__eq</c>) and the proxy cache can key on
        /// a primitive value rather than wrestling with NLua-wrapped Guid
        /// identity (each <c>__index</c>-of-DefinitionId access yields a
        /// fresh wrapper around the same Guid value, so userdata equality
        /// in Lua compares wrappers, not values).
        /// </summary>
        public string DefinitionIdString
        {
            get { return mDefinitionId.ToString(); }
        }

        /// <summary>True iff this reference points at <see cref="Guid.Empty"/>.</summary>
        public bool IsEmpty
        {
            get { return mDefinitionId == Guid.Empty; }
        }

        /// <summary>
        /// Resolves the target model in this reference's resolver graph.
        /// Returns null if the reference is empty, the resolver is null,
        /// or the target is no longer present in the resolver's graph.
        /// </summary>
        public ModelTypeBase Resolve()
        {
            if (IsEmpty || mResolver == null) return null;
            return mResolver.Resolve<ModelTypeBase>(mDefinitionId);
        }

        // ---------------------------------------------------------- Internal

        /// <summary>
        /// Cloner-internal: returns the resolver this reference is bound to.
        /// The cloner does NOT carry this resolver across — it builds a
        /// fresh LuaModelRef bound to the destination state's resolver.
        /// Exposed only so diagnostics can verify identity in tests.
        /// </summary>
        internal IModelResolver Resolver
        {
            get { return mResolver; }
        }

        /// <summary>
        /// Cloner-internal: produces a new LuaModelRef carrying the same
        /// DefinitionId, rebound to <paramref name="newResolver"/>. Called
        /// by <see cref="LuaStateCloner.CloneValue"/> when it encounters a
        /// LuaModelRef inside a table being cloned across the fork
        /// boundary, so the fork's proxy resolves through the fork's
        /// resolver rather than the source's.
        /// </summary>
        internal LuaModelRef WithResolver(IModelResolver newResolver)
        {
            return new LuaModelRef(newResolver, mDefinitionId);
        }
    }
}
