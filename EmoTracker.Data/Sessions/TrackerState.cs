using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
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
        /// through this manager (not the singleton) once
        /// <see cref="ScriptManagerHost.Current"/> is repointed at it
        /// in step 6.
        /// </summary>
        public ScriptManager Scripts { get; }

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
        public TrackerState(string name = null)
        {
            mName = name;
            Scripts = new ScriptManager();
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
        /// </summary>
        public override void Dispose()
        {
            Scripts?.Dispose();
            mResolver.Clear();
            base.Dispose();
        }
    }
}
