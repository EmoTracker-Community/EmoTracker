using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Items;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Scripting;
using System;
using System.Collections.Generic;

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
        /// own; the static <see cref="Sessions.SessionContext.ActiveState?.Items"/> aliases the
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
        /// Phase 7.3: per-state user-toggleable settings (IgnoreAllLogic,
        /// DisplayAllLocations, AlwaysAllowClearing, AutoUnpinLocationsOnClear,
        /// PinLocationsOnItemCapture, MapEnabled, SwapLeftRight). These
        /// previously lived on <see cref="ApplicationSettings"/> and were
        /// process-wide; they now follow the state on Fork.
        /// </summary>
        public SessionSettings Settings { get; }

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

            // Phase 7.3: per-state SessionSettings. OwnerState is set after
            // the state is otherwise wired so OnChanged hooks (e.g. on
            // IgnoreAllLogic) can resolve OwnerState back-references.
            Settings = new SessionSettings();
            Settings.OwnerState = this;

            // Phase 6 step 11: wire each catalog's back-ref so peer-catalog
            // access from within the catalog (e.g. LocationDatabase calling
            // MapDatabase) resolves to the same state's instance, not the
            // ambient singleton.
            WireCatalogStateBackRefs();
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

            // Phase 6 step 10 fix: when ANY collaborator was adopted from
            // an outside source (the singleton-adoption path used by
            // ApplicationModel.RebindActivePackageInstanceFromSingletons),
            // skip the disposal cascade in our Dispose. Otherwise the old
            // primary state's teardown closes the just-re-bootstrapped
            // singleton ScriptManager's Lua interpreter, leaving the
            // newly-adopted PrimaryState pointing at a singleton with a
            // closed mLua — which manifests as NRE in
            // ScriptManager.InvokeStandardCallback and ProviderCountForCode
            // when sections fire their PropertyChanging/Changed callbacks.
            mAdoptedCollaborators =
                scripts != null || transactions != null || items != null
                || locations != null || maps != null || layouts != null;

            // Phase 7.3: per-state settings. The adoption ctor seeds from
            // ApplicationSettings.Instance's current values so a primary
            // state adopting the singletons inherits the user's already-
            // loaded preferences. (Pre-Phase-7 saves wrote these into
            // ApplicationSettings.json; reading back at startup populates
            // those fields, and adopting the seeds them onto this state.)
            Settings = new SessionSettings();
            Settings.OwnerState = this;
            ApplicationSettings.Instance.SeedIntoSession(Settings);

            WireCatalogStateBackRefs();
        }

        // True iff this state was constructed via the adoption ctor and
        // must NOT dispose its (shared) collaborators on teardown.
        readonly bool mAdoptedCollaborators;

        // Sets the State back-ref on each catalog so peer access within
        // the EmoTracker.Data catalog code (e.g. LocationDatabase reaching
        // MapDatabase) resolves to this state's catalogs rather than the
        // ambient singleton. Idempotent — safe to call multiple times if
        // a state is reconstructed (which doesn't happen in practice but
        // would still be sound).
        void WireCatalogStateBackRefs()
        {
            Items.State = this;
            Locations.State = this;
            Maps.State = this;
            Layouts.State = this;
        }

        /// <summary>
        /// Phase 6 step 11: walks every model in this state's catalogs and
        /// stamps <see cref="ModelTypeBase.OwnerState"/> = this. Called by
        /// the adoption path in ApplicationModel.RebindActivePackageInstanceFromSingletons
        /// so the primary state's models route their per-state lookups
        /// (transaction processor, script manager, peer catalogs) through
        /// this state rather than fall through to the ambient singletons.
        ///
        /// <para>
        /// The Fork path (step 8) sets OwnerState as it walks; this method
        /// is for the adoption-from-singletons case where the catalogs
        /// arrive pre-populated. Idempotent — calling on already-stamped
        /// models is a no-op.
        /// </para>
        ///
        /// <para>
        /// Layouts are not yet stamped (step 8 deferred layout fork
        /// orchestration; LayoutItem hierarchy walks land with that
        /// follow-up).
        /// </para>
        /// </summary>
        public void StampOwnerStateOnAdoptedModels()
        {
            // Items: enumerate the catalog and stamp ModelTypeBase-derived ones.
            // Phase 6 step 10 fix: also register each item in the IndexedModelResolver
            // so primary-state ModelReference<T>.Target lookups (which now go through
            // GetModelResolver() → state.Resolve()) actually find the model. Pre-step-9
            // those lookups walked Sessions.SessionContext.ActiveState?.Items.Items via the now-deleted
            // AmbientSingletonModelResolver; post-step-9 the resolver IS the index, and
            // adoption must keep it populated.
            foreach (var item in Items.Items)
            {
                if (item is ModelTypeBase mtb)
                {
                    mtb.OwnerState = this;
                    mResolver.Register(mtb);
                }
            }

            // Locations: walk the flat AllLocations + each Location's sections.
            foreach (var loc in Locations.AllLocations)
            {
                loc.OwnerState = this;
                mResolver.Register(loc);
                foreach (var sec in loc.Sections)
                {
                    sec.OwnerState = this;
                    mResolver.Register(sec);
                }
            }

            // Maps: each Map + its MapLocations.
            foreach (var map in Maps.Maps)
            {
                map.OwnerState = this;
                mResolver.Register(map);
                foreach (var ml in map.Locations)
                {
                    ml.OwnerState = this;
                    mResolver.Register(ml);
                }
            }
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
            // Phase 6 step 10 fix: skip Scripts disposal when this state
            // adopted its collaborators from outside (the singleton path).
            // The collaborators outlive the state and must not have their
            // resources torn down here — Tracker.Reload's own ScriptManager
            // Reset/Load cycle is the canonical lifecycle for the singleton's
            // Lua interpreter.
            if (!mAdoptedCollaborators)
                Scripts?.Dispose();
            mResolver.Clear();
            base.Dispose();
        }

        // -------- Phase 6 step 8: coordinated state fork ------------------

        /// <summary>
        /// Phase 6 step 8: produces a fork of this state with deep-copied
        /// model graph. The resulting state has its own catalogs (Items /
        /// Locations / Maps), its own per-state Lua interpreter (deep-cloned
        /// from this state's via <see cref="LuaStateCloner"/>), its own
        /// transaction processor and resolver. Each forked model has its
        /// <see cref="ModelTypeBase.OwnerState"/> set to the fork.
        ///
        /// <para>
        /// Forking sequence — order matters:
        /// </para>
        /// <list type="number">
        ///   <item>Allocate new TrackerState with fresh catalogs.</item>
        ///   <item>Walk source.Items.Items; <c>item.Fork()</c> each
        ///         <see cref="ItemBase"/> via Phase 2 mechanics; set
        ///         OwnerState; register in fork's resolver + ItemDatabase.
        ///         Build the model identity map (source → fork).</item>
        ///   <item>Walk source.Locations starting from Root;
        ///         <c>location.Fork()</c> cascades to sections + children
        ///         via Phase 3 coordinated fork; set OwnerState on every
        ///         resulting Location + Section; register in resolver +
        ///         LocationDatabase. Build identity map entries.</item>
        ///   <item>Walk source.Maps; <c>map.Fork()</c> cascades to
        ///         MapLocations via Phase 3; set OwnerState; register.</item>
        ///   <item>Push the model identity map into Scripts.PendingExtraBridges
        ///         so the cloner remaps closure upvalues that captured
        ///         per-state model objects.</item>
        ///   <item><c>source.Scripts.Fork()</c> → fork's ScriptManager
        ///         runs <see cref="LuaStateCloner.CloneAll"/> with the
        ///         extended bridge map; replace fork.Scripts with the new
        ///         instance.</item>
        ///   <item>Walk LuaItems on the fork; for each, call the fork
        ///         ScriptManager's <c>RewireForkedLuaItem</c> to redirect
        ///         the LuaItem's NLua field references through the fork's
        ///         interpreter.</item>
        /// </list>
        ///
        /// <para>
        /// <b>Step-8 limitation — Layouts deferred:</b> the layout catalog
        /// fork is not yet wired (Phase 4's UID registration via
        /// <c>LayoutItem.RegisterUniqueID</c> and the fork's interaction
        /// with the singleton-LayoutManager registration model is more
        /// involved). The fork's <see cref="Layouts"/> remains empty until
        /// a follow-up extends this orchestrator. For state forks that
        /// don't depend on per-state layout overrides, this is acceptable;
        /// the primary state's Layouts continue to work via the singleton
        /// shim from step 5.
        /// </para>
        /// </summary>
        public TrackerState Fork(string name = null)
        {
            var copy = new TrackerState(name ?? ("fork_" + Id.ToString().Substring(0, 8)));

            // Identity map: source-side reference → fork-side counterpart.
            // Populated as we walk the catalogs; passed to ScriptManager.Fork
            // as bridge-map extras so closure upvalues capturing models get
            // remapped at clone time.
            var modelIdentityMap = new Dictionary<object, object>();

            // ---- Items ------------------------------------------------------
            foreach (var item in this.Items.Items)
            {
                if (!(item is ItemBase srcItemBase)) continue;
                var forkedItem = (ItemBase)srcItemBase.Fork();
                forkedItem.OwnerState = copy;
                copy.Items.RegisterItem(forkedItem);
                copy.mResolver.Register(forkedItem);
                modelIdentityMap[srcItemBase] = forkedItem;
            }
            copy.Items.BuildCodeIndex();

            // ---- Locations + Sections (coordinated tree walk) --------------
            // Phase 3 Location.Fork cascades to sections + child locations
            // via the owned-subtree pattern. We fork the source's root and
            // then descend the resulting tree to register every node.
            if (this.Locations.Root != null)
            {
                var forkedRoot = (Location)this.Locations.Root.Fork();
                RegisterLocationTreeOnFork(this.Locations.Root, forkedRoot, copy, modelIdentityMap);
                copy.Locations.SetRootFromFork(forkedRoot);
            }

            // ---- Maps -------------------------------------------------------
            // Phase 3 Map.Fork cascades to MapLocations.
            foreach (var map in this.Maps.Maps)
            {
                var forkedMap = (Map)map.Fork();
                forkedMap.OwnerState = copy;
                copy.Maps.AddMapFromFork(forkedMap);
                copy.mResolver.Register(forkedMap);
                modelIdentityMap[map] = forkedMap;
            }

            // ---- Per-state AccessibilityRule cache seeding (Phase 7.2) -----
            // Deep-copy the source's cache into the fork so the fork starts
            // pre-warmed with the source's evaluations, avoiding cold-start
            // re-evaluation on first refresh. Subsequent mutations diverge
            // independently — fork mutations clear only fork's cache.
            copy.Locations.SeedRuleCacheFromFork(this.Locations);

            // ---- Per-state SessionSettings (Phase 7.3) ---------------------
            // Copy each setting value from the source into the fork. The
            // fork's Settings is a fresh ModelTypeBase (its OwnerState =
            // copy is already set in the ctor), and per-key COW on
            // MutableData would normally let us share the source's KV
            // store via InitializeAsForkOf — but tying the lifecycle to
            // ItemBase.Fork's machinery here is more boilerplate than just
            // copying the seven scalar bools, so we bulk-copy directly.
            copy.Settings.IgnoreAllLogic = this.Settings.IgnoreAllLogic;
            copy.Settings.DisplayAllLocations = this.Settings.DisplayAllLocations;
            copy.Settings.AlwaysAllowClearing = this.Settings.AlwaysAllowClearing;
            copy.Settings.AutoUnpinLocationsOnClear = this.Settings.AutoUnpinLocationsOnClear;
            copy.Settings.PinLocationsOnItemCapture = this.Settings.PinLocationsOnItemCapture;
            copy.Settings.MapEnabled = this.Settings.MapEnabled;
            copy.Settings.SwapLeftRight = this.Settings.SwapLeftRight;

            // ---- ScriptManager fork (with extended bridges) ----------------
            // copy.Scripts was constructed by the TrackerState ctor without
            // a Lua interpreter; we bootstrap one here and then drive the
            // cloner directly so the bridge map can include the model
            // identity entries built above. We bypass ScriptManager.Fork's
            // OnForked-runs-cloner-with-fixed-6-bridges shape because
            // step 8's whole point is to extend that map with per-state
            // model objects.
            copy.Scripts.BootstrapInterpreter();
            copy.Scripts.RunCloneFrom(this.Scripts, modelIdentityMap);

            // ---- LuaItem rewire (Phase 5 step 7 wiring) --------------------
            foreach (var pair in modelIdentityMap)
            {
                if (pair.Key is LuaItem srcLua && pair.Value is LuaItem forkLua)
                {
                    copy.Scripts.RewireForkedLuaItem(forkLua, srcLua);
                }
            }

            return copy;
        }

        // Walks the source location tree alongside the fork tree (parallel
        // structure thanks to Phase 3's coordinated Location.Fork) and
        // registers each fork-side Location + Section on the new state.
        // Produces (source → fork) identity-map entries for every node.
        static void RegisterLocationTreeOnFork(
            Location srcLoc, Location forkLoc, TrackerState forkState,
            Dictionary<object, object> identityMap)
        {
            forkLoc.OwnerState = forkState;
            forkState.Locations.AddLocationFromFork(forkLoc);
            forkState.mResolver.Register(forkLoc);
            identityMap[srcLoc] = forkLoc;

            // Sections — walked in parallel with the source's mSections so
            // we can build per-section identity-map entries.
            using (var srcEnum = srcLoc.Sections.GetEnumerator())
            using (var forkEnum = forkLoc.Sections.GetEnumerator())
            {
                while (srcEnum.MoveNext() && forkEnum.MoveNext())
                {
                    var srcSection = srcEnum.Current;
                    var forkSection = forkEnum.Current;
                    forkSection.OwnerState = forkState;
                    forkState.mResolver.Register(forkSection);
                    identityMap[srcSection] = forkSection;
                }
            }

            // Children — recurse.
            using (var srcEnum = srcLoc.Children.GetEnumerator())
            using (var forkEnum = forkLoc.Children.GetEnumerator())
            {
                while (srcEnum.MoveNext() && forkEnum.MoveNext())
                {
                    RegisterLocationTreeOnFork(srcEnum.Current, forkEnum.Current, forkState, identityMap);
                }
            }
        }
    }
}
