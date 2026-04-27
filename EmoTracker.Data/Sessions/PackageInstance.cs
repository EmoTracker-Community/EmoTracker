using EmoTracker.Core;
using System;
using System.Collections.Generic;

namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 6: represents one loaded pack and the family of states
    /// layered on top of it. Created against a (Pack, Variant) pair —
    /// the pack identity is part of the PackageInstance's identity, so
    /// switching to a different pack (or variant) constructs a new
    /// PackageInstance rather than mutating an existing one.
    ///
    /// <para>
    /// Holds the <see cref="DefinitionalState"/> (built once by the
    /// package loader, never used for interaction) and the registry of
    /// live <see cref="TrackerState"/> instances (the "primary" states
    /// the UI binds to, plus any forks created for simulation contexts).
    /// Every state in this PackageInstance shares a common pack identity
    /// — accessed through <see cref="TrackerState.PackageInstance"/>'s
    /// <see cref="GamePackage"/> / <see cref="ActiveVariant"/>.
    /// </para>
    ///
    /// <para>
    /// Lifetime is owned by <c>ApplicationModel</c>, NOT by
    /// <c>PackageManager</c>. The two concerns are deliberately
    /// decoupled:
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
    /// </summary>
    public sealed class PackageInstance : ObservableObject
    {
        readonly TrackerState mDefinitionalState;
        readonly Dictionary<Guid, TrackerState> mStates = new Dictionary<Guid, TrackerState>();

        /// <summary>
        /// The pack this instance was constructed against. Null for the
        /// bootstrap "no pack loaded" instance that exists between app
        /// startup and the first pack activation.
        /// </summary>
        public IGamePackage GamePackage { get; }

        /// <summary>
        /// The active variant of <see cref="GamePackage"/>. Null when the
        /// pack has no variants, or when this is the bootstrap instance
        /// pre-pack-load.
        /// </summary>
        public IGamePackageVariant ActiveVariant { get; }

        /// <summary>
        /// The model graph as initialized by the package-load process —
        /// items, locations, maps, layouts, scripts. Never used for
        /// interaction; new states are produced by forking this state.
        /// </summary>
        public TrackerState DefinitionalState => mDefinitionalState;

        /// <summary>
        /// Read-only view of the live, identifiable state instances.
        /// Keyed by <see cref="TrackerState.Id"/>.
        /// </summary>
        public IReadOnlyDictionary<Guid, TrackerState> States => mStates;

        /// <summary>
        /// Constructs a PackageInstance for the given (pack, variant)
        /// pair. Pass nulls for the bootstrap "no pack loaded" instance.
        /// </summary>
        public PackageInstance(IGamePackage gamePackage, IGamePackageVariant activeVariant)
        {
            GamePackage = gamePackage;
            ActiveVariant = activeVariant;
            mDefinitionalState = new TrackerState("__definitional__");
            mDefinitionalState.PackageInstance = this;
        }

        /// <summary>
        /// Bootstrap-only ctor: builds an empty PackageInstance with no
        /// pack/variant. Used by <c>ApplicationModel.PreallocatePrimaryState</c>
        /// at startup to maintain the "always-non-null PrimaryState"
        /// invariant before any pack is loaded.
        /// </summary>
        public PackageInstance() : this(null, null)
        {
        }

        /// <summary>
        /// Creates a new primary state inside this PackageInstance and
        /// stamps the back-reference. The state's catalogs are empty;
        /// callers (typically <c>PackageLoader.LoadInto</c>) populate them.
        /// </summary>
        public TrackerState CreateState(string name = null)
        {
            var state = new TrackerState(name ?? "state_" + (mStates.Count + 1));
            state.PackageInstance = this;
            mStates[state.Id] = state;
            // Phase 7.4: notify the lifecycle observer (typically the
            // ExtensionManager) so per-state extensions are attached.
            StateLifecycle.Observer?.OnStateRegistered(state);
            return state;
        }

        /// <summary>
        /// Phase 6 step 7: registers <paramref name="state"/> as a primary
        /// state on this PackageInstance, used by ApplicationModel to wrap
        /// the existing pack-load result without re-running pack load.
        /// Unlike <see cref="CreateState"/> this adopts a caller-constructed
        /// state rather than allocating a fresh one. Sets the back-reference
        /// on the adopted state.
        /// </summary>
        public void AdoptAsPrimary(TrackerState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.PackageInstance = this;
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
