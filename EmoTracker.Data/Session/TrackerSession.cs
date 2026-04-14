using EmoTracker.Core;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Items;
using EmoTracker.Data.Layout;
using EmoTracker.Data.Locations;
using System;
using System.Threading;

namespace EmoTracker.Data.Session
{
    /// <summary>
    /// Aggregates all per-session tracker state. The session owns the transaction
    /// processor, session-scoped settings facade, item state store, location state
    /// store, accessibility evaluator, and the seven former data-layer singletons
    /// (<c>Tracker</c>, <c>ItemDatabase</c>, <c>LocationDatabase</c>,
    /// <c>MapDatabase</c>, <c>LayoutManager</c>, <c>ScriptManager</c>,
    /// <c>ApplicationSettings</c>).
    ///
    /// Phase 6 retired the <c>.Instance</c> accessors on those seven types: all
    /// callers (C# + XAML) now route through <c>TrackerSession.Current</c>, and
    /// the types themselves are plain <c>ObservableObject</c>s constructed by the
    /// session ctor in dependency order. <see cref="DesignInstance"/> bootstraps
    /// a minimal session for XAML design-time previewers.
    ///
    /// Phase 7 introduced <see cref="EnterScope"/> / <see cref="Fork"/>: the
    /// <see cref="Current"/> accessor is backed by an <see cref="AsyncLocal{T}"/>
    /// override so simulation code can run a cloned session on a background
    /// flow without disturbing the UI-thread default. A fork aliases the
    /// pack-loaded catalogs (<c>Items</c>, <c>Locations</c>, <c>Maps</c>,
    /// <c>Layouts</c>, <c>Scripts</c>, <c>Tracker</c>, <c>Global</c>) and
    /// clones the mutable stores (<c>ItemStates</c>, <c>LocationStates</c>,
    /// <c>Evaluator</c> cache, <c>Settings</c>) plus spawns a fresh transaction
    /// processor (empty undo stack). See the plan at
    /// <c>twinkly-gathering-turing.md</c> Phase 7 for the forking model and
    /// the known constraints (no save/load, no pack reload, one Lua interpreter
    /// shared with parent).
    ///
    /// Note: <c>ApplicationModel</c>, <c>PackageManager</c>, and
    /// <c>ExtensionManager</c> remain singletons by design — they live in the UI
    /// / Extensions layer, outside the session's ownership boundary.
    /// </summary>
    public class TrackerSession : ObservableObject
    {
        // Phase 7: Current is now AsyncLocal-scoped. The "default" session is
        // the one constructed by CreateCurrent() at app startup; any flow that
        // enters a Fork scope sees the forked session as Current until the
        // scope disposes. AsyncLocal (vs [ThreadStatic]) composes with
        // Task.Run / await via ExecutionContext flow.
        static TrackerSession sDefault;
        static readonly AsyncLocal<TrackerSession> sScopeOverride = new AsyncLocal<TrackerSession>();

        public static TrackerSession Current => sScopeOverride.Value ?? sDefault;

        /// <summary>The root (non-forked) session. Equal to <see cref="Current"/> unless a fork scope is active.</summary>
        public static TrackerSession Default => sDefault;

        /// <summary>
        /// Design-time-only session handle for XAML previewers. Returns the
        /// runtime <c>Current</c> if one exists; otherwise a freshly-built
        /// session. The previewer never tears this down, so we simply leak it
        /// for the lifetime of the design surface.
        /// </summary>
        public static TrackerSession DesignInstance
        {
            get
            {
                if (Current != null)
                    return Current;

                // Bootstrap a minimal session so XAML design-time bindings have
                // something to resolve against. Only safe to call from the
                // designer process; the runtime always has Current set early in
                // App startup.
                return CreateCurrent();
            }
        }

        /// <summary>The session this session was forked from, or null if this is the root.</summary>
        public TrackerSession Parent { get; }

        /// <summary>True iff this session is a fork of another session (see <see cref="Fork"/>).</summary>
        public bool IsFork => Parent != null;

        public Tracker Tracker { get; }
        public ItemDatabase Items { get; }

        /// <summary>
        /// Read-only view of the loaded item set (Phase 3 of the refactor).
        /// Forwards to <c>Items.Catalog</c> for convenience; conceptually the
        /// shared/immutable half of the item state split.
        /// </summary>
        public ItemCatalog ItemCatalog => Items?.Catalog;

        /// <summary>
        /// Per-session mutable property store backing every item's transactable
        /// properties (Phase 3). Phase 7 hoisted this onto the session directly
        /// so <see cref="Fork"/> can swap in a cloned store without any
        /// plumbing through <see cref="ItemDatabase"/>.
        /// </summary>
        public ItemStateStore ItemStates { get; }

        public LocationDatabase Locations { get; }

        /// <summary>
        /// Per-session mutable property store backing every Location and Section's
        /// transactable properties (Phase 4). Phase 7: owned directly by the
        /// session; <see cref="Fork"/> deep-clones.
        /// </summary>
        public LocationStateStore LocationStates { get; }

        /// <summary>
        /// Per-session accessibility evaluator (Phase 4). Hosts what was a
        /// process-wide static cache on <see cref="AccessibilityRule"/>; routed
        /// through here so a forked session's evaluation doesn't poison the
        /// parent's cache once item state diverges.
        /// </summary>
        public AccessibilityEvaluator Evaluator { get; }
        public MapDatabase Maps { get; }
        public LayoutManager Layouts { get; }
        public ScriptManager Scripts { get; }

        /// <summary>Global (non-session) settings: persisted UI preferences, app-wide state.</summary>
        public ApplicationSettings Global { get; }

        /// <summary>Session-scoped tracker flags (IgnoreAllLogic, DisplayAllLocations, etc.).</summary>
        public SessionSettings Settings { get; }

        /// <summary>Transaction processor owned by this session. Undo stack is session-local.</summary>
        public ITransactionProcessor Transactions { get; private set; }

        /// <summary>
        /// Root-session ctor: constructs every subsystem from scratch. The ctor
        /// publishes itself as <see cref="Default"/> up front so that subsystem
        /// ctors below can resolve <see cref="Current"/> during their own
        /// construction (e.g. <c>LocationDatabase</c>'s root <c>Location</c>,
        /// which reads <c>TrackerSession.Current.LocationStates</c> via its
        /// <c>TransactableObject.PropertyStore</c> override).
        /// </summary>
        private TrackerSession()
        {
            Parent = null;
            sDefault = this;

            // Settings first; drives persisted flag loading. ApplicationSettings'
            // parameterless ctor reads application_settings.json from disk.
            Global = new ApplicationSettings();

            // Location-tree and item mutable state + accessibility cache must
            // exist before LocationDatabase / ItemDatabase are touched — their
            // ctors construct transactable objects whose first property write
            // resolves through TrackerSession.Current.{LocationStates,ItemStates}.
            ItemStates = new ItemStateStore();
            LocationStates = new LocationStateStore();
            Evaluator = new AccessibilityEvaluator();

            // LocationDatabase must be constructed before the transaction
            // processor so the processor can inject it for Undo().
            Locations = new LocationDatabase();

            Transactions = new Core.Transactions.Processors.LocalTransactionProcessorWithUndo(Locations);

            Items = new ItemDatabase();
            Maps = new MapDatabase();
            Layouts = new Layout.LayoutManager();
            Scripts = new ScriptManager();

            Settings = new SessionSettings(Global);

            Tracker = new Tracker();
        }

        /// <summary>
        /// Fork ctor: aliases the parent's pack graph (items, locations, maps,
        /// layouts, Tracker, Global) and spawns fresh mutable stores seeded
        /// from the parent's current state. Also builds a fresh
        /// <see cref="ScriptManager"/> with its own NLua.Lua interpreter,
        /// populated by replaying the parent's cached pack script sources
        /// inside a fork scope. Pack Lua re-executes against the fork's Lua,
        /// re-binding every LuaItem's *Func properties into the fork's
        /// per-session bindings dict; shared LuaItem identity is preserved
        /// because <see cref="ScriptManager.CreateLuaItem"/> returns the
        /// N'th shared instance during replay.
        /// </summary>
        private TrackerSession(TrackerSession parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            Parent = parent;

            // Aliased (shared by reference). Safe to share because mutations
            // route through session-scoped stores, not through these object
            // identities. Scripts is NOT aliased — see below.
            Tracker = parent.Tracker;
            Items = parent.Items;
            Locations = parent.Locations;
            Maps = parent.Maps;
            Layouts = parent.Layouts;
            Global = parent.Global;

            // Cloned (deep). A fork sees its own mutable property bags seeded
            // from parent's current values; subsequent mutations on either side
            // are independent.
            ItemStates = parent.ItemStates.Clone();
            LocationStates = parent.LocationStates.Clone();
            Evaluator = parent.Evaluator.Clone();
            Settings = new SessionSettings(parent.Global);

            // Fresh transaction processor. Undo stack is intentionally empty on
            // fork; redo frames that referenced parent's transactions would be
            // unsafe to replay against a diverged state.
            Transactions = new Core.Transactions.Processors.LocalTransactionProcessorWithUndo(Locations);

            // Fresh ScriptManager with its own NLua.Lua. Inherit the package
            // reference, the cached script sources, and the shared LuaItem
            // list from parent, then Rebuild() under this fork's scope so
            // replay's side-effects (Tracker/Layout interface lookups, LuaItem
            // *Func binding assignments) resolve against fork state.
            Scripts = new ScriptManager();
            Scripts.InheritFrom(parent.Scripts);
            using (EnterScope())
            {
                Scripts.Rebuild();
            }
        }

        /// <summary>
        /// Constructs the process-wide session. Must be called once during
        /// application startup, after settings load.
        /// </summary>
        public static TrackerSession CreateCurrent()
        {
            if (sDefault != null)
                throw new InvalidOperationException("TrackerSession.Default is already constructed.");

            return new TrackerSession();
        }

        /// <summary>
        /// Creates a forked session sharing the pack graph with this session but
        /// carrying independent mutable state. Use together with
        /// <see cref="EnterScope"/>:
        /// <code>
        /// var fork = TrackerSession.Current.Fork();
        /// using (fork.EnterScope()) {
        ///     // mutations on fork; parent is untouched
        /// }
        /// </code>
        ///
        /// Constraints (see Phase 7 plan):
        /// - No save/load on a fork.
        /// - Pack reload invalidates all outstanding forks.
        /// - Fork construction replays every cached pack script against a new
        ///   NLua.Lua, which costs single-to-double-digit ms per MB of pack
        ///   Lua. Fork scopes are intended for short-lived simulation work,
        ///   not UI latency paths.
        /// </summary>
        public TrackerSession Fork()
        {
            return new TrackerSession(this);
        }

        /// <summary>
        /// Activates this session as <see cref="Current"/> on the current
        /// execution flow (AsyncLocal scope). Dispose to restore the previous
        /// <see cref="Current"/>.
        /// </summary>
        public IDisposable EnterScope()
        {
            var previous = sScopeOverride.Value;
            sScopeOverride.Value = this;
            // Transactions static fallback is set here so TransactionProcessor.Current
            // picks up this session's processor while the scope is active. The
            // processor resolves via Current anyway (see TransactionProcessor.cs),
            // but we belt-and-brace by re-publishing for any legacy path that
            // cached the static.
            return new Scope(previous);
        }

        sealed class Scope : IDisposable
        {
            readonly TrackerSession mPrevious;
            bool mDisposed;
            public Scope(TrackerSession previous) { mPrevious = previous; }
            public void Dispose()
            {
                if (mDisposed) return;
                mDisposed = true;
                sScopeOverride.Value = mPrevious;
            }
        }
    }
}
