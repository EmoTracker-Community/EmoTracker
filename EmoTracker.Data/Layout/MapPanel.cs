using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4: <see cref="MapPanel"/> exposes either a pack-specified
    /// projection of a subset of maps (via the JSON <c>"maps"</c> array) or,
    /// when none is specified, all maps in the active <see cref="MapDatabase"/>.
    /// Explicit map references are stored as <see cref="ModelReference{Map}"/>
    /// so a fork's <c>Maps</c> resolves through the holder's resolver — Phase 6
    /// swaps in per-state map catalogs without touching this code.
    /// </summary>
    [JsonTypeTags("map")]
    public partial class MapPanel : LayoutItem
    {
        public enum MapOrientation
        {
            Auto,
            Horizontal,
            Vertical
        };

        [KVOverridable]
        public partial MapOrientation Orientation { get; set; }

        // Cross-reference list: Phase 2.5 framework. Null when the JSON didn't
        // specify a "maps" array (the getter then falls back to the ambient
        // MapDatabase). Populated during PopulateDefinitionData / parse.
        List<ModelReference<Map>> mMapRefs;

        public IEnumerable<Map> Maps
        {
            get
            {
                if (mMapRefs != null)
                    return mMapRefs.Select(r => r.Target).Where(m => m != null);

                return MapDatabase.Instance.Maps;
            }
        }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            definition[nameof(Orientation) + "__def"] = data.GetEnumValue<MapOrientation>("orientation", MapOrientation.Auto);
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            if (!Data.Tracker.Instance.MapEnabled)
                return false;

            JArray mapList = data.GetValue<JArray>("maps");
            if (mapList != null)
            {
                mMapRefs = new List<ModelReference<Map>>();
                foreach (string mapName in mapList)
                {
                    Map instance = MapDatabase.Instance.FindMap(mapName);
                    if (instance != null)
                    {
                        mMapRefs.Add(new ModelReference<Map>(this, instance));
                    }
                }
            }

            return true;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (MapPanel)source;
            if (src.mMapRefs != null)
            {
                mMapRefs = new List<ModelReference<Map>>(src.mMapRefs.Count);
                foreach (var r in src.mMapRefs)
                    mMapRefs.Add(r.ForFork(this));
            }
            else
            {
                mMapRefs = null;
            }
        }
    }
}
