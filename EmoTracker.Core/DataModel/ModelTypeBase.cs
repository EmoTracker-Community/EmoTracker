using System;
using System.Collections.Generic;

namespace EmoTracker.Core.DataModel
{
    /// <summary>
    /// Base class for data-model-v2 model types. Stores definition data in
    /// <see cref="ImmutableData"/> (shared by reference across forks) and per-state
    /// runtime data in <see cref="MutableData"/> (per-key copy-on-write across forks).
    ///
    /// <para>
    /// Concrete leaves override <see cref="Fork"/> with a covariant return type so
    /// callers get the concrete type back without casting. Intermediate abstract types
    /// in a hierarchy do not override; only concrete leaves do. CRTP is intentionally
    /// not used because it does not compose with multi-level inheritance.
    /// </para>
    /// <para>
    /// All instances expose a stable <see cref="DefinitionId"/> that persists across
    /// forks via <see cref="ImmutableData"/>; this lets later phases identify "the same
    /// conceptual entity in a different state" without relying on reference equality.
    /// </para>
    /// </summary>
    public abstract class ModelTypeBase : ObservableObject
    {
        /// <summary>
        /// Reserved key used by <see cref="DefinitionId"/> in <see cref="ImmutableData"/>.
        /// Two leading underscores avoid collisions with property-name-derived keys.
        /// </summary>
        protected const string DefinitionIdKey = "__DefinitionId";

        ImmutableKeyValueStore mImmutableData;
        MutableKeyValueStore mMutableData;

        /// <summary>
        /// Definition data for this model type — pack-defined config and other values
        /// that don't change at runtime. Shared by reference across forks of a single
        /// definition. Subclasses' <c>[KVImmutable]</c> properties read from here.
        /// </summary>
        protected ImmutableKeyValueStore ImmutableData
        {
            get { return mImmutableData; }
            set { mImmutableData = value; }
        }

        /// <summary>
        /// Per-state runtime data. Each fork holds its own
        /// <see cref="MutableKeyValueStore"/>, layered as a copy-on-write child over the
        /// source state's mutable data. Subclasses' <c>[KVMutable]</c> /
        /// <c>[KVTransactable]</c> properties read from and write to this store.
        /// </summary>
        protected MutableKeyValueStore MutableData
        {
            get { return mMutableData; }
            set { mMutableData = value; }
        }

        /// <summary>
        /// Stable identity for "the same conceptual entity across states". Two
        /// instances produced by <see cref="Fork"/> (or independently representing the
        /// same definition in different live states) share the same value because
        /// <see cref="ImmutableData"/> is shared by reference.
        ///
        /// The <see cref="Guid"/> is generated once at definition-construction time
        /// and stored in <see cref="ImmutableData"/>; it is never regenerated.
        /// </summary>
        public Guid DefinitionId
        {
            get
            {
                return mImmutableData == null
                    ? Guid.Empty
                    : mImmutableData.GetValue<Guid>(DefinitionIdKey, Guid.Empty);
            }
        }

        /// <summary>
        /// Default ctor — allocates a fresh <see cref="MutableData"/> store and a fresh
        /// <see cref="ImmutableData"/> store seeded with a new <see cref="DefinitionId"/>.
        /// Pack-load code (or a subclass's parse step) typically replaces
        /// <see cref="ImmutableData"/> with the shared definition store after
        /// construction; subsequent forks then inherit that definition by reference.
        /// </summary>
        protected ModelTypeBase()
        {
            mMutableData = new MutableKeyValueStore();
            var seed = new Dictionary<string, object> { { DefinitionIdKey, Guid.NewGuid() } };
            mImmutableData = new ImmutableKeyValueStore(seed);
        }

        /// <summary>
        /// Allocates a fresh <see cref="MutableData"/> store and a fresh
        /// <see cref="ImmutableData"/> store seeded with a new <see cref="DefinitionId"/>,
        /// and initializes <see cref="OwnerState"/> with the supplied <see cref="ITrackerStateContext"/>.
        /// Used during pack-load to set OwnerState at construction time rather than
        /// as a post-hoc assignment.
        /// </summary>
        protected ModelTypeBase(ITrackerStateContext state)
        {
            mMutableData = new MutableKeyValueStore();
            var seed = new Dictionary<string, object> { { DefinitionIdKey, Guid.NewGuid() } };
            mImmutableData = new ImmutableKeyValueStore(seed);
            OwnerState = state;
        }

        /// <summary>
        /// Creates a copy-on-write fork of this instance into the destination
        /// state <paramref name="destOwnerState"/>: <see cref="ImmutableData"/>
        /// is shared by reference (definition data); <see cref="MutableData"/>
        /// is a new <see cref="MutableKeyValueStore"/> COW-layered over this
        /// instance's mutable state. Concrete leaves override with a covariant
        /// return type, stamp <see cref="OwnerState"/> = <paramref name="destOwnerState"/>
        /// on the fresh instance immediately after allocation, then invoke
        /// <see cref="InitializeAsForkOf"/> (which fires <see cref="OnForked"/>).
        ///
        /// <para>
        /// <paramref name="destOwnerState"/> must be non-null — every fork is
        /// born in a specific state. Cascading children (e.g. a forked
        /// Location's Sections) pass the same destination state through to
        /// their own Fork calls; the destination state propagates explicitly
        /// rather than via any ambient hand-off.
        /// </para>
        /// </summary>
        public abstract ModelTypeBase Fork(ITrackerStateContext destOwnerState);

        /// <summary>
        /// Hook invoked on the new instance at the tail of <see cref="Fork"/>, after
        /// <see cref="ImmutableData"/> and <see cref="MutableData"/> are wired. Concrete
        /// types holding sibling references override this to re-resolve and re-subscribe
        /// their references relative to the new state's siblings.
        ///
        /// Cross-state correctness for sibling references is addressed in a later phase;
        /// this hook exists from day one so the wiring point is in place.
        /// </summary>
        protected virtual void OnForked(ModelTypeBase source)
        {
        }

        /// <summary>
        /// Called by concrete leaves' <see cref="Fork"/> overrides on the freshly-
        /// allocated copy after construction: shares the source's definition store by
        /// reference, layers a COW <see cref="MutableData"/> over the source's mutable
        /// state, and invokes <see cref="OnForked"/>. Returns the same instance for
        /// fluency.
        /// </summary>
        protected void InitializeAsForkOf(ModelTypeBase source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (this.OwnerState == null)
                throw new InvalidOperationException(
                    "InitializeAsForkOf requires the fork's OwnerState to be stamped " +
                    "by the Fork(destOwnerState) override before this is called. " +
                    "OwnerState is part of construction-time identity, not a post-init " +
                    "field.");
            this.ImmutableData = source.ImmutableData;
            // Snapshot the source's mutable state at fork time so forks are
            // truly independent of subsequent source mutations. Without
            // this Flatten the COW parent-chain would leak source-side
            // writes into forks via fall-through reads.
            this.MutableData = new MutableKeyValueStore(source.MutableData);
            this.MutableData.Flatten();
            this.OnForked(source);
        }

        /// <summary>
        /// Phase 6: the owning <see cref="ITrackerStateContext"/> for this
        /// model — null when no state has claimed ownership (e.g., during
        /// definitional construction before <c>PackageInstance</c> wires
        /// up the definitional state). Set by the fork pipeline; never
        /// mutates after a model is in a live state.
        ///
        /// <para>
        /// The back-reference eliminates the need for a thread-local
        /// "current state" indirection: a model's transaction routing,
        /// resolver lookup, and (Phase 5) script-callback dispatch all
        /// read directly from <see cref="OwnerState"/>. This holds the
        /// design invariant <i>"transactions on a model are handled
        /// exclusively by the state that owns it"</i> by construction —
        /// there's no ambient context to leak across state boundaries.
        /// </para>
        ///
        /// <para>
        /// Setter is <c>public</c> for now; the fork pipeline in
        /// <c>EmoTracker.Data.Sessions.TrackerState</c> is the only
        /// intended caller. A future refinement could lock this down via
        /// <c>InternalsVisibleTo</c> from <c>EmoTracker.Core</c> to
        /// <c>EmoTracker.Data</c> if discipline ever slips, but for now
        /// the doc-comment + grep-for-callsites is sufficient.
        /// </para>
        /// </summary>
        public ITrackerStateContext OwnerState { get; set; }

        /// <summary>
        /// Returns the <see cref="IModelResolver"/> that <see cref="ModelReference{T}"/>
        /// holders on this instance should resolve through. Phase 6 overrides:
        /// when <see cref="OwnerState"/> is non-null the state IS the resolver
        /// (<see cref="ITrackerStateContext"/> inherits <see cref="IModelResolver"/>),
        /// so cross-references chase the holder's state automatically. Falls back
        /// to <see cref="ModelResolver.Current"/> for models that haven't yet been
        /// claimed by a state (e.g., during definitional pack load).
        /// </summary>
        public virtual IModelResolver GetModelResolver()
        {
            return OwnerState;
        }

        /// <summary>
        /// Returns the <see cref="IScriptManager"/> that should dispatch
        /// script-callback events on behalf of this holder. Default
        /// implementation returns the singleton <see cref="ScriptManagerHost.Current"/>
        /// (matching pre-Phase-5 behavior — every holder shares one
        /// ScriptManager). Phase 6's per-state model graphs override this
        /// to return their state's own ScriptManager, so a callback fired
        /// from a model in state A goes through state A's Lua interpreter
        /// rather than leaking into the active primary state.
        ///
        /// <para>
        /// Analogous to <see cref="GetModelResolver"/>: the indirection is
        /// in place from Phase 5 onward, but the per-state override doesn't
        /// fire until Phase 6 introduces per-state TrackerStates.
        /// </para>
        /// </summary>
        public virtual IScriptManager GetScriptManager()
        {
            return OwnerState?.Scripts;
        }
    }
}
