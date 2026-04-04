using EmoTracker.Core;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Locations
{
    public class Group : ObservableObject
    {
        ObservableCollection<Location> mLocations = new ObservableCollection<Location>();

        public IEnumerable<Location> Locations
        {
            get { return mLocations; }
        }

        private string mName;

        public string Name
        {
            get { return mName; }
            set { mName = value; NotifyPropertyChanged(); }
        }

        private bool mbHasAvailableItems = false;

        public bool HasAvailableItems
        {
            get { return mbHasAvailableItems; }
            set { mbHasAvailableItems = value; NotifyPropertyChanged(); }
        }

        public bool HasLocations
        {
            get { return mLocations.Count > 0; }
        }

        private string mColor;

        public string Color
        {
            get { return mColor; }
            set { SetProperty(ref mColor, value); }
        }

        public void AddLocation(Location location)
        {
            mLocations.Add(location);
            NotifyPropertyChanged("HasLocations");
        }
    }
}
