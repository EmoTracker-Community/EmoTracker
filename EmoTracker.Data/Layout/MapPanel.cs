using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("map")]
    public class MapPanel : LayoutItem
    {
        public enum MapOrientation
        {
            Auto,
            Horizontal,
            Vertical
        };


        MapOrientation mOrientation = MapOrientation.Auto;
        public MapOrientation Orientation
        {
            get { return mOrientation; }
            set { SetProperty(ref mOrientation, value); }
        }

        ObservableCollection<Map> mMaps;

        public IEnumerable<Map> Maps
        {
            get
            {
                if (mMaps != null)
                    return mMaps;

                return MapDatabase.Instance.Maps;
            }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            if (!Data.Tracker.Instance.MapEnabled)
                return false;

            Orientation = data.GetEnumValue<MapOrientation>("orientation", MapOrientation.Auto);

            JArray mapList = data.GetValue<JArray>("maps");
            if (mapList != null)
            {
                foreach (string mapName in mapList)
                {
                    Map instance = MapDatabase.Instance.FindMap(mapName);
                    if (instance != null)
                    {
                        if (mMaps == null)
                            mMaps = new ObservableCollection<Map>();

                        mMaps.Add(instance);
                    }
                }
            }

            return true;
        }
    }
}
