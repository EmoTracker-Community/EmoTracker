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

namespace EmoTracker.Data
{
    /// <summary>
    /// Phase 7.1: <see cref="MapDatabase"/> is per-state. Each
    /// <c>TrackerState</c> owns one. Reach via the holder's
    /// <see cref="ModelTypeBase.OwnerState"/>, or via
    /// <c>ApplicationModel.Instance.PrimaryState.Maps</c> /
    /// <c>Sessions.SessionContext.ActiveState.Maps</c>.
    /// </summary>
    public class MapDatabase : ObservableObject
    {
        // Phase 6 step 11: back-reference to the owning TrackerState.
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

            this.State?.Scripts.OutputWarning("Loading Legacy Maps");
            using (new LoggingBlock())
            {
                return IncrementalLoad("maps.json", package);
            }
        }

        internal bool IncrementalLoad(string path, IGamePackage package)
        {
            this.State?.Scripts.Output("Loading Maps: {0}", path);
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
                            this.State?.Scripts.Output("File not found");
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    this.State?.Scripts.OutputException(e);
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
