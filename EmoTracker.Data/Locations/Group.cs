using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using System.Collections.Generic;

namespace EmoTracker.Data.Locations
{
    public partial class Group : ModelTypeBase
    {
        // List of cross-references to member locations. Each entry is a
        // ModelReference<Location> (Phase 2.5 framework) so resolution flows
        // through this group's GetModelResolver(); on Fork, OnForked rebuilds the
        // list with ForFork(this) per entry. The list itself is a private field —
        // not in the KV stores — so the per-instance cache slots in each
        // ModelReference survive across reads.
        List<ModelReference<Location>> mLocationRefs = new List<ModelReference<Location>>();

        // Public API parity: Locations is enumerable Location, projecting through
        // the resolver. Pre-Phase-3 callers iterating `group.Locations` continue
        // to see Location instances.
        public IEnumerable<Location> Locations
        {
            get
            {
                foreach (var r in mLocationRefs)
                {
                    var t = r.Target;
                    if (t != null) yield return t;
                }
            }
        }

        [KVMutable]
        public partial string Name { get; set; }

        [KVMutable]
        public partial bool HasAvailableItems { get; set; }

        public bool HasLocations
        {
            get { return mLocationRefs.Count > 0; }
        }

        [KVMutable]
        public partial string Color { get; set; }

        public void AddLocation(Location location)
        {
            if (location == null) return;
            mLocationRefs.Add(new ModelReference<Location>(this, location));
            NotifyPropertyChanged("HasLocations");
            NotifyPropertyChanged(nameof(Locations));
        }

        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new System.ArgumentNullException(nameof(destOwnerState));
            var copy = new Group();
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            return copy;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (Group)source;
            // Rebind each member-location reference to the fork's holder so its
            // Target getter resolves through this fork's resolver. Same DefinitionIds
            // (the cross-state identity), fresh per-entry caches.
            mLocationRefs = new List<ModelReference<Location>>(src.mLocationRefs.Count);
            foreach (var r in src.mLocationRefs)
                mLocationRefs.Add(r.ForFork(this));
        }
    }
}
