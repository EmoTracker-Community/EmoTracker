using EmoTracker.Core;
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
    public class LocationDatabase : ObservableSingleton<LocationDatabase>, ICodeProvider
    {
        public class SuspendRefreshScope : IDisposable
        {
            public SuspendRefreshScope()
            {
                LocationDatabase.Instance.PushSuspendRefresh();
            }

            public virtual void Dispose()
            {
                LocationDatabase.Instance.PopSuspendRefresh();
            }
        }

        Location mRoot;
        Location mLastClearedLocation;

        ObservableCollection<Location> mAllLocations = new ObservableCollection<Location>();
        Dictionary<Location, int> mLocationIndex = new Dictionary<Location, int>();
        ObservableCollection<Location> mPinnedLocations = new ObservableCollection<Location>();
        ObservableCollection<Location> mVisibleLocations = new ObservableCollection<Location>();

        public bool SuspendRefresh
        {
            get { return mSuspendRefreshCount > 0; }
            set
            {
                // Legacy compatibility: direct assignment is discouraged.
                // Prefer SuspendRefreshScope for reentrant-safe scoping.
                if (value)
                    PushSuspendRefresh();
                else
                    PopSuspendRefresh();
            }
        }

        internal void PushSuspendRefresh()
        {
            ++mSuspendRefreshCount;
        }

        internal void PopSuspendRefresh()
        {
            if (mSuspendRefreshCount <= 0)
            {
                ScriptManager.Instance.OutputError("PopSuspendRefresh called with no matching Push — possible over-close bug");
                System.Diagnostics.Debug.Fail("PopSuspendRefresh: underflow — more Pops than Pushes");
                return;
            }

            --mSuspendRefreshCount;

            if (mSuspendRefreshCount == 0)
            {
                RefeshAccessibility(bPendingOnly: true);
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

        public LocationDatabase()
        {
        }

        public void Reset()
        {
            LastClearedLocation = null;
            mAllLocations.Clear();
            mLocationIndex.Clear();
            mPinnedLocations.Clear();
            mVisibleLocations.Clear();
            mRoot = new Location()
            {
                Color = "#212121",
                OpenChestImage = ImageReference.FromExternalURI(new Uri("pack://application:,,,/EmoTracker;component/Resources/chest_open.png")),
                ClosedChestImage = ImageReference.FromExternalURI(new Uri("pack://application:,,,/EmoTracker;component/Resources/chest_closed.png"))
            };
        }

        public void ParseLocationVisualProperties(JObject data, LocationVisualProperties visual, IGamePackage package)
        {
            ImageReference openImg = ImageReference.FromPackRelativePath(package, data.GetValue<string>("chest_opened_img"));
            if (openImg != null)
                visual.OpenChestImage = openImg;

            ImageReference closedImg = ImageReference.FromPackRelativePath(package, data.GetValue<string>("chest_unopened_img"));
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

        public bool LegacyLoad(IGamePackage package)
        {
            //  Do not load legacy data if we already have new-style data
            if (mAllLocations.Count > 0)
                return true;

            return IncrementalLoad("locations.json", package, true);
        }


        internal bool IncrementalLoad(string path, IGamePackage package, bool bLegacy = false)
        {
            if (bLegacy)
                ScriptManager.Instance.OutputWarning("Loading legacy locations");
            else
                ScriptManager.Instance.Output("Loading Locations: {0}", path);

            using (new LoggingBlock())
            {
                try
                {
                    PushSuspendRefresh();

                    using (Stream s = package.Open(path))
                    {
                        if (s != null)
                        {
                            using (StreamReader reader = new StreamReader(s))
                            {
                                JArray locations = (JArray)JToken.ReadFrom(new JsonTextReader(reader));
                                foreach (JObject location in locations)
                                {
                                    Location child = LoadLocation(package, mRoot, location);
                                    if (child != null)
                                        mRoot.AddChild(child);
                                }
                            }
                        }
                        else
                        {
                            ScriptManager.Instance.Output("File not found");
                        }
                    }
                }
                catch (Exception e)
                {
                    ScriptManager.Instance.OutputException(e);
                }
                finally
                {
                    PopSuspendRefresh();
                }

                RefeshAccessibility();
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

        public Location FindLocation(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (Location location in AllLocations)
                {
                    if (name.Equals(location.Name, StringComparison.OrdinalIgnoreCase))
                        return location;
                }
            }

            return null;
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

        int mSuspendRefreshCount = 0;
        bool mbInRefresh = false;
        uint mPendingRefreshCount = 0;

        internal void RefeshAccessibility(bool bPendingOnly = false)
        {
            if (mSuspendRefreshCount == 0)
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

                                AccessibilityRule.ClearCaches();
                                ScriptManager.Instance.ClearExpressionCache();

                                ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.AccessibilityUpdating);

                                if (mRoot != null)
                                    mRoot.RefreshAccessibility();

                                MapDatabase.Instance.MarkVisibilityDirty();
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
                        MapDatabase.Instance.UpdateVisibilityIfNecessary();

                        if (bRefreshedAccessibility)
                        {
                            ScriptManager.Instance.InvokeStandardCallback(ScriptManager.StandardCallback.AccessibilityUpdated);
                        }
                    }
                }

            }
            else
            {
                AccessibilityRule.ClearCaches();
                ++mPendingRefreshCount;
            }
        }

        void LoadLocationList(IGamePackage package, Location parent, JArray locationNodes)
        {
            if (locationNodes != null)
            {
                foreach (JObject location in locationNodes)
                {
                    //  NOTE: We do not assume that the parent that was passed in
                    //  is the parent that we ulimately attach to. This can be 
                    //  overriden by a `parent` attribute
                    Location child = LoadLocation(package, parent, location);
                    if (child != null && child.Parent != null)
                        child.Parent.AddChild(child);
                }
            }
        }

        Location LoadLocation(IGamePackage package, Location parent, JObject data)
        {
            Location instance = new Location()
            {
                Name = data.GetValue<string>("name"),
                ShortName = data.GetValue<string>("short_name"),
                Parent = parent
            };

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
                    ParseLocationVisualProperties(sectionData, section, package);

                    section.Name = sectionData.GetValue<string>("name");
                    section.ItemCaptureLayout = sectionData.GetValue<string>("capture_item_layout");
                    section.ShortName = sectionData.GetValue<string>("short_name");
                    section.ChestCount = section.AvailableChestCount = sectionData.GetValue<uint>("item_count", 0);
                    section.ClearAsGroup = sectionData.GetValue<bool>("clear_as_group", true);
                    section.CaptureItem = sectionData.GetValue<bool>("capture_item", false);
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
                        section.Thumbnail = ImageReference.FromPackRelativePath(package, thumbnailPath);

                    section.HostedItemCode = sectionData.GetValue<string>("hosted_item");
                    section.GateItemCode = sectionData.GetValue<string>("gate_item");

                    if (section.ChestCount > 0 || section.HostedItem != null)
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
                        Map map = MapDatabase.Instance.FindMap(entry.GetValue<string>("map"));
                        if (map != null)
                        {
                            double x = entry.GetValue<double>("x");
                            double y = entry.GetValue<double>("y");
                            double size = entry.GetValue<double>("size",-1);
                            double bordersize = entry.GetValue<double>("border_thickness",-1);
                            double badgesize = entry.GetValue<double>("badge_size", -1);

                            size = size < 0 ? map.LocationSize : size;
                            bordersize = bordersize < 0 ? map.LocationBorderThickness : bordersize;

                            MapLocation mapLocation = new MapLocation() { X = x, Y = y, Size = size, BorderThickness = bordersize, Location = instance };

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
                                if (System.Enum.TryParse<Locations.BadgeAlignment>(badgealignment, ignoreCase: true, out var alignment))
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
            LoadLocationList(package, instance, children);

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
                                sectionData["captured_item"] = ItemDatabase.Instance.GetPersistableItemReference(section.CapturedItem, allowAnyType: true);

                            sectionDataArray.Add(sectionData);
                        }
                    }

                    if (sectionDataArray.Count > 0)
                        locationData["sections"] = sectionDataArray;

                    JArray notesDataArray = location.NoteTakingSite.AsJsonArray();
                    if (notesDataArray != null)
                        locationData["notes"] = notesDataArray;

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
                            ITrackableItem captureItem = ItemDatabase.Instance.ResolvePersistableItemReference(capturedItemRef);

                            if (!string.IsNullOrWhiteSpace(capturedItemRef) && captureItem == null)
                                return false;

                            if (captureItem != null && !section.CaptureItem)
                                return false;

                            section.CapturedItem = captureItem;
                        }
                    }

                    location.NoteTakingSite.PopulateWithJsonArray(locationData.GetValue<JArray>("notes"));
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
