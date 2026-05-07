using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Layout
{
    /// <summary>
    /// Phase 4: a button-shaped layout element that opens a popup containing a
    /// referenced <see cref="Data.Layout.Layout"/>. The Layout reference is held
    /// as a <see cref="ModelReference{Layout}"/> for per-state resolution; the
    /// referenced layout key is preserved in <see cref="ImmutableData"/> so a
    /// per-state resolver can re-resolve in Phase 6.
    /// </summary>
    [JsonTypeTags("button_popup")]
    public partial class ButtonPopup : LayoutItem
    {
        public enum ButtonStyle
        {
            Settings,
            Solid,
            Image
        }

        [KVOverridable]
        public partial ButtonStyle Style { get; set; }

        [KVOverridable]
        public partial ImageReference Image { get; set; }

        [KVOverridable]
        public partial string PopupBackground { get; set; }

        [KVOverridable]
        public partial bool MaskInput { get; set; }

        // The referenced layout name lives in ImmutableData (definition); the
        // resolved Layout instance is per-state via ModelReference.
        [KVImmutable]
        public partial string LayoutKey { get; }

        ModelReference<Layout> mLayoutRef;

        public ButtonPopup()
        {
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

        protected override void PopulateDefinitionData(JObject data, IGamePackage package, Dictionary<string, object> definition)
        {
            definition[nameof(Style) + "__def"] = data.GetEnumValue<ButtonStyle>("style", ButtonStyle.Settings);
            definition[nameof(Image) + "__def"] = ImageReference.FromPackRelativePath(
                (this.OwnerState as Sessions.TrackerState)?.PackageInstance, data.GetValue<string>("image"), data.GetValue<string>("image_filter"));
            definition[nameof(PopupBackground) + "__def"] = data.GetValue<string>("popup_background", "#ff212121");
            definition[nameof(MaskInput) + "__def"] = data.GetValue<bool>("mask_input", false);
            definition[nameof(LayoutKey)] = data.GetValue<string>("layout");
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            // Resolve through the owning state's LayoutManager. OwnerState
            // is stamped before parse so cross-references resolve.
            var layouts = (this.OwnerState as Sessions.TrackerState)?.Layouts;
            var resolved = layouts?.FindLayout(LayoutKey);
            mLayoutRef.Set(resolved);
            return true;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (ButtonPopup)source;
            mLayoutRef = src.mLayoutRef.ForFork(this);
            // Fire PropertyChanged on Layout so post-fork bindings re-evaluate.
            NotifyPropertyChanged(nameof(Layout));
        }
    }
}
