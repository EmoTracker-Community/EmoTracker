using EmoTracker.Core;
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Locations
{
    /// <summary>
    /// Phase 3: <see cref="LocationVisualProperties"/> is now a
    /// <see cref="TransactableModelTypeBase"/>. The four image / behavior
    /// properties whose value optionally falls through to <see cref="VisualParent"/>
    /// (and ultimately to app-wide defaults) are hand-written. The rest of the
    /// machinery (KV stores, INPC, fork) is inherited from the v2 base classes.
    ///
    /// <para>
    /// <see cref="VisualParent"/> is held as a <see cref="ModelReference{T}"/>
    /// rather than a direct C# reference: when the owning <see cref="Location"/>
    /// or <see cref="Section"/> forks, the parent's coordinated fork rewires the
    /// new child's <see cref="VisualParent"/> via
    /// <see cref="ModelReference{T}.Set(T)"/>, and `OnForked` calls
    /// <see cref="ModelReference{T}.ForFork"/> to give each fork its own cache slot.
    /// </para>
    /// </summary>
    public partial class LocationVisualProperties : TransactableModelTypeBase
    {
        // Cross-reference to the visual parent (typically the owning Location or
        // the parent Section's owning Location). Stored as a private field, not in
        // MutableData — same rationale as Phase 2.5 cross-item-references.
        ModelReference<LocationVisualProperties> mVisualParentRef;

        // "Override" flags track whether a per-item value has been explicitly
        // set (in which case it wins over VisualParent's value). They live in
        // MutableData so each fork has its own override state.
        const string KeyAlwaysAllowOverride = "__AAACM_Override";
        const string KeyAutoUnpinOverride = "__AUOC_Override";

        public LocationVisualProperties()
        {
            mVisualParentRef = new ModelReference<LocationVisualProperties>(this);
        }

        public LocationVisualProperties VisualParent
        {
            get { return mVisualParentRef.Target; }
            protected set
            {
                var current = mVisualParentRef.Target;
                if (ReferenceEquals(current, value)) return;
                NotifyPropertyChanging();
                mVisualParentRef.Set(value);
                NotifyPropertyChanged();
            }
        }

        // -------- AlwaysAllowChestManipulation: locally-overridable bool ------

        public bool AlwaysAllowChestManipulation
        {
            get
            {
                if (MutableData.GetValue<bool>(KeyAlwaysAllowOverride, false))
                    return MutableData.GetValue<bool>(nameof(AlwaysAllowChestManipulation), false);

                var parent = VisualParent;
                if (parent != null)
                    return parent.AlwaysAllowChestManipulation;

                return false;
            }
            set
            {
                MutableData.SetValue(KeyAlwaysAllowOverride, true);
                var current = MutableData.GetValue<bool>(nameof(AlwaysAllowChestManipulation), false);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(AlwaysAllowChestManipulation), value);
                    NotifyPropertyChanged();
                }
            }
        }

        // -------- AutoUnpinOnClear: locally-overridable bool with app fallback

        public bool AutoUnpinOnClear
        {
            get
            {
                if (MutableData.GetValue<bool>(KeyAutoUnpinOverride, false))
                    return MutableData.GetValue<bool>(nameof(AutoUnpinOnClear), false);

                var parent = VisualParent;
                if (parent != null)
                    return parent.AutoUnpinOnClear;

                // Phase 7.3: prefer the owning state's per-state setting.
                return (this.OwnerState as Sessions.TrackerState)?.Settings?.AutoUnpinLocationsOnClear
                       ?? ApplicationSettings.Instance.AutoUnpinLocationsOnClear;
            }
            set
            {
                MutableData.SetValue(KeyAutoUnpinOverride, true);
                var current = MutableData.GetValue<bool>(nameof(AutoUnpinOnClear), false);
                if (current != value)
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(AutoUnpinOnClear), value);
                    NotifyPropertyChanged();
                }
            }
        }

        // -------- OpenChestImage: when set, also derives Unavailable variant ---

        public ImageReference OpenChestImage
        {
            get
            {
                var local = MutableData.GetValue<ImageReference>(nameof(OpenChestImage), null);
                if (local != null) return local;

                var parent = VisualParent;
                if (parent != null) return parent.OpenChestImage;

                return null;
            }
            set
            {
                var current = MutableData.GetValue<ImageReference>(nameof(OpenChestImage), null);
                if (!ReferenceEquals(current, value))
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(OpenChestImage), value);
                    if (value != null)
                        UnavailableOpenChestImage = ImageReference.FromImageReference(value, "grayscale,dim");
                    else
                        UnavailableOpenChestImage = null;
                    NotifyPropertyChanged();
                }
            }
        }

        public ImageReference ClosedChestImage
        {
            get
            {
                var local = MutableData.GetValue<ImageReference>(nameof(ClosedChestImage), null);
                if (local != null) return local;

                var parent = VisualParent;
                if (parent != null) return parent.ClosedChestImage;

                return null;
            }
            set
            {
                var current = MutableData.GetValue<ImageReference>(nameof(ClosedChestImage), null);
                if (!ReferenceEquals(current, value))
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(ClosedChestImage), value);
                    if (value != null)
                        UnavailableClosedChestImage = ImageReference.FromImageReference(value, "grayscale,dim");
                    else
                        UnavailableClosedChestImage = null;
                    NotifyPropertyChanged();
                }
            }
        }

        public ImageReference UnavailableOpenChestImage
        {
            get
            {
                var local = MutableData.GetValue<ImageReference>(nameof(UnavailableOpenChestImage), null);
                if (local != null) return local;

                var parent = VisualParent;
                if (parent != null) return parent.UnavailableOpenChestImage;

                return null;
            }
            private set
            {
                var current = MutableData.GetValue<ImageReference>(nameof(UnavailableOpenChestImage), null);
                if (!ReferenceEquals(current, value))
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(UnavailableOpenChestImage), value);
                    NotifyPropertyChanged();
                }
            }
        }

        public ImageReference UnavailableClosedChestImage
        {
            get
            {
                var local = MutableData.GetValue<ImageReference>(nameof(UnavailableClosedChestImage), null);
                if (local != null) return local;

                var parent = VisualParent;
                if (parent != null) return parent.UnavailableClosedChestImage;

                return null;
            }
            private set
            {
                var current = MutableData.GetValue<ImageReference>(nameof(UnavailableClosedChestImage), null);
                if (!ReferenceEquals(current, value))
                {
                    NotifyPropertyChanging();
                    MutableData.SetValue(nameof(UnavailableClosedChestImage), value);
                    NotifyPropertyChanged();
                }
            }
        }

        // -------- Fork --------------------------------------------------------

        // LocationVisualProperties' Fork() is provided by the abstract
        // ModelTypeBase, but concrete subclasses (Location / Section) must
        // override it. We provide a virtual default-Activator implementation
        // here so types like Section and Location can override it cleanly with
        // their coordinated-fork logic.
        public override ModelTypeBase Fork()
        {
            var copy = (LocationVisualProperties)System.Activator.CreateInstance(this.GetType());
            copy.InitializeAsForkOf(this);
            return copy;
        }

        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            var src = (LocationVisualProperties)source;
            // Rebind VisualParent reference to the fork. The owning Location
            // (or Section, via the owning Location's coordinated fork) will
            // call mVisualParentRef.Set(...) with the actual new parent
            // immediately after this OnForked returns, so the cache will be
            // updated correctly.
            mVisualParentRef = src.mVisualParentRef.ForFork(this);
        }
    }
}
