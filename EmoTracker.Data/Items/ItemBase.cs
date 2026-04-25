using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Data.Items
{
    /// <summary>
    /// Phase 2: <see cref="ItemBase"/> is now a <see cref="TransactableModelTypeBase"/>.
    /// All previously field-backed properties are emitted by the
    /// <c>EmoTracker.SourceGenerators</c> KV property generator: <c>[KVMutable]</c>
    /// for runtime-mutable per-state values (which preserve the public setters that
    /// existing code uses), <c>[KVTransactable]</c> for undo-tracked values, and
    /// hand-written for properties whose getters or setters carry non-trivial logic.
    ///
    /// <para>
    /// Definition-time data that is parsed once and never re-assigned at runtime
    /// (icons, code providers, stage tables, ...) is intentionally kept as private
    /// fields on the concrete subclasses rather than in <see cref="ImmutableData"/>;
    /// see Phase 2 §2.6 for the rationale (these are reference types or collections
    /// that don't fit cleanly through the <see cref="IDeepCopyable"/> boundary).
    /// Forks share these by reference via copying the field on the new instance —
    /// concrete subclasses' Fork() overrides handle that explicitly.
    /// </para>
    /// </summary>
    public abstract partial class ItemBase : TransactableModelTypeBase, ITrackableItem
    {
        protected ItemBase()
        {
            // Seed defaults that differ from default(T) so the generator-emitted
            // getters return the historical values without anyone having to write
            // a getter override. Forks see these via copy-on-write inheritance from
            // the source's MutableData (the new fork's local KV is empty and reads
            // walk through to the source for unset keys).
            MutableData.SetValue(nameof(Capturable), true);
            MutableData.SetValue(nameof(BadgeTextColor), "WhiteSmoke");
        }

        // ---------------------------------------------------------- Generated KV

        [KVMutable]
        public partial string Name { get; set; }

        [KVMutable]
        public partial string BadgeText { get; set; }

        [KVMutable]
        public partial string BadgeTextColor { get; set; }

        [KVMutable]
        public partial bool Capturable { get; set; }

        [KVMutable]
        public partial bool MaskInput { get; set; }

        [KVMutable]
        public partial bool IgnoreUserInput { get; set; }

        [KVMutable]
        public partial string[] PhoneticSubstitutes { get; set; }

        // Icon: cascades to PotentialIcon (via DependentProperty reflection in the
        // base ObservableObject) and triggers a global accessibility refresh on
        // change. The KVMutable generator's setter only invokes the OnChanged
        // callback when the value actually changed, matching the pre-Phase-2
        // behavior of the hand-written setter.
        [KVMutable]
        [DependentProperty("PotentialIcon")]
        [OnChanged(nameof(InvalidateAccessibility))]
        public partial ImageReference Icon { get; set; }

        // ---------------------------------------------------------- Hand-written

        /// <summary>
        /// DisabledImageFilterSpec falls back to the per-tracker default when the
        /// per-item value is null, so the getter has to be hand-written; the setter
        /// is straightforward.
        /// </summary>
        public string DisabledImageFilterSpec
        {
            get
            {
                var local = MutableData.GetValue<string>(nameof(DisabledImageFilterSpec), null);
                if (local != null) return local;
                return Tracker.Instance.DisabledImageFilterSpec;
            }
            set
            {
                var current = MutableData.GetValue<string>(nameof(DisabledImageFilterSpec), null);
                if (!EqualityComparer<string>.Default.Equals(current, value))
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(DisabledImageFilterSpec), value);
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// PotentialIcon falls back to <see cref="Icon"/> when no per-item potential
        /// has been set, and the setter intentionally skips both the equality check
        /// and the <c>PropertyChanging</c> notification — preserving the pre-Phase-2
        /// behavior verbatim.
        /// </summary>
        public ImageReference PotentialIcon
        {
            get
            {
                var local = MutableData.GetValue<ImageReference>(nameof(PotentialIcon), null);
                if (local != null) return local;
                return MutableData.GetValue<ImageReference>(nameof(Icon), null);
            }
            set
            {
                MutableData.SetValue(nameof(PotentialIcon), value);
                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Backing for <see cref="Icon"/>'s <see cref="OnChangedAttribute"/>:
        /// pushes a global accessibility refresh through <see cref="LocationDatabase"/>
        /// whenever the displayed icon changes. Public so the same trigger can be
        /// invoked manually (e.g. from external code that updates derived state).
        /// </summary>
        public void InvalidateAccessibility()
        {
            LocationDatabase.Instance.RefeshAccessibility();
        }

        // ---------------------------------------------------------- Fork

        /// <summary>
        /// Default Fork: allocates a fresh instance of the concrete runtime type via
        /// <see cref="System.Activator"/>, layers a COW <see cref="MutableData"/>
        /// over the source's mutable state (sharing <see cref="ImmutableData"/> by
        /// reference), and then invokes <see cref="ModelTypeBase.OnForked"/> so
        /// concrete leaves can copy their private definition fields and re-resolve
        /// any cross-item references. Concrete leaves are free to override this
        /// with a more efficient direct allocation; the activator path is fast
        /// enough for fork-on-state-switch (not a hot path).
        /// </summary>
        public override ModelTypeBase Fork()
        {
            var copy = (ItemBase)System.Activator.CreateInstance(this.GetType());
            copy.InitializeAsForkOf(this);
            return copy;
        }

        public abstract void OnLeftClick();
        public abstract void OnRightClick();

        public abstract uint ProvidesCode(string code);
        public abstract bool CanProvideCode(string code);
        public abstract void AdvanceToCode(string code = null);

        /// <summary>
        /// Returns the set of all codes this item can potentially provide, for
        /// indexing purposes. Returns null if the item's codes are dynamic and
        /// cannot be statically enumerated (e.g. LuaItem with a Lua callback).
        /// </summary>
        public virtual IEnumerable<string> GetAllProvidedCodes() => null;


        #region -- Static Methods ---

        public static ITrackableItem CreateItem(JObject data, IGamePackage package)
        {
            ItemBase instance = JsonTypeTagsAttribute.CreateIntanceForTypeTag<ItemBase>(data.GetValue<string>("type"));

            if (instance != null)
            {
                instance.Name = data.GetValue<string>("name");
                instance.Capturable = data.GetValue<bool>("capturable", true);
                instance.MaskInput = data.GetValue<bool>("mask_input", false);
                instance.IgnoreUserInput = data.GetValue<bool>("ignore_user_input", false);
                instance.DisabledImageFilterSpec = data.GetValue<string>("disabled_image_filter", null);

                var phonetics = data["phonetic_substitutes"] as Newtonsoft.Json.Linq.JArray;
                if (phonetics != null)
                    instance.PhoneticSubstitutes = phonetics.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

                instance.ParseDataInternal(data, package);
            }

            return instance;
        }

        #endregion

        #region --- Serialization ---

        protected abstract void ParseDataInternal(JObject data, IGamePackage package);

        protected virtual bool Save(JObject data)
        {
            return false;
        }

        protected virtual bool Load(JObject data)
        {
            return true;
        }

        bool ITrackableItem.Save(JObject data)
        {
            return Save(data);
        }

        bool ITrackableItem.Load(JObject data)
        {
            return Load(data);
        }

        #endregion
    }
}
