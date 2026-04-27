using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4 layout type: a single trackable item embedded in a layout. The
    /// referenced <see cref="ITrackableItem"/> is held via
    /// <see cref="ModelReference{T}"/> so a fork's <c>Data</c> resolves through
    /// its own <c>IModelResolver</c> (Phase 6 swaps in per-state resolvers; for
    /// now the holder reads through the ambient singleton resolver).
    /// </summary>
    [JsonTypeTags("item")]
    public partial class Item : LayoutItem
    {
        // Cross-reference: Phase 2.5 framework. Stored as a private field, not in
        // MutableData (the boundary deep-copy on every read would otherwise
        // return a fresh-cached ModelReference each time). OnForked carries
        // the field across via ForFork(this).
        ModelReference<ITrackableItem> mDataRef;

        public Item()
        {
            mDataRef = new ModelReference<ITrackableItem>(this);
        }

        public ITrackableItem Data
        {
            get { return mDataRef.Target; }
            set
            {
                var current = mDataRef.Target;
                if (ReferenceEquals(current, value)) return;
                NotifyPropertyChanging();
                mDataRef.Set(value);
                NotifyPropertyChanged();
            }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            // Resolve through the owning state's ItemDatabase. The
            // resolved instance's DefinitionId is captured in mDataRef
            // so cross-state resolution flows through the per-state
            // resolver afterward.
            var itemDb = (this.OwnerState as Sessions.TrackerState)?.Items;
            var resolved = itemDb?.FindProvidingItemForCode(data.GetValue<string>("item"));
            mDataRef.Set(resolved);
            return true;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (Item)source;
            // Carry the reference across by identity. ForFork(this) gives us a
            // fresh ModelReference bound to this fork — same DefinitionId, empty
            // cache — so resolution flows through this fork's resolver on first
            // read.
            mDataRef = src.mDataRef.ForFork(this);
        }

        public override void OnOwnerStateStamped()
        {
            base.OnOwnerStateStamped();
            // Phase 7 polish: invalidate the cache so the NEXT read of
            // Data resolves through the (now-stamped) OwnerState's
            // resolver. Without this, a Target read between Fork and
            // stamp (e.g. during early visual-tree binding) would have
            // cached the primary state's item.
            mDataRef?.InvalidateCache();
            // Fire PropertyChanged on Data so any pre-stamp bindings
            // re-evaluate against the now-correct target.
            NotifyPropertyChanged(nameof(Data));
        }
    }
}
