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
    public sealed partial class TrackerState : ObservableObject, ITrackerStateContext
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

        // Phase 7.1.h: pack metadata lives on the back-referenced
        // PackageInstance. PackageInstance is created against a (pack,
        // variant) pair, and the state inherits both through the
        // back-reference. Switching pack or variant means swapping this
        // state's PackageInstance to a new one.
        PackageInstance mPackageInstance;

        /// <summary>
        /// The <see cref="Sessions.PackageInstance"/> this state belongs
        /// to. Stamped by <see cref="PackageInstance.CreateState"/> /
        /// <see cref="PackageInstance.AdoptAsPrimary"/>. Pack metadata
        /// (<see cref="Sessions.PackageInstance.GamePackage"/> /
        /// <see cref="Sessions.PackageInstance.ActiveVariant"/>) is read
        /// through this back-reference rather than mirrored on the state
        /// itself.
        /// </summary>
        public PackageInstance PackageInstance
        {
            get => mPackageInstance;
            internal set { SetProperty(ref mPackageInstance, value); }
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

        // Phase 7.11 polish: dirty-tracking. The tab strip surfaces a
        // modified marker (•) when this flag is true; cleared by
        // <see cref="MarkClean"/> after save/load.
        bool mIsDirty;
        public bool IsDirty
        {
            get => mIsDirty;
            internal set { SetProperty(ref mIsDirty, value); }
        }

        /// <summary>Phase 7.11: mark this state as having unsaved changes.</summary>
        public void MarkDirty() { IsDirty = true; }

        /// <summary>Phase 7.11: clear the dirty marker (call on save/load).</summary>
        public void MarkClean() { IsDirty = false; }

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
        /// Registers a freshly-constructed model with this state's resolver
        /// so cross-references can find it by DefinitionId. Called by load
        /// orchestrators (LayoutManager, MapDatabase, etc.) immediately
        /// after they construct a model — paired with stamping
        /// <see cref="ModelTypeBase.OwnerState"/> = this. No-op if the model
        /// is null.
        /// </summary>
        public void RegisterModel(ModelTypeBase model)
        {
            if (model == null) return;
            mResolver.Register(model);
        }

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
            Scripts.OwnerState = this;
            Transactions = new LocalTransactionProcessorWithUndo();
            Items = new ItemDatabase();
            Locations = new LocationDatabase();
            Maps = new MapDatabase();
            Layouts = new LayoutManager();

            Settings = new SessionSettings();
            Settings.OwnerState = this;

            // Wire each catalog's back-ref so peer-catalog access (e.g.
            // LocationDatabase calling MapDatabase) resolves to this
            // state's instance.
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
            Scripts.OwnerState = this;
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
            // Each fork is born with OwnerState = copy via the state-aware
            // Fork(destState) overload — InitializeAsForkOf reads the
            // destination state from the thread-local hand-off and stamps
            // it before OnForked runs.
            foreach (var item in this.Items.Items)
            {
                if (!(item is ItemBase srcItemBase)) continue;
                var forkedItem = (ItemBase)srcItemBase.Fork(copy);
                copy.Items.RegisterItem(forkedItem);
                copy.mResolver.Register(forkedItem);
                modelIdentityMap[srcItemBase] = forkedItem;
            }
            copy.Items.BuildCodeIndex();

            // ---- Locations + Sections (coordinated tree walk) --------------
            // Phase 3 Location.Fork cascades to sections + child locations
            // via the owned-subtree pattern. The cascading children all
            // observe ForkDestination.Current = copy and stamp OwnerState
            // accordingly during their own InitializeAsForkOf.
            if (this.Locations.Root != null)
            {
                var forkedRoot = (Location)this.Locations.Root.Fork(copy);
                RegisterLocationTreeOnFork(this.Locations.Root, forkedRoot, copy, modelIdentityMap);
                copy.Locations.SetRootFromFork(forkedRoot);
            }

            // ---- Maps -------------------------------------------------------
            // Phase 3 Map.Fork cascades to MapLocations — children stamp
            // OwnerState=copy at construction via the same hand-off, and
            // their OnForked override establishes the Location subscription.
            foreach (var map in this.Maps.Maps)
            {
                var forkedMap = (Map)map.Fork(copy);
                copy.Maps.AddMapFromFork(forkedMap);
                copy.mResolver.Register(forkedMap);
                modelIdentityMap[map] = forkedMap;
                foreach (var ml in forkedMap.Locations)
                    copy.mResolver.Register(ml);
            }

            // ---- Per-state AccessibilityRule cache seeding (Phase 7.2) -----
            // Deep-copy the source's cache into the fork so the fork starts
            // pre-warmed with the source's evaluations, avoiding cold-start
            // re-evaluation on first refresh. Subsequent mutations diverge
            // independently — fork mutations clear only fork's cache.
            copy.Locations.SeedRuleCacheFromFork(this.Locations);

            // ---- Layouts ---------------------------------------------------
            // Layout.Fork's tree walk recurses into children which all
            // observe destOwnerState = copy via the state-aware Fork
            // overload. OwnerState is stamped at construction time on every
            // forked LayoutItem; each subclass's OnForked establishes any
            // necessary cross-reference cache invalidation, subscriptions,
            // and PropertyChanged fan-out.
            foreach (var pair in this.Layouts.GetLayoutsForFork())
            {
                if (pair.Value == null) continue;
                var forkedLayout = (EmoTracker.Data.Layout.Layout)pair.Value.Fork(copy);
                copy.Layouts.AddLayoutFromFork(pair.Key, forkedLayout);
                copy.mResolver.Register(forkedLayout);
                RegisterLayoutTreeInResolver(forkedLayout, copy);
            }

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

            // ---- Phase 7.11 polish: a freshly-forked state isn't dirty.
            // The transactable writes during the fork pipeline (e.g.
            // OnForked side effects, layout content stamping) set the
            // dirty flag — clear it here so the modified marker only
            // appears for user-driven mutations.
            copy.MarkClean();

            // ---- ScriptManager fork (with extended bridges) ----------------
            // copy.Scripts was constructed by the TrackerState ctor without
            // a Lua interpreter; we bootstrap one here and then drive the
            // cloner directly so the bridge map can include the model
            // identity entries built above. We bypass ScriptManager.Fork's
            // OnForked-runs-cloner-with-fixed-6-bridges shape because
            // step 8's whole point is to extend that map with per-state
            // model objects.
            // ---- AutoTracker memory segments (Phase 7.13) ------------------
            // Pre-fork each LuaMemorySegment registered in the source's
            // ScriptManager BEFORE the cloner walks pack-script tables.
            // CodeTracker-style packs cache segment refs in user tables
            // (`SEGMENTS.ItemData = ScriptHost:AddMemoryWatch(...)`); the
            // cloner's ModelTypeBase remap path resolves source-segment
            // DefinitionIds through copy's IModelResolver and substitutes
            // the fork-side segment automatically. Without seeding the
            // fork's segments BEFORE RunCloneFrom, the cloner falls
            // through to userdata pass-through and pack tables on the
            // fork keep pointing at source-state segments (whose buffer
            // is driven by the source's autotracker, often idle).
            //
            // The Lua callback (LuaMemorySegment.Callback) is NOT cloned
            // here — its handle binds to the source's interpreter and
            // can only be remapped AFTER copy.Scripts.RunCloneFrom
            // populates its ForkCloner. RewireForkedLuaSegment below
            // does that pass.
            foreach (var srcSeg in this.Scripts.MemorySegments)
            {
                var forkSeg = (AutoTracking.LuaMemorySegment)srcSeg.Fork(copy);
                copy.Scripts.AdoptForkedSegment(forkSeg);
                copy.RegisterModel(forkSeg);
                modelIdentityMap[srcSeg] = forkSeg;
            }

            copy.Scripts.BootstrapInterpreter();
            copy.Scripts.RunCloneFrom(this.Scripts, modelIdentityMap);
            // Carry the source's developer-terminal scrollback across
            // so the fork's terminal opens with the source's pack-load
            // output + init.lua diagnostics + prior transcripts — not
            // an empty buffer. (TrackerState.Fork bypasses
            // ScriptManager.OnForked which would normally do this; we
            // call the explicit seed here instead.)
            copy.Scripts.SeedLogOutputFromFork(this.Scripts);

            // ---- LuaItem rewire (Phase 5 step 7 wiring) --------------------
            foreach (var pair in modelIdentityMap)
            {
                if (pair.Key is LuaItem srcLua && pair.Value is LuaItem forkLua)
                {
                    copy.Scripts.RewireForkedLuaItem(forkLua, srcLua);
                }
            }

            // ---- LuaMemorySegment callback rewire (Phase 7.13) -------------
            // The fork's segments were created above with null Callback
            // (the source's LuaFunction binds to the source's
            // interpreter). Now that copy.Scripts has its own bootstrapped
            // interpreter + ForkCloner, clone each source's Callback into
            // the fork's interpreter and assign onto the fork's segment.
            foreach (var pair in modelIdentityMap)
            {
                if (pair.Key is AutoTracking.LuaMemorySegment srcSeg && pair.Value is AutoTracking.LuaMemorySegment forkSeg)
                {
                    copy.Scripts.RewireForkedLuaSegment(forkSeg, srcSeg);
                }
            }

            // ---- Phase 7.1.h diagnostic: dump source vs fork accessibility -
            // Temporary diagnostic to compare source's evaluated section
            // accessibility against the fork's inherited values, so we can
            // tell whether the all-red / partial-red CodeTracker symptom
            // comes from a source-side mis-evaluation, a Section.OnForked
            // inheritance gap, or fork-side post-fork divergence. Writes
            // to /tmp/codetracker_fork_trace.log; remove once the bug is
            // localized.
            try
            {
                DumpForkTrace(this, copy);
            }
            catch { /* defensive */ }

            // Notify the lifecycle observer that a fork happened. Fired
            // BEFORE the fork is registered with a PackageInstance (which
            // typically happens in the caller via AdoptAsPrimary or
            // CreateState-equivalent). Lets per-state-extension observers
            // fork their per-state data from `this` to `copy` so the
            // fork's extensions inherit the source's state.
            StateLifecycle.Observer?.OnStateForked(this, copy);

            return copy;
        }

        static void DumpForkTrace(TrackerState src, TrackerState fork)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"==== Fork trace {DateTime.Now:HH:mm:ss.fff} ====");
            sb.AppendLine($"src state id={src.Id}, fork id={fork.Id}");
            sb.AppendLine($"src.RuleCache.Count={src.Locations?.RuleCache.Count}");
            sb.AppendLine($"fork.RuleCache.Count={fork.Locations?.RuleCache.Count}");

            // Walk both trees in parallel via the resolver. Compare cached
            // accessibility for each section.
            sb.AppendLine("--- Sections (src level | fork level | section name) ---");
            int total = 0, mismatches = 0;
            if (src.Locations?.Root != null && fork.Locations?.Root != null)
                WalkSections(src.Locations.Root, fork.Locations.Root, sb, ref total, ref mismatches);
            sb.AppendLine($"--- total sections compared: {total}, mismatches: {mismatches} ---");

            try
            {
                System.IO.File.AppendAllText("/tmp/codetracker_fork_trace.log", sb.ToString());
            }
            catch { /* defensive */ }
        }

        static void WalkSections(Location srcLoc, Location forkLoc, System.Text.StringBuilder sb, ref int total, ref int mismatches)
        {
            using (var srcEnum = srcLoc.Sections.GetEnumerator())
            using (var forkEnum = forkLoc.Sections.GetEnumerator())
            {
                while (srcEnum.MoveNext() && forkEnum.MoveNext())
                {
                    total++;
                    var s = srcEnum.Current;
                    var f = forkEnum.Current;
                    var srcLevel = s.AccessibilityLevel;
                    var forkLevel = f.AccessibilityLevel;
                    var marker = (srcLevel == forkLevel) ? "  " : "**";
                    if (srcLevel != forkLevel) mismatches++;
                    sb.AppendLine($"{marker} {srcLevel,-15} | {forkLevel,-15} | {s.Name ?? "(unnamed)"}");
                }
            }
            using (var srcEnum = srcLoc.Children.GetEnumerator())
            using (var forkEnum = forkLoc.Children.GetEnumerator())
            {
                while (srcEnum.MoveNext() && forkEnum.MoveNext())
                {
                    WalkSections(srcEnum.Current, forkEnum.Current, sb, ref total, ref mismatches);
                }
            }
        }

        // Walks a freshly-forked layout tree and registers each LayoutItem
        // with the destination state's resolver. OwnerState is already set
        // at construction time via the Fork(destState) → ForkDestination
        // hand-off + InitializeAsForkOf — no late stamping needed; the walk
        // here is purely for resolver indexing.
        //
        // Cross-references (ButtonPopup.Layout, LayoutReference.Layout,
        // MapPanel.Maps) point at separately-forked siblings and are NOT
        // walked here — those siblings (Layouts, Maps) are forked +
        // registered by their own catalog walks above.
        static void RegisterLayoutTreeInResolver(EmoTracker.Core.DataModel.ModelTypeBase node, TrackerState state)
        {
            if (node == null || state == null) return;
            state.mResolver.Register(node);

            // ItemGrid layout-items wrap a Data.Items.ItemGrid that holds
            // resolved item references; the grid's rows must be rebuilt
            // against the fork's items so the panel renders the right
            // state's catalog. OwnerState is already set; this is the
            // grid-specific post-construction trigger.
            if (node is EmoTracker.Data.Layout.ItemGrid itemGrid)
                itemGrid.ResolveRowsAgainstOwnerState();

            switch (node)
            {
                case EmoTracker.Data.Layout.Layout layout:
                    if (layout.Root != null)
                        RegisterLayoutTreeInResolver(layout.Root, state);
                    break;
                case EmoTracker.Data.Layout.LayoutItem item:
                    foreach (var child in item.EnumerateChildren())
                        RegisterLayoutTreeInResolver(child, state);
                    break;
            }
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
