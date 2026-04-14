using EmoTracker.Core;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Locations
{
    public class LocationVisualProperties : TransactableObject
    {
        // Phase 4 of the TrackerSession refactor: Locations and Sections (the only
        // subclasses of this base) source their mutable property dictionary from
        // the session-owned LocationStateStore rather than holding it on the
        // instance. That puts every Location/Section's runtime state (Pinned,
        // CapturedItem, AvailableChestCount, HostedItem, ...) behind a single
        // store that future Fork() can deep-clone, without recreating Location
        // or Section objects (preserving XAML bindings on the originals).
        //
        // During very early construction (before the first session is built),
        // and as a defensive fallback if no session is current, we fall back to
        // the per-instance dictionary on TransactableObject. In practice the
        // session is always available by the time any pack is loaded.
        protected override System.Collections.Generic.Dictionary<string, object> PropertyStore
        {
            get
            {
                var session = Session.TrackerSession.Current;
                var states = session?.LocationStates;
                if (states != null)
                    return states.StateFor(this);

                return base.PropertyStore;
            }
        }

        LocationVisualProperties mVisualParent;
        ImageReference mOpenChestImage;
        ImageReference mClosedChestImage;
        ImageReference mUnavailableOpenChestImage;
        ImageReference mUnavailableClosedChestImage;

        bool mbAlwaysAllowChestManipulation = false;
        bool mbOverrideAlwaysAllowChestManipulation = false;

        bool mbOverrideAutoUnpinOnClear = false;
        bool mbAutoUnpinOnClear = false;


        public LocationVisualProperties VisualParent
        {
            get { return mVisualParent; }
            protected set { SetProperty(ref mVisualParent, value); }
        }

        public bool AlwaysAllowChestManipulation
        {
            get
            {
                if (mbOverrideAlwaysAllowChestManipulation)
                    return mbAlwaysAllowChestManipulation;

                if (mVisualParent != null)
                    return mVisualParent.AlwaysAllowChestManipulation;

                return false;
            }
            set
            {
                mbOverrideAlwaysAllowChestManipulation = true;
                SetProperty(ref mbAlwaysAllowChestManipulation, value);
            }
        }

        public bool AutoUnpinOnClear
        {
            get
            {
                if (mbOverrideAutoUnpinOnClear)
                    return mbAutoUnpinOnClear;

                if (mVisualParent != null)
                    return mVisualParent.AutoUnpinOnClear;

                return Session.TrackerSession.Current.Global.AutoUnpinLocationsOnClear;
            }
            set
            {
                mbOverrideAutoUnpinOnClear = true;
                SetProperty(ref mbAutoUnpinOnClear, value);
            }
        }

        public ImageReference OpenChestImage
        {
            get
            {
                if (mOpenChestImage != null)
                    return mOpenChestImage;

                if (mVisualParent != null)
                    return mVisualParent.OpenChestImage;

                return null;
            }
            set
            {
                if (SetProperty(ref mOpenChestImage, value))
                {
                    if (mOpenChestImage != null)
                    {
                        UnavailableOpenChestImage = ImageReference.FromImageReference(mOpenChestImage, "grayscale,dim");
                    }
                    else
                    {
                        UnavailableOpenChestImage = null;
                    }
                }
            }
        }

        public ImageReference ClosedChestImage
        {
            get
            {
                if (mClosedChestImage != null)
                    return mClosedChestImage;

                if (mVisualParent != null)
                    return mVisualParent.ClosedChestImage;

                return null;
            }
            set
            {
                if (SetProperty(ref mClosedChestImage, value))
                {
                    if (mClosedChestImage != null)
                    {
                        UnavailableClosedChestImage = ImageReference.FromImageReference(mClosedChestImage, "grayscale,dim");
                    }
                    else
                    {
                        UnavailableClosedChestImage = null;
                    }
                }
            }
        }

        public ImageReference UnavailableOpenChestImage
        {
            get
            {
                if (mUnavailableOpenChestImage != null)
                    return mUnavailableOpenChestImage;

                if (mVisualParent != null)
                    return mVisualParent.UnavailableOpenChestImage;

                return null;
            }
            private set { SetProperty(ref mUnavailableOpenChestImage, value); }
        }

        public ImageReference UnavailableClosedChestImage
        {
            get
            {
                if (mUnavailableClosedChestImage != null)
                    return mUnavailableClosedChestImage;

                if (mVisualParent != null)
                    return mVisualParent.UnavailableClosedChestImage;

                return null;
            }
            private set { SetProperty(ref mUnavailableClosedChestImage, value); }
        }
    }
}
