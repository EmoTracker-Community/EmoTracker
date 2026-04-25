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
    /// Phase 4 reactive layout element: surfaces
    /// <see cref="LocationDatabase.LastClearedLocation"/> through a stable
    /// <c>Location</c> property, re-firing when the underlying database
    /// signals a change. Phase 6 routes the lookup through a per-state
    /// <c>LocationDatabase</c>; for now the singleton subscription remains.
    /// </summary>
    [JsonTypeTags("last_cleared_location")]
    public partial class LastClearedLocation : LayoutItem
    {
        [KVOverridable]
        public partial bool CompactDisplay { get; set; }

        public LastClearedLocation()
        {
            LocationDatabase.Instance.PropertyChanged += Instance_PropertyChanged;
        }

        public override void Dispose()
        {
            LocationDatabase.Instance.PropertyChanged -= Instance_PropertyChanged;
            base.Dispose();
        }

        private void Instance_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            NotifyPropertyChanged("Location");
        }

        public Location Location
        {
            get { return LocationDatabase.Instance.LastClearedLocation; }
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
