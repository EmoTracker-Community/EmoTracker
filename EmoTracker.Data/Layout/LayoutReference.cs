using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4: a layout-element that resolves a referenced
    /// <see cref="Data.Layout.Layout"/> by name through the
    /// <see cref="LayoutManager"/>. The reference is held as a
    /// <see cref="ModelReference{Layout}"/> so a fork's <c>Layout</c> resolves
    /// through the holder's <see cref="ModelTypeBase.GetModelResolver"/>.
    /// </summary>
    [JsonTypeTags("layout")]
    public partial class LayoutReference : LayoutItem
    {
        // Layout key is part of the definition — saved in ImmutableData via
        // [KVImmutable]. The resolved Layout instance is per-state via
        // ModelReference (Phase 6 will route it through state-scoped resolvers;
        // for now it goes through the singleton).
        [KVImmutable]
        public partial string Key { get; }

        ModelReference<Layout> mLayoutRef;

        public LayoutReference()
        {
            //  TODO: Track changes to our referenced layout key and re-acquire referenced layout as needed
            mLayoutRef = new ModelReference<Layout>(this);
        }

        public Layout Layout
        {
            get { return mLayoutRef.Target; }
            set
            {
                var current = mLayoutRef.Target;
                if (ReferenceEquals(current, value)) return;
                NotifyPropertyChanging();
                mLayoutRef.Set(value);
                NotifyPropertyChanged();
            }
        }

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, System.Collections.Generic.Dictionary<string, object> definition)
        {
            definition[nameof(Key)] = data.GetValue<string>("key");
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            // Phase 6 step 11: prefer the owning state's LayoutManager; at
            // parse time OwnerState may not yet be set, so fall back via
            // SessionContext / singleton. The resolved instance's DefinitionId
            // is captured in mLayoutRef so cross-state resolution flows
            // through the per-state resolver afterward.
            var layouts = (this.OwnerState as Sessions.TrackerState)?.Layouts
                ?? Sessions.SessionContext.ActiveState?.Layouts;
            var resolved = layouts?.FindLayout(Key);
            mLayoutRef.Set(resolved);
            return true;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (LayoutReference)source;
            mLayoutRef = src.mLayoutRef.ForFork(this);
        }

        public override void OnOwnerStateStamped()
        {
            base.OnOwnerStateStamped();
            mLayoutRef?.InvalidateCache();
            NotifyPropertyChanged(nameof(Layout));
        }
    }
}
