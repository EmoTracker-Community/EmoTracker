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

// Phase 6 step 11: MapDatabase's internal ScriptManager.Instance accesses
// are pure logging.
#pragma warning disable CS0618

namespace EmoTracker.Data
{
    /// <summary>
    /// Phase 6 step 5: <see cref="MapDatabase"/> is now a regular
    /// instantiable <see cref="ObservableObject"/>. Each
    /// <c>TrackerState</c> holds one. <see cref="Instance"/> aliases
    /// <see cref="Current"/> for the existing 9 callsites.
    ///
    /// <para>
    /// Phase 6 step 11 (partial): the static <c>Current</c> / <c>Instance</c>
    /// shim is marked <see cref="System.ObsoleteAttribute"/> as a
    /// migration nudge. Full deletion requires refactoring
    /// <c>Tracker.Reload</c> to write into <c>SessionContext.ActiveState</c>'s
    /// catalogs rather than the static — deferred to a follow-up commit.
    /// </para>
    /// </summary>
    public class MapDatabase : ObservableObject
    {
        // ---- Static current-instance plumbing (replaces ObservableSingleton<T>) ----

        static MapDatabase mCurrent;

        [System.Obsolete("Phase 6: prefer (this.OwnerState as TrackerState)?.Maps for ModelTypeBase holders, or Sessions.SessionContext.ActiveState?.Maps otherwise.")]
        public static MapDatabase Current
        {
            get
            {
                if (mCurrent == null)
                    mCurrent = new MapDatabase();
                return mCurrent;
            }
        }
        [System.Obsolete("Phase 6: state-aware code installs the active state via TrackerState's catalog adoption rather than reassigning Current.")]
        public static void SetCurrent(MapDatabase database) => mCurrent = database;

        [System.Obsolete("Phase 6: prefer (this.OwnerState as TrackerState)?.Maps for ModelTypeBase holders, or Sessions.SessionContext.ActiveState?.Maps otherwise.")]
        public static MapDatabase Instance
        {
            get
            {
                return Current;
            }
        }

        // Phase 6 step 11: back-reference to the owning TrackerState (set by
        // TrackerState's adopt-existing-instances ctor in step 7 + the
        // fresh-instances ctor in step 8). Used for peer-catalog access
        // from within EmoTracker.Data.
        internal Sessions.TrackerState State { get; set; }

        ObservableCollection<Map> mMaps = new ObservableCollection<Map>();

        public IEnumerable<Map> Maps
        {
            get { return mMaps; }
        }

        public MapDatabase()
        {
        }

        public void Reset()
        {
            DisposeCollection(mMaps);
            mMaps.Clear();
        }

        public bool LegacyLoad(IGamePackage package)
        {
            //  Do not load legacy data if we already have new-style data
            if (mMaps.Count > 0)
                return true;

            ScriptManager.Instance.OutputWarning("Loading Legacy Maps");
            using (new LoggingBlock())
            {
                return IncrementalLoad("maps.json", package);
            }
        }

        internal bool IncrementalLoad(string path, IGamePackage package)
        {
            ScriptManager.Instance.Output("Loading Maps: {0}", path);
            using (new LoggingBlock())
            {
                try
                {
                    using (Stream s = package.Open(path))
                    {
                        if (s != null)
                        {
                            using (StreamReader reader = new StreamReader(s))
                            {
                                JArray maps = (JArray)JToken.ReadFrom(new JsonTextReader(reader));
                                foreach (JObject map in maps)
                                {
                                    mMaps.Add(new Map()
                                    {
                                        Name = map.GetValue<string>("name"),
                                        LocationSize = map.GetValue<double>("location_size", 70),
                                        LocationBorderThickness = map.GetValue<double>("location_border_thickness", 8),
                                        Image = ImageReference.FromPackRelativePath(package, map.GetValue<string>("img"), map.GetValue<string>("img_mods"))
                                    });
                                }
                            }
                        }
                        else
                        {
                            ScriptManager.Instance.Output("File not found");
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    ScriptManager.Instance.OutputException(e);
                    return false;
                }
            }
        }

        /// <summary>
        /// Phase 6 step 8: appends a Map to this database. Used by
        /// <c>TrackerState.Fork()</c>'s coordinated walk.
        /// </summary>
        internal void AddMapFromFork(Map map)
        {
            if (map != null)
                mMaps.Add(map);
        }

        public Map FindMap(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (Map m in mMaps)
                {
                    if (name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))
                        return m;
                }
            }

            return null;
        }

        internal void MarkVisibilityDirty()
        {
            foreach (Map m in mMaps)
            {
                foreach (MapLocation l in m.Locations)
                {
                    l.MarkVisibilityDirty();
                }
            }
        }

        internal void UpdateVisibilityIfNecessary()
        {
            foreach (Map m in mMaps)
            {
                foreach (MapLocation l in m.Locations)
                {
                    l.UpdateVisibilityIfNecessary();
                }
            }
        }

    }
}
