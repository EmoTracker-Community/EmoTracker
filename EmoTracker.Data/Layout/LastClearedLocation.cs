using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

// Phase 6 step 11: this layout element subscribes to the singleton
// LocationDatabase's PropertyChanged at construction time (before any
// state has stamped OwnerState). The full per-state migration of this
// reactive subscription is part of the multi-window UI follow-up; for
// now the singleton subscription remains, with the same caveat as
// Phase 4 §4.6 documented.
#pragma warning disable CS0618

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
