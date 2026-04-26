using EmoTracker.Core.DataModel;
using System;

namespace EmoTracker.Data.Core.DataModel
{
    /// <summary>
    /// Phase 6 step 9: ambient resolver that delegates to whichever
    /// <see cref="EmoTracker.Data.Sessions.TrackerState"/> is currently
    /// active. Replaces the Phase 2.5 <c>AmbientSingletonModelResolver</c>'s
    /// linear scan over the legacy singleton catalogs with an O(1)
    /// <see cref="IndexedModelResolver"/> hit on the active state.
    ///
    /// <para>
    /// Resolution path:
    /// <list type="number">
    ///   <item>Read <see cref="EmoTracker.Data.Sessions.SessionContext.ActiveState"/>.</item>
    ///   <item>If non-null, delegate to its <see cref="IModelResolver.Resolve{T}"/>
    ///         (which routes through the state's <see cref="IndexedModelResolver"/>).</item>
    ///   <item>If null (the pre-pack-load window or test scenarios that
    ///         don't go through <c>ApplicationModel</c>), return null.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Installed once at app startup via
    /// <c>ModelResolver.Current = new PrimaryStateModelResolver();</c>.
    /// Production callers go through <see cref="ModelTypeBase.GetModelResolver"/>
    /// which prefers <see cref="ModelTypeBase.OwnerState"/> directly when
    /// the holder has been claimed by a state — this resolver is the
    /// fallback for unclaimed holders only (definitional construction
    /// before adoption, tests, tooling).
    /// </para>
    /// </summary>
    public sealed class PrimaryStateModelResolver : IModelResolver
    {
        public T Resolve<T>(Guid definitionId) where T : class
        {
            if (definitionId == Guid.Empty)
                return null;

            var state = Sessions.SessionContext.ActiveState;
            if (state == null)
                return null;

            return state.Resolve<T>(definitionId);
        }
    }
}
