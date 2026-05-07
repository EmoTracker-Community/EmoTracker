using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using EmoTracker.Data.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace EmoTracker.Data
{
    /// <summary>
    /// Phase 7.1: <see cref="LocationDatabase"/> is per-state. Each
    /// <c>TrackerState</c> owns one. Reach via the holder's
    /// <see cref="ModelTypeBase.OwnerState"/>, or via
    /// <c>ApplicationModel.Instance.PrimaryState.Locations</c> /
    /// <c>Sessions.SessionContext.ActiveState.Locations</c>.
    /// </summary>
    public class LocationDatabase : ObservableObject, ICodeProvider
    {
        // Phase 6 step 11: back-reference to the owning TrackerState.
        internal Sessions.TrackerState State { get; set; }

        public class SuspendRefreshScope : IDisposable
        {
            // Capture the target LocationDatabase at construction so a state
            // swap mid-scope doesn't push on one instance and pop on another.
            // Construction always requires an explicit target — there is no
            // ambient state slot to fall back to.
            readonly LocationDatabase mTarget;

            public SuspendRefreshScope(LocationDatabase target)
            {
                mTarget = target;
                mTarget?.PushSuspendRefresh();
            }

            public virtual void Dispose()
            {
                mTarget?.PopSuspendRefresh();
            }
        }

        Location mRoot;
        Location mLastClearedLocation;

        ObservableCollection<Location> mAllLocations = new ObservableCollection<Location>();
        Dictionary<Location, int> mLocationIndex = new Dictionary<Location, int>();
        ObservableCollection<Location> mPinnedLocations = new ObservableCollection<Location>();
        ObservableCollection<Location> mVisibleLocations = new ObservableCollection<Location>();

        private struct SuspendRefreshRequest
        {
            public string CallStack { get; set; }
        };

        private Stack<SuspendRefreshRequest> mSuspendRefreshStack = new Stack<SuspendRefreshRequest>();

        public bool SuspendRefresh
        {
            get { return mSuspendRefreshStack.Count > 0; }
        }

        internal void PushSuspendRefresh()
        {
            mSuspendRefreshStack.Push(new SuspendRefreshRequest() { CallStack = Environment.StackTrace });
        }

        internal void PopSuspendRefresh()
        {
            if (mSuspendRefreshStack.Count == 0)
            {
                this.State?.Scripts.OutputError("PopSuspendRefresh called with no matching Push — possible over-close bug");
                System.Diagnostics.Debug.Fail("PopSuspendRefresh: underflow — more Pops than Pushes");
                return;
            }

            mSuspendRefreshStack.Pop();

            if (mSuspendRefreshStack.Count == 0)
            {
                RefreshAccessibility(bPendingOnly: true);
            }
        }


        public Location Root
        {
            get { return mRoot; }
        }

        public Location LastClearedLocation
        {
            get { return mLastClearedLocation; }
            set { SetProperty(ref mLastClearedLocation, value); }
        }

        public IEnumerable<Location> AllLocations
        {
            get { return mAllLocations; }
        }

        public IEnumerable<Location> VisibleLocations
        {
            get { return mVisibleLocations; }
        }

        public IEnumerable<Location> PinnedLocations
        {
            get { return mPinnedLocations; }
        }

        // Phase 7.2: per-state accessibility-rule cache. Lives here so it
        // shares the LocationDatabase's lifetime (one per state) and is
        // reachable from rule consumers via state.Locations.RuleCache.
        // Cloned on TrackerState.Fork via SeedRuleCacheFromFork so the fork
        // starts pre-warmed with the source's evaluations.
        readonly Locations.AccessibilityRuleCache mRuleCache = new Locations.AccessibilityRuleCache();
        internal Locations.AccessibilityRuleCache RuleCache => mRuleCache;

        public LocationDatabase()
        {
            // Phase 7.2: keep the per-instance cache enabled flag in sync
            // with the process-wide AccessibilityRule.EnableCache toggle,
            // so a unit test (or the legacy debug setting) that flips that
            // global propagates to this state's cache.
            mRuleCache.Enabled = Locations.AccessibilityRule.EnableCache;
            Locations.AccessibilityRule.EnableCacheChanged += OnEnableCacheChanged;

            // Invalidate the lazy name-index whenever the location set
            // changes membership. Lazy-rebuilt on next FindLocation call.
            mAllLocations.CollectionChanged += (_, __) => InvalidateNameIndex();
        }

        void OnEnableCacheChanged(bool enabled)
        {
            mRuleCache.Enabled = enabled;
        }

        /// <summary>
        /// Phase 7.2: seed this database's rule cache from a source
        /// database's cache (used at <c>TrackerState.Fork</c> time so the
        /// fork starts pre-warmed and avoids cold-start re-evaluation).
        /// </summary>
        internal void SeedRuleCacheFromFork(LocationDatabase source)
        {
            if (source == null) return;
            mRuleCache.Clear();
            var clone = source.mRuleCache.CloneForFork();
            // Adopt the cloned entries by replaying — we don't expose
            // bulk-set on the cache type; entries are public via Put.
            // The cache class deliberately keeps mEntries private to avoid
            // callers reaching past the API. Use a small adoption helper:
            mRuleCache.AdoptFrom(clone);
        }

        public void Reset()
        {
            LastClearedLocation = null;
            mAllLocations.Clear();
            mLocationIndex.Clear();
            mPinnedLocations.Clear();
            mVisibleLocations.Clear();
            // Stamp OwnerState + register before property setters fire so
            // any [OnChanged] hooks resolve the owning state.
            mRoot = new Location();
            mRoot.OwnerState = this.State;
            this.State?.RegisterModel(mRoot);
            mRoot.Color = "#212121";
            mRoot.OpenChestImage = ImageReference.FromExternalURI(new Uri("pack://application:,,,/EmoTracker;component/Resources/chest_open.png"));
            mRoot.ClosedChestImage = ImageReference.FromExternalURI(new Uri("pack://application:,,,/EmoTracker;component/Resources/chest_closed.png"));
        }

        public void ParseLocationVisualProperties(JObject data, LocationVisualProperties visual, IGamePackage package)
        {
            ImageReference openImg = ImageReference.FromPackRelativePath(this.State?.PackageInstance, data.GetValue<string>("chest_opened_img"));
            if (openImg != null)
                visual.OpenChestImage = openImg;

            ImageReference closedImg = ImageReference.FromPackRelativePath(this.State?.PackageInstance, data.GetValue<string>("chest_unopened_img"));
            if (closedImg != null)
                visual.ClosedChestImage = closedImg;

            string testForAlwaysAllowChestManipulation = data.GetValue<string>("always_allow_chest_manipulation");
            if (!string.IsNullOrWhiteSpace(testForAlwaysAllowChestManipulation))
            {
                visual.AlwaysAllowChestManipulation = data.GetValue<bool>("always_allow_chest_manipulation", false);
            }

            if (!object.ReferenceEquals(visual, Root))
            {
                string testForAutoUnpinOnClear = data.GetValue<string>("auto_unpin_on_clear");
                if (!string.IsNullOrWhiteSpace(testForAutoUnpinOnClear))
                {
                    visual.AutoUnpinOnClear = data.GetValue<bool>("auto_unpin_on_clear", false);
                }
            }
        }

        public bool LegacyLoad(IGamePackage package, Sessions.TrackerState state)
        {
            //  Do not load legacy data if we already have new-style data
            if (mAllLocations.Count > 0)
                return true;

            return IncrementalLoad("locations.json", package, true, state);
        }


        internal bool IncrementalLoad(string path, IGamePackage package, bool bLegacy = false, Sessions.TrackerState state = null)
        {
            if (bLegacy)
                this.State?.Scripts.OutputWarning("Loading legacy locations");
            else
                this.State?.Scripts.Output("Loading Locations: {0}", path);

            using (new LoggingBlock(state?.Scripts))
            {
                try
                {
                    PushSuspendRefresh();

                    using (Stream s = package.Open(path, state?.PackageInstance?.ActiveVariant))
                    {
                        if (s != null)
                        {
                            using (StreamReader reader = new StreamReader(s))
                            {
                                JArray locations = (JArray)JToken.ReadFrom(new JsonTextReader(reader));
                                foreach (JObject location in locations)
                                {
                                    Location child = LoadLocation(package, mRoot, location, state);
                                    if (child != null)
                                        mRoot.AddChild(child);
                                }
                            }
                        }
                        else
                        {
                            this.State?.Scripts.Output("File not found");
                        }
                    }
                }
                catch (Exception e)
                {
                    this.State?.Scripts.OutputException(e);
                }
                finally
                {
                    PopSuspendRefresh();
                }

                RefreshAccessibility();
            }

            return true;
        }

        internal AccessibilityLevel GetAccessibilityForCode(string code)
        {
            object codeObj = FindObjectForCode(code);

            Location l = codeObj as Location;
            if (l != null)
                return l.BaseAccessibilityLevel;

            Section s = codeObj as Section;
            if (s != null)
                return s.AccessibilityLevel;

            return AccessibilityLevel.Normal;
        }

        public uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibilityLevel)
        {
            //  Do all searches in lower case
            code = code.ToLower();

            maxAccessibilityLevel = GetAccessibilityForCode(code);

            string[] components = code.Split('/');
            if (components.Length > 0)
            {
                Location location = FindLocation(components[0]);
                if (location != null)
                {
                    if (components.Length >= 2)
                    {
                        Section s = location.FindSection(components[1]);
                        if (s != null)
                        {
                            if (s.AccessibilityLevel >= AccessibilityLevel.Unlockable)
                                return 1;
                        }
                    }
                    else
                    {
                        if (location.BaseAccessibilityLevel >= AccessibilityLevel.Unlockable)
                            return 1;
                    }
                }
            }

            return 0;
        }

        public object FindObjectForCode(string code)
        {
            //  Do all searches in lower case
            code = code.ToLower();

            string[] components = code.Split('/');
            if (components.Length > 0)
            {
                Location location = FindLocation(components[0]);
                if (location != null)
                {
                    if (components.Length >= 2)
                        return location.FindSection(components[1]);
                    else
                        return location;
                }
            }

            return null;
        }

        // Lazy-built case-insensitive name → Location index. Backs
        // FindLocation, which is hammered during accessibility refresh
        // for every @-prefixed code rule. The legacy linear scan
        // re-fetched each Location.Name through MutableKeyValueStore +
        // DeepCopyForStore on every call, which (per profile) burned
        // ~62ms / 437ms (14%) of refresh time on a CodeTracker lamp
        // toggle. The index amortizes that to one Name read per
        // location across the index build, hit by O(1) thereafter.
        //
        // Invalidation: cleared whenever AllLocations changes
        // membership (Add/Remove/Reset). Mid-session name renames
        // without a membership change would leave the index stale; in
        // practice Location.Name is set during pack-load and rarely
        // mutates after that. If a future feature renames Locations
        // dynamically it should call InvalidateNameIndex() explicitly.
        Dictionary<string, Location> mNameIndex;

        void EnsureNameIndex()
        {
            if (mNameIndex != null) return;
            var idx = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
            foreach (Location location in mAllLocations)
            {
                if (location == null) continue;
                var nm = location.Name;
                if (string.IsNullOrEmpty(nm)) continue;
                // Preserve "first match wins" semantics from the legacy
                // linear scan: do not overwrite an existing entry.
                if (!idx.ContainsKey(nm))
                    idx[nm] = location;
            }
            mNameIndex = idx;
        }

        internal void InvalidateNameIndex()
        {
            mNameIndex = null;
        }

        public Location FindLocation(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            EnsureNameIndex();
            return mNameIndex.TryGetValue(name, out var loc) ? loc : null;
        }

        /// <summary>
        /// Phase 6 step 8: appends a Location to this database's
        /// AllLocations + index. Used by <c>TrackerState.Fork()</c>'s
        /// coordinated walk to populate the fork's location database
        /// without re-running pack-load. Internal to the assembly so
        /// production code goes through the parse path; tests +
        /// TrackerState see it via <c>InternalsVisibleTo</c>.
        /// </summary>
        internal void AddLocationFromFork(Location location)
        {
            if (location == null) return;
            mLocationIndex[location] = mAllLocations.Count;
            mAllLocations.Add(location);

            // Mirror the pack-load guard at line 664: locations whose
            // own subtree contributes sections (HasLocalItems) belong to
            // the visible-locations index. Without this, a forked state's
            // mVisibleLocations stays empty even though its tree carries
            // sections — and LocationDatabase.Save iterates only
            // mVisibleLocations to build the JSON entries, so the entire
            // "location_database" key gets stripped from the save file
            // (the "any properties?" guard at line 863 strips the
            // wrapper when no entries were emitted). On reload Load()
            // finds no key, short-circuits, and chest counts / captured
            // items / cleared state are silently dropped.
            //
            // The sibling Group.AddLocation call from pack-load's
            // visibility branch is INTENTIONALLY omitted here: Group.Fork
            // rebuilds its own mLocationRefs via OnForked, so duplicating
            // the AddLocation call would double-register the location in
            // the group on the fork side.
            if (location.HasLocalItems)
                mVisibleLocations.Add(location);
        }

        /// <summary>
        /// Strip the synthetic <see cref="Root"/> entry from
        /// <c>mAllLocations</c> / <c>mLocationIndex</c> after a fork walk
        /// and re-index the remaining real locations to match the source
        /// state's order one-for-one.
        ///
        /// <para>
        /// <b>Why this is necessary:</b> pack-load's <c>LoadLocation</c>
        /// never adds <c>mRoot</c> to <c>mAllLocations</c> — the synthetic
        /// root is a parent placeholder that real locations attach to as
        /// children. The fork path, however, registers the root via the
        /// generic <c>RegisterLocationTreeOnFork</c> recursive walk
        /// (<c>TrackerState.cs:725</c>), which calls
        /// <see cref="AddLocationFromFork"/> for every node in the tree
        /// — root included. Without correction, fork's <c>mAllLocations</c>
        /// is shifted by one slot relative to the source's: root sits at
        /// fork-index 0, while the source's first real location sits at
        /// source-index 0.
        /// </para>
        ///
        /// <para>
        /// That shift breaks save / load round-trips. Save serialises
        /// indices using the FORK's <c>mLocationIndex</c>, so a saved
        /// reference reads e.g. <c>"3:Master Sword Pedestal"</c>. On load,
        /// <see cref="ResolvePersistableLocationReference"/> indexes into
        /// the FRESHLY-pack-loaded state's <c>mAllLocations</c> (which
        /// has no root entry), so position 3 resolves to a different
        /// location, the name-match check fails, and
        /// <see cref="Load"/> bails returning <c>false</c> — silently
        /// dropping every section's chest counts, captured items, and
        /// pinned-status state on reload.
        /// </para>
        ///
        /// <para>
        /// This method clears the locations index and rebuilds it by
        /// walking the source's <c>mAllLocations</c> in order (which
        /// already excludes root) and resolving each entry to its
        /// fork-side counterpart through <paramref name="identityMap"/>.
        /// After the call, fork-side index N maps to the same logical
        /// location as source-side index N.
        /// </para>
        ///
        /// <para>
        /// <c>mVisibleLocations</c> is left alone — it was populated by
        /// <see cref="AddLocationFromFork"/>'s <c>HasLocalItems</c> guard
        /// in the same pre-order DFS the source's pack-load used, so
        /// its order already matches the source's <c>mVisibleLocations</c>
        /// without re-walking.
        /// </para>
        /// </summary>
        internal void ReindexFromSource(LocationDatabase source, System.Collections.Generic.Dictionary<object, object> identityMap)
        {
            mAllLocations.Clear();
            mLocationIndex.Clear();
            foreach (Location srcLoc in source.mAllLocations)
            {
                if (identityMap.TryGetValue(srcLoc, out object forkObj) && forkObj is Location forkLoc)
                {
                    mLocationIndex[forkLoc] = mAllLocations.Count;
                    mAllLocations.Add(forkLoc);
                }
            }
        }

        /// <summary>
        /// Phase 6 step 8: sets <see cref="Root"/> on the fork. Used by
        /// <c>TrackerState.Fork()</c> after the source's root location is
        /// forked via Phase 3's coordinated <c>Location.Fork</c>.
        /// </summary>
        internal void SetRootFromFork(Location root)
        {
            mRoot = root;
        }

        public void PinLocation(Location location)
        {
            if (!mPinnedLocations.Contains(location))
            {
                mPinnedLocations.Insert(0, location);
            }
            else
            {
                mPinnedLocations.Move(mPinnedLocations.IndexOf(location), 0);
            }
        }

        public void UnpinLocation(Location location)
        {
            mPinnedLocations.Remove(location);
        }

        bool mbInRefresh = false;
        uint mPendingRefreshCount = 0;

        // Holder-aware script-manager lookup for standard-callback dispatch.
        // Prefers mRoot's per-state ScriptManager when a pack root has been
        // parsed; otherwise falls back to the owning state's Scripts.
        // Returns null when this LocationDatabase has no state context
        // (test scenarios) — callers must null-check.
        IScriptManager GetActiveScriptManager()
        {
            if (mRoot != null) return mRoot.GetScriptManager();
            return State?.Scripts;
        }

        // Peer-catalog access: the State back-ref is set when this
        // LocationDatabase is wired into a TrackerState. Returns null in
        // test scenarios where no state is installed.
        MapDatabase ActiveMaps()
        {
            return State?.Maps;
        }

        /// <summary>
        /// Triggers an accessibility-refresh sweep over this state's
        /// locations. Public so cross-assembly callers (ApplicationModel,
        /// extensions) can force a refresh after operations like
        /// fork-then-adopt where the fork's cached values may be stale
        /// relative to a fresh evaluation. Pass <c>bPendingOnly: true</c>
        /// for the legacy "only refresh if there are pending requests"
        /// semantics.
        /// </summary>
        public void RefreshAccessibility(bool bPendingOnly = false)
        {
            if (!SuspendRefresh)
            {
                if (!bPendingOnly)
                    ++mPendingRefreshCount;

                if (!mbInRefresh)
                {
                    bool bRefreshedAccessibility = false;


                    try
                    {
                        mbInRefresh = true;

                        using (ObservableObject.SuspendNotifications())
                        {
                            while (mPendingRefreshCount > 0)
                            {
                                mPendingRefreshCount = 0;
                                bRefreshedAccessibility = true;

                                // Phase 7.2: per-state cache clear (was static AccessibilityRule.ClearCaches).
                                mRuleCache.Clear();
                                this.State?.Scripts.ClearExpressionCache();

                                // Phase 5 step 5: route the standard-callback through the
                                // holder-aware path. mRoot is a Location (ModelTypeBase),
                                // so its GetScriptManager() override (Phase 6) returns the
                                // owning state's ScriptManager. Falls back to the singleton
                                // host when no pack is loaded (mRoot == null).
                                GetActiveScriptManager()?.InvokeStandardCallback(StandardCallback.AccessibilityUpdating);

                                if (mRoot != null)
                                    mRoot.RefreshAccessibility();

                                ActiveMaps()?.MarkVisibilityDirty();
                            }
                        } // queued PropertyChanged notifications fire here, before AccessibilityUpdated
                    }
                    finally
                    {
                        mbInRefresh = false;

                        // Do NOT clear caches here — they were built during RefreshAccessibility()
                        // above and must survive into the AccessibilityUpdated callback so that
                        // any rule evaluations triggered by that callback benefit from the cache.
                        // Clearing here negated all caching, reproducing the slow-update symptom
                        // that enable_accessibility_rule_caching was introduced to fix.
                        ActiveMaps()?.UpdateVisibilityIfNecessary();

                        if (bRefreshedAccessibility)
                        {
                            GetActiveScriptManager()?.InvokeStandardCallback(StandardCallback.AccessibilityUpdated);
                        }
                    }
                }

            }
            else
            {
                // Phase 7.2: per-state cache clear.
                mRuleCache.Clear();
                ++mPendingRefreshCount;
            }
        }

        void LoadLocationList(IGamePackage package, Location parent, JArray locationNodes, Sessions.TrackerState state)
        {
            if (locationNodes != null)
            {
                foreach (JObject location in locationNodes)
                {
                    //  NOTE: We do not assume that the parent that was passed in
                    //  is the parent that we ulimately attach to. This can be 
                    //  overriden by a `parent` attribute
                    Location child = LoadLocation(package, parent, location, state);
                    if (child != null && child.Parent != null)
                        child.Parent.AddChild(child);
                }
            }
        }

        Location LoadLocation(IGamePackage package, Location parent, JObject data, Sessions.TrackerState state)
        {
            // Stamp OwnerState before any property setters fire so any
            // [OnChanged] hooks (accessibility refresh, etc.) resolve the
            // owning state on the first invocation.
            Location instance = new Location();
            instance.OwnerState = state;
            state?.RegisterModel(instance);
            instance.Name = data.GetValue<string>("name");
            instance.ShortName = data.GetValue<string>("short_name");
            instance.Parent = parent;

            //  Allow for an explicit parent override
            string parentOverride = data.GetValue<string>("parent");
            if (!string.IsNullOrWhiteSpace(parentOverride))
            {
                Location newParent = FindObjectForCode(parentOverride) as Location;
                if (newParent != null)
                    instance.Parent = newParent;
            }

            ParseLocationVisualProperties(data, instance, package);

            string color = data.GetValue<string>("color");
            if (!string.IsNullOrWhiteSpace(color))
                instance.Color = color;

            JArray rules = data.GetValue<JArray>("access_rules");
            if (rules != null)
            {
                foreach (var entry in rules)
                {
                    instance.AccessibilityRules.AddRule(entry.Value<string>());
                }
            }

            JArray sections = data.GetValue<JArray>("sections");
            if (sections != null)
            {
                foreach (JObject sectionData in sections)
                {
                    Section section = new Section(instance);
                    section.OwnerState = state;
                    state?.RegisterModel(section);
                    ParseLocationVisualProperties(sectionData, section, package);

                    section.Name = sectionData.GetValue<string>("name");
                    section.ItemCaptureLayout = sectionData.GetValue<string>("capture_item_layout");
                    section.ShortName = sectionData.GetValue<string>("short_name");
                    section.ChestCount = section.AvailableChestCount = sectionData.GetValue<uint>("item_count", 0);
                    section.ClearAsGroup = sectionData.GetValue<bool>("clear_as_group", true);
                    section.CaptureItem = sectionData.GetValue<bool>("capture_item", false);
                    string captureBadgeOffset = sectionData.GetValue<string>("capture_badge_offset");
                    if (captureBadgeOffset != null)
                    {
                        string[] parts = captureBadgeOffset.Split(',');
                        if (parts.Length == 2 &&
                            double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ox) &&
                            double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double oy))
                        {
                            section.CaptureBadge = true;
                            section.CaptureBadgeOffsetX = ox;
                            section.CaptureBadgeOffsetY = oy;
                        }
                    }
                    section.ClearOnCapture = sectionData.GetValue<bool>("clear_on_capture", false);
                    section.CapturePersist = sectionData.GetValue<bool>("capture_persist", false);
                    section.ShowGateItem = sectionData.GetValue<bool>("show_gate_item", true);

                    JArray sectionRules = sectionData.GetValue<JArray>("access_rules");
                    if (sectionRules != null)
                    {
                        foreach (var entry in sectionRules)
                        {
                            section.AccessibilityRules.AddRule(entry.Value<string>());
                        }
                    }

                    JArray sectionVisRules = sectionData.GetValue<JArray>("visibility_rules");
                    if (sectionVisRules != null)
                    {
                        foreach (var entry in sectionVisRules)
                        {
                            section.VisibilityRules.AddRule(entry.Value<string>());
                        }
                    }

                    JArray gateRules = sectionData.GetValue<JArray>("gate_access_rules");
                    if (gateRules != null)
                    {
                        foreach (var entry in gateRules)
                        {
                            section.GateAccessibilityRules.AddRule(entry.Value<string>());
                        }
                    }

                    JArray gateBypassRules = sectionData.GetValue<JArray>("gate_bypass_rules");
                    if (gateBypassRules != null)
                    {
                        foreach (var entry in gateBypassRules)
                        {
                            section.GateBypassRules.AddRule(entry.Value<string>());
                        }
                    }

                    string thumbnailPath = sectionData.GetValue<string>("thumbnail");
                    if (!string.IsNullOrWhiteSpace(thumbnailPath))
                        section.Thumbnail = ImageReference.FromPackRelativePath(this.State?.PackageInstance, thumbnailPath);

                    section.HostedItemCode = sectionData.GetValue<string>("hosted_item");
                    section.GateItemCode = sectionData.GetValue<string>("gate_item");

                    // Phase 7.1 fix: gate on the raw HostedItemCode string
                    // rather than the resolved HostedItem instance. Item
                    // resolution at parse time can lag behind the location
                    // parse (item code-index isn't built until BuildCodeIndex
                    // runs at the end of PackageLoader.LoadInto), and per-state
                    // OwnerState routing through transactable HostedItemId
                    // can leave the cache stale until the first explicit read.
                    // The intent of the original condition was "skip empty
                    // placeholder sections that have neither chests nor a
                    // hosted item" — using the JSON-supplied code is the
                    // correct invariant for that intent and resolves at
                    // pack-author-time, not runtime.
                    if (section.ChestCount > 0 || !string.IsNullOrWhiteSpace(section.HostedItemCode))
                        instance.AddSection(section);
                }
            }

            if (instance.HasLocalItems)
            {
                mVisibleLocations.Add(instance);

                if (instance.Group != null)
                    instance.Group.AddLocation(instance);

                JArray mapEntries = data.GetValue<JArray>("map_locations");
                if (mapEntries != null)
                {
                    foreach (JObject entry in mapEntries)
                    {
                        //  TODO: We need to improve referencing to support late binding
                        Map map = ActiveMaps()?.FindMap(entry.GetValue<string>("map"));
                        if (map != null)
                        {
                            double x = entry.GetValue<double>("x");
                            double y = entry.GetValue<double>("y");
                            double size = entry.GetValue<double>("size",-1);
                            double bordersize = entry.GetValue<double>("border_thickness",-1);
                            double badgesize = entry.GetValue<double>("badge_size", -1);

                            size = size < 0 ? map.LocationSize : size;
                            bordersize = bordersize < 0 ? map.LocationBorderThickness : bordersize;

                            // Stamp OwnerState + register before setters fire.
                            MapLocation mapLocation = new MapLocation();
                            mapLocation.OwnerState = state;
                            state?.RegisterModel(mapLocation);
                            mapLocation.X = x;
                            mapLocation.Y = y;
                            mapLocation.Size = size;
                            mapLocation.BorderThickness = bordersize;
                            mapLocation.Location = instance;

                            mapLocation.AlwaysVisible = entry.GetValue<bool>("always_visible", false);
                            mapLocation.EnableBadgeHitTest = entry.GetValue<bool>("enable_badge_hit_test", false);

                            if (badgesize >= 0)
                            {
                                mapLocation.OverrideBadgeSize = true;
                                mapLocation.BadgeSize = badgesize;
                            }

                            string badgealignment = entry.GetValue<string>("badge_alignment");
                            if (!string.IsNullOrEmpty(badgealignment))
                            {
                                if (System.Enum.TryParse<EmoTracker.Data.Layout.ContentAlignment>(badgealignment, ignoreCase: true, out var alignment))
                                    mapLocation.BadgeAlignment = alignment;
                            }

                            string badgeoffset = entry.GetValue<string>("badge_offset");
                            if (!string.IsNullOrEmpty(badgeoffset))
                            {
                                var parts = badgeoffset.Split(',');
                                if (parts.Length >= 2 &&
                                    double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ox) &&
                                    double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double oy))
                                {
                                    mapLocation.BadgeOffsetX = ox;
                                    mapLocation.BadgeOffsetY = oy;
                                }
                            }

                            JArray visibilityRules = entry.GetValue<JArray>("restrict_visibility_rules", entry.GetValue<JArray>("visibility_rules"));
                            if (visibilityRules != null)
                            {
                                foreach (var rule in visibilityRules)
                                {
                                    mapLocation.RestrictVisibilityRules.AddRule(rule.Value<string>());
                                }
                            }

                            JArray forceVisibilityRules = entry.GetValue<JArray>("force_visibility_rules");
                            if (forceVisibilityRules != null)
                            {
                                foreach (var rule in forceVisibilityRules)
                                {
                                    mapLocation.ForceVisibilityRules.AddRule(rule.Value<string>());
                                }
                            }

                            JArray forceInvisibilityRules = entry.GetValue<JArray>("force_invisibility_rules");
                            if (forceInvisibilityRules != null)
                            {
                                foreach (var rule in forceInvisibilityRules)
                                {
                                    mapLocation.ForceInvisibilityRules.AddRule(rule.Value<string>());
                                }
                            }

                            map.AddLocation(mapLocation);
                        }
                    }
                }
            }

            mLocationIndex[instance] = mAllLocations.Count;
            mAllLocations.Add(instance);

            var children = data.GetValue<JArray>("children");
            LoadLocationList(package, instance, children, state);

            return instance;
        }

        #region -- Save/Load --

        internal void Save(JObject root)
        {
            JObject locationDatabaseData = new JObject();

            JArray locationDataArray = new JArray();
            {
                foreach (Location location in VisibleLocations)
                {
                    JObject locationData = new JObject();

                    locationData["location_reference"] = GetPersistableLocationReference(location);
                    locationData["modified_by_user"] = location.ModifiedByUser;

                    JArray sectionDataArray = new JArray();
                    {
                        foreach (Section section in location.Sections)
                        {
                            JObject sectionData = new JObject();

                            sectionData["section_reference"] = location.GetPersistableSectionReference(section);
                            sectionData["available_chest_count"] = section.AvailableChestCount;

                            if (section.CapturedItem != null)
                            {
                                // Phase 6 step 11: prefer the peer ItemDatabase from this state.
                                var itemDb = this.State?.Items;
                                sectionData["captured_item"] = itemDb?.GetPersistableItemReference(section.CapturedItem, allowAnyType: true);
                            }

                            sectionDataArray.Add(sectionData);
                        }
                    }

                    if (sectionDataArray.Count > 0)
                        locationData["sections"] = sectionDataArray;

                    JArray notesDataArray = location.NoteTakingSite.AsJsonArray();
                    if (notesDataArray != null)
                        locationData["notes"] = notesDataArray;

                    if (location.HasBadges)
                    {
                        JObject badgesData = new JObject();
                        foreach (var kvp in location.Badges)
                        {
                            var entry = kvp.Value;
                            JObject badgeData = new JObject();

                            if (entry.Image is ConcreteImageReference concrete)
                            {
                                string path = concrete.URI?.ToString()?.Replace("gamepackage://", "") ?? "";
                                badgeData["image"] = path;
                                if (!string.IsNullOrEmpty(concrete.Filter))
                                    badgeData["filter"] = concrete.Filter;
                            }
                            else if (entry.Image is FilterImageReference filtered && filtered.Reference is ConcreteImageReference baseRef)
                            {
                                string path = baseRef.URI?.ToString()?.Replace("gamepackage://", "") ?? "";
                                badgeData["image"] = path;
                                badgeData["filter"] = filtered.Filter;
                            }

                            if (badgeData.ContainsKey("image"))
                            {
                                badgeData["ox"] = entry.OffsetX;
                                badgeData["oy"] = entry.OffsetY;
                                badgesData[kvp.Key] = badgeData;
                            }
                        }
                        if (badgesData.Count > 0)
                            locationData["badges"] = badgesData;
                    }

                    locationDataArray.Add(locationData);
                }
            }

            if (locationDataArray.Count > 0)
                locationDatabaseData["locations"] = locationDataArray;

            JArray pinArray = new JArray();
            {
                foreach (Location pin in mPinnedLocations.Reverse())
                {
                    pinArray.Add(GetPersistableLocationReference(pin));
                }

                if (pinArray.Count > 0)
                    locationDatabaseData["pinned_locations"] = pinArray;
            }

            if (locationDatabaseData.Properties().Any())
                root["location_database"] = locationDatabaseData;
        }

        internal bool Load(JObject root)
        {
            PushSuspendRefresh();
            try
            {
                JObject locationDatabaseData = root.GetValue<JObject>("location_database");
                if (locationDatabaseData == null)
                    return true;

                JArray locationDataArray = locationDatabaseData.GetValue<JArray>("locations");
                if (locationDataArray == null)
                    return false;

                foreach (JObject locationData in locationDataArray)
                {
                    Location location = ResolvePersistableLocationReference(locationData.GetValue<string>("location_reference"));
                    if (location == null)
                        return false;

                    location.ModifiedByUser = locationData.GetValue<bool>("modified_by_user", false);

                    JArray sectionDataArray = locationData.GetValue<JArray>("sections");

                    if (sectionDataArray == null && location.Sections.Count() != 0)
                        return false;

                    if (sectionDataArray != null && location.Sections.Count() != sectionDataArray.Count)
                        return false;

                    foreach (JObject sectionData in sectionDataArray)
                    {
                        Section section = location.ResolvePersistableSectionReference(sectionData.GetValue<string>("section_reference"));
                        if (section == null)
                            return false;

                        section.AvailableChestCount = sectionData.GetValue<uint>("available_chest_count");

                        string capturedItemRef = sectionData.GetValue<string>("captured_item");
                        {
                            // Phase 6 step 11: prefer the peer ItemDatabase from this state.
                            var itemDb = this.State?.Items;
                            ITrackableItem captureItem = itemDb?.ResolvePersistableItemReference(capturedItemRef);

                            if (!string.IsNullOrWhiteSpace(capturedItemRef) && captureItem == null)
                                return false;

                            if (captureItem != null && !section.CaptureItem)
                                return false;

                            section.CapturedItem = captureItem;
                        }
                    }

                    location.NoteTakingSite.PopulateWithJsonArray(locationData.GetValue<JArray>("notes"));

                    JObject badgesData = locationData.GetValue<JObject>("badges");
                    if (badgesData != null)
                    {
                        foreach (JProperty badge in badgesData.Properties())
                        {
                            string key = badge.Name;
                            JObject badgeData = badge.Value as JObject;
                            if (badgeData == null) continue;
                            string imagePath = badgeData.GetValue<string>("image");
                            string filter = badgeData.GetValue<string>("filter");
                            double ox = badgeData.GetValue<double>("ox", 0);
                            double oy = badgeData.GetValue<double>("oy", 0);

                            if (!string.IsNullOrEmpty(imagePath))
                            {
                                ImageReference imageRef = ImageReference.FromPackRelativePath(this.State?.PackageInstance, imagePath, filter);
                                if (imageRef != null)
                                    location.AddBadge(key, imageRef, null, ox, oy);
                            }
                        }
                    }
                }

                JArray pinArray = locationDatabaseData.GetValue<JArray>("pinned_locations");
                if (pinArray != null)
                {
                    foreach (string locationRef in pinArray)
                    {
                        Location location = ResolvePersistableLocationReference(locationRef);
                        if (location == null)
                            return false;

                        location.Pinned = true;
                    }
                }

                return true;
            }
            finally
            {
                PopSuspendRefresh();
            }
        }

        public string GetPersistableLocationReference(Location location)
        {
            if (!mLocationIndex.TryGetValue(location, out int idx))
                throw new InvalidOperationException("Cannot generate persistable reference for location that is not in the LocationDatabase");

            if (!string.IsNullOrWhiteSpace(location.Name))
                return string.Format("{0}:{1}", idx, Uri.EscapeDataString(location.Name));
            else
                return string.Format("{0}", idx);
        }

        public Location ResolvePersistableLocationReference(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            string[] tokens = reference.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 1)
                return null;

            int idx = -1;
            if (!int.TryParse(tokens[0], out idx) || idx < 0 || idx > (mAllLocations.Count - 1))
                return null;

            Location location = mAllLocations[idx];

            if (tokens.Length >= 2)
            {
                if (!string.Equals(Uri.UnescapeDataString(tokens[1]), location.Name, StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            return location;
        }

        #endregion
    }
}
