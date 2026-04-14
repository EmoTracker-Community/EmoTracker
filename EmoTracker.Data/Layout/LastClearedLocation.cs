using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("last_cleared_location")]
    public class LastClearedLocation : LayoutItem
    {
        bool mbDisplayCompact = true;

        public LastClearedLocation()
        {
            TrackerSession.Current.Locations.PropertyChanged += Instance_PropertyChanged;
        }

        private void Instance_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            NotifyPropertyChanged("Location");
        }

        public Location Location
        {
            get { return TrackerSession.Current.Locations.LastClearedLocation; }
        }

        public bool CompactDisplay
        {
            get { return mbDisplayCompact; }
            set { SetProperty(ref mbDisplayCompact, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            CompactDisplay = data.GetValue<bool>("compact", true);
            return true;
        }
    }
}
