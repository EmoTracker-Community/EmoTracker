using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Layout;
using System;

namespace EmoTracker.Data.Sessions
{
    /// <summary>
    /// Phase 6: a self-contained model graph for one tracking session —
    /// owns its own indexed model resolver, script manager, and (later
    /// steps) per-state catalogs / transaction processor / settings. The
    /// UI binds to a designated <i>primary</i> <see cref="TrackerState"/>;
    /// switching primary states is a pointer swap on
    /// <c>ApplicationModel.PrimaryState</c> that re-fires UI bindings
    /// without forcing a wholesale rebuild.
    ///
    /// <para>
    /// Step 2 (this commit) lands the shell: identity (<see cref="Id"/>,
    /// <see cref="Name"/>), the per-state <see cref="IndexedModelResolver"/>,
    /// a per-state <see cref="ScriptManager"/>, and the
    /// <see cref="ITrackerStateContext"/> implementation. Subsequent
    /// steps populate per-state catalogs (step 5), wire transaction
    /// routing through <see cref="ModelTypeBase.OwnerState"/> (step 4),
    /// and add the coordinated fork primitive (step 8).
    /// </para>
    ///
    /// <para>
    /// TrackerState is currently NOT used by production code paths;
    /// existing singletons remain primary until Phase 6 step 7 wires
    /// <c>ApplicationModel</c> to own a <c>PackageInstance</c> and one
    /// primary state. This keeps the migration incremental and
    /// reversible.
    /// </para>
    /// </summary>
    public sealed class TrackerState : ObservableObject, ITrackerStateContext
    {
        readonly IndexedModelResolver mResolver = new IndexedModelResolver();
        readonly Guid mId = Guid.NewGuid();
        string mName;

        /// <summary>
        /// Stable identifier for this state — fresh GUID on construction.
        /// Used by the <c>PackageInstance</c> States dictionary
        /// (arrives in step 3) to address individual states by id.
        /// </summary>
        public Guid Id => mId;

        /// <summary>
        /// Human-readable name (settable for UI labelling). Pure runtime
        /// state — no INPC plumbing wired yet because nothing currently
        /// binds it; step 7 may upgrade this if the UI needs it.
        /// </summary>
        public string Name
        {
            get => mName;
            set { SetProperty(ref mName, value); }
        }

        /// <summary>
        /// The script manager scoped to this state. Owns its own
        /// <see cref="NLua.Lua"/> interpreter; pack-author globals,
        /// LuaItem callbacks, and standard-callback dispatch all flow
        /// through this manager via <see cref="ModelTypeBase.GetScriptManager"/>'s
        /// OwnerState-preferring path.
        /// </summary>
        public ScriptManager Scripts { get; }

        // ITrackerStateContext.Scripts is typed IScriptManager. Forward via
        // explicit interface implementation so the public-facing property
        // returns the concrete ScriptManager (avoiding casts at every
        // ScriptManager-specific call site, e.g. RewireForkedLuaItem).
        IScriptManager ITrackerStateContext.Scripts => Scripts;

        /// <summary>
        /// The transaction processor scoped to this state. Each state
        /// has its own undo / redo stack; transactable property writes
        /// on a model owned by this state route through this processor
        /// (via <see cref="TransactableModelTypeBase.SetTransactableProperty"/>'s
        /// owner-resolution path). Step 4 wires this up; step 8's
        /// coordinated fork ensures every transactable model in a forked
        /// state has its <see cref="ModelTypeBase.OwnerState"/> set so
        /// the routing actually fires.
        ///
        /// <para>
        /// <b>API discipline:</b> this property exposes the processor
        /// for state-level <c>Undo()</c> / <c>ClearUndoHistory()</c> —
        /// but NOT for opening transaction scopes. Per plan §6.2 scopes
        /// are obtainable from a model
        /// (<see cref="TransactableModelTypeBase.OpenTransaction"/>),
        /// not from a state, so writes inside the scope can't
        /// accidentally cross state boundaries.
        /// </para>
        /// </summary>
        public IUndoableTransactionProcessor Transactions { get; }

        /// <summary>
        /// Per-state item database. Owns the items, sections, etc. that
        /// appear in this state's UI. Phase 6 step 5: each state has its
        /// own; the static <see cref="ItemDatabase.Current"/> aliases the
        /// active state's instance once <c>ApplicationModel</c> wires up
        /// the state-switch path in step 7.
        /// </summary>
        public ItemDatabase Items { get; }

        /// <summary>
        /// Per-state location database. Owns the location tree, sections,
        /// chest counts, captured items.
        /// </summary>
        public LocationDatabase Locations { get; }

        /// <summary>
        /// Per-state map database. Owns the map definitions and their
        /// per-state location markers.
        /// </summary>
        public MapDatabase Maps { get; }

        /// <summary>
        /// Per-state layout manager. Owns the parsed layout tree + UID
        /// registry (LayoutItem.UniqueID lookup). Phase 4's
        /// <c>LayoutItem.RegisterUniqueID</c> hook routes registrations
        /// through this manager once the per-state OwnerState wiring is
        /// in place (step 8).
        /// </summary>
        public LayoutManager Layouts { get; }

        /// <summary>
        /// The per-state model resolver. Populated by the coordinated
        /// fork (step 8) as each model is added to this state's graph;
        /// holders read through it via <see cref="ITrackerStateContext.Resolve"/>
        /// → <see cref="IndexedModelResolver.Resolve"/>.
        ///
        /// Internal access only — production code routes lookups through
        /// the <see cref="ITrackerStateContext"/> surface; the typed
        /// resolver is exposed at internal scope so the fork pipeline can
        /// register registrations efficiently without going through the
        /// interface boundary.
        /// </summary>
        internal IndexedModelResolver Resolver => mResolver;

        /// <summary>
        /// Constructs a fresh TrackerState with its own ScriptManager.
        /// Caller is responsible for populating the resolver via the fork
        /// pipeline (step 8) before any cross-reference resolution occurs.
        /// </summary>
        /// <summary>
        /// Constructs a fresh TrackerState with brand-new collaborators.
        /// Used by tests + step 8's coordinated fork (where each fork
        /// allocates its own catalogs).
        /// </summary>
        public TrackerState(string name = null)
        {
            mName = name;
            Scripts = new ScriptManager();
            Transactions = new LocalTransactionProcessorWithUndo();
            Items = new ItemDatabase();
            Locations = new LocationDatabase();
            Maps = new MapDatabase();
            Layouts = new LayoutManager();
        }

        /// <summary>
        /// Phase 6 step 7: constructs a TrackerState that <i>adopts</i>
        /// pre-existing collaborator instances rather than allocating
        /// fresh ones. Used by <c>ApplicationModel</c> to wrap the
        /// existing singleton-driven pack-load result into a primary
        /// state without re-running pack load — the primary state's
        /// catalogs ARE the active singletons. Step 8's coordinated
        /// fork still allocates fresh catalogs per fork (using the
        /// other constructor overload).
        ///
        /// <para>
        /// Any null parameter falls back to a fresh allocation, so
        /// callers can adopt only the collaborators they care about.
        /// In the typical step-7 path, every parameter is supplied:
        /// the primary state takes ownership of every existing
        /// singleton.
        /// </para>
        /// </summary>
        public TrackerState(
            string name,
            ScriptManager scripts,
            IUndoableTransactionProcessor transactions,
            ItemDatabase items,
            LocationDatabase locations,
            MapDatabase maps,
            LayoutManager layouts)
        {
            mName = name;
            Scripts = scripts ?? new ScriptManager();
            Transactions = transactions ?? new LocalTransactionProcessorWithUndo();
            Items = items ?? new ItemDatabase();
            Locations = locations ?? new LocationDatabase();
            Maps = maps ?? new MapDatabase();
            Layouts = layouts ?? new LayoutManager();
        }

        /// <inheritdoc />
        public T Resolve<T>(Guid definitionId) where T : class
        {
            return mResolver.Resolve<T>(definitionId);
        }

        /// <summary>
        /// Tear-down: disposes the per-state script manager and clears
        /// the resolver. Called by <c>PackageInstance.RemoveState</c>
        /// (step 3) and by application shutdown.
        ///
        /// <para>
        /// <b>Known gap (step 5 audit):</b> the catalogs (Items, Locations,
        /// Maps, Layouts) own per-state items / sections / layouts whose
        /// Dispose chains aren't run from here. Each catalog's existing
        /// <c>Reset()</c> / <c>Clear()</c> primitive isn't pure
        /// teardown — <c>LocationDatabase.Reset()</c> in particular
        /// allocates a fresh root Location with WPF-specific
        /// <c>pack://</c> URIs that throw outside the WPF runtime — so
        /// calling them blindly here is unsafe. Phase 6 step 7 wires up
        /// the production state lifecycle and will introduce proper
        /// <c>Dispose()</c> overrides on each catalog at that time.
        /// For step 5 alone, abandoned TrackerStates leak their catalog
        /// contents — acceptable because no production code yet creates
        /// extra states.
        /// </para>
        /// </summary>
        public override void Dispose()
        {
            Scripts?.Dispose();
            mResolver.Clear();
            base.Dispose();
        }
    }
}
