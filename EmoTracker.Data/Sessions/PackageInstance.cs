using EmoTracker.Core;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 6: represents one loaded pack and the family of states
    /// layered on top of it. Holds the <see cref="DefinitionalState"/>
    /// (built once by the package loader, never used for interaction)
    /// and the registry of live <see cref="TrackerState"/> instances
    /// (the "primary" states the UI binds to, plus any forks created
    /// for simulation contexts).
    ///
    /// <para>
    /// Lifetime is owned by <c>ApplicationModel</c> (introduced in
    /// step 7), NOT by <c>PackageManager</c>. The two concerns are
    /// deliberately decoupled:
    /// <list type="bullet">
    ///   <item><c>PackageManager</c> retains pack discovery / install /
    ///         resolution responsibilities — long-lived, app-wide.</item>
    ///   <item><c>PackageInstance</c> tracks the active session of a
    ///         pack — constructed when a pack activates, disposed when
    ///         it deactivates.</item>
    /// </list>
    /// They communicate only via events; neither holds a back-reference
    /// to the other.
    /// </para>
    ///
    /// <para>
    /// Step 3 (this commit) lands the container shell with the
    /// definitional + states surfaces. The definitional-state population
    /// (parse-phase items / locations / layouts → seeded ImmutableData)
    /// and the <see cref="CreateState"/> coordinated fork plumbing
    /// arrive in step 8 alongside the per-state catalogs from step 5.
    /// For now, callers can construct an empty PackageInstance, retrieve
    /// the (empty) definitional state, and create empty primary states
    /// for testing.
    /// </para>
    /// </summary>
    public sealed class PackageInstance : ObservableObject
    {
        readonly TrackerState mDefinitionalState;
        readonly Dictionary<Guid, TrackerState> mStates = new Dictionary<Guid, TrackerState>();
        readonly IGamePackage mPackage;
        readonly IGamePackageVariant mActiveVariant;

        /// <summary>
        /// The pack this instance wraps. Null in tests that construct a
        /// PackageInstance without a real pack — the Phase 6 step-3
        /// shell allows this; production callers always supply one.
        /// </summary>
        public IGamePackage Package => mPackage;

        /// <summary>The active variant of the pack (e.g. ALttPR's "standard" vs. "keysanity").</summary>
        public IGamePackageVariant ActiveVariant => mActiveVariant;

        /// <summary>
        /// The model graph as initialized by the package-load process —
        /// items, locations, maps, layouts, scripts. Never used for
        /// interaction; new states are produced by forking this state
        /// (step 8). For step 3 it's a fresh empty TrackerState waiting
        /// for the parse-phase population.
        /// </summary>
        public TrackerState DefinitionalState => mDefinitionalState;

        /// <summary>
        /// Read-only view of the live, identifiable state instances.
        /// Keyed by <see cref="TrackerState.Id"/>.
        /// </summary>
        public IReadOnlyDictionary<Guid, TrackerState> States => mStates;

        public PackageInstance(IGamePackage package = null, IGamePackageVariant activeVariant = null)
        {
            mPackage = package;
            mActiveVariant = activeVariant;
            mDefinitionalState = new TrackerState("__definitional__");
        }

        /// <summary>
        /// Creates a new primary state. In step 8 this becomes
        /// <c>DefinitionalState.Fork()</c> with a coordinated walk over
        /// every per-state catalog; for the step-3 shell the returned
        /// state is empty (callers can populate it for tests).
        /// </summary>
        public TrackerState CreateState(string name = null)
        {
            var state = new TrackerState(name ?? "state_" + (mStates.Count + 1));
            mStates[state.Id] = state;
            // Phase 7.4: notify the lifecycle observer (typically the
            // ExtensionManager) so per-state extensions are attached.
            StateLifecycle.Observer?.OnStateRegistered(state);
            return state;
        }

        /// <summary>
        /// Phase 6 step 7: registers <paramref name="state"/> as a primary
        /// state on this PackageInstance, used by ApplicationModel to wrap
        /// the existing singleton-based pack-load result without
        /// re-running pack load. Unlike <see cref="CreateState"/> this
        /// adopts a caller-constructed state (typically built with the
        /// adopt-the-singletons constructor overload on
        /// <see cref="TrackerState"/>) rather than allocating a fresh one.
        /// </summary>
        public void AdoptAsPrimary(TrackerState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            mStates[state.Id] = state;
            // Phase 7.4: same lifecycle hook as CreateState — adopting a
            // pre-built state still wants per-state extensions attached.
            StateLifecycle.Observer?.OnStateRegistered(state);
        }

        /// <summary>
        /// Removes and disposes the given state. Returns true if a state
        /// was removed.
        /// </summary>
        public bool RemoveState(Guid stateId)
        {
            if (mStates.TryGetValue(stateId, out var state))
            {
                mStates.Remove(stateId);
                // Phase 7.4: notify the lifecycle observer BEFORE dispose so
                // per-state extension cleanup can still query the state.
                StateLifecycle.Observer?.OnStateUnregistered(state);
                state.Dispose();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Look up a state by id. Returns null if no state with that id
        /// is registered.
        /// </summary>
        public TrackerState GetState(Guid stateId)
        {
            return mStates.TryGetValue(stateId, out var state) ? state : null;
        }

        public override void Dispose()
        {
            // Tear down the live states first, then the definitional state.
            // Order matters: live states share the definitional state's
            // ImmutableData by reference (via Phase 1 fork mechanics);
            // disposing the definitional first would leave dangling
            // references on the live states.
            // Phase 7.4: notify the lifecycle observer for each state
            // before disposing, so per-state extensions get a chance to
            // tear down cleanly.
            foreach (var state in mStates.Values)
                StateLifecycle.Observer?.OnStateUnregistered(state);
            foreach (var state in mStates.Values)
                state.Dispose();
            mStates.Clear();

            mDefinitionalState?.Dispose();

            base.Dispose();
        }
    }
}
