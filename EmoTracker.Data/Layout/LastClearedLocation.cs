using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4 reactive layout element: surfaces the owning
    /// <see cref="Sessions.TrackerState"/>'s
    /// <see cref="LocationDatabase.LastClearedLocation"/> through a stable
    /// <c>Location</c> property, re-firing when the underlying database
    /// signals a change. Subscription is wired in
    /// <see cref="OnOwnerStateStamped"/> so we resolve through this
    /// element's per-state LocationDatabase.
    /// </summary>
    [JsonTypeTags("last_cleared_location")]
    public partial class LastClearedLocation : LayoutItem
    {
        [KVOverridable]
        public partial bool CompactDisplay { get; set; }

        LocationDatabase mSubscribedLocations;

        public LastClearedLocation()
        {
        }

        public override void Dispose()
        {
            UnsubscribeLocations();
            base.Dispose();
        }

        public override void OnOwnerStateStamped()
        {
            base.OnOwnerStateStamped();
            UnsubscribeLocations();
            var locations = (this.OwnerState as Sessions.TrackerState)?.Locations;
            if (locations != null)
            {
                mSubscribedLocations = locations;
                locations.PropertyChanged += Instance_PropertyChanged;
            }
            NotifyPropertyChanged("Location");
        }

        void UnsubscribeLocations()
        {
            if (mSubscribedLocations != null)
            {
                mSubscribedLocations.PropertyChanged -= Instance_PropertyChanged;
                mSubscribedLocations = null;
            }
        }

        private void Instance_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            NotifyPropertyChanged("Location");
        }

        public Location Location
        {
            get { return (this.OwnerState as Sessions.TrackerState)?.Locations.LastClearedLocation; }
        }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            definition[nameof(CompactDisplay) + "__def"] = data.GetValue<bool>("compact", true);
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            return true;
        }
    }
}
