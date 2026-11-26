using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media;

namespace EmoTracker.UI.Controls
{
    /// <summary>
    /// Binds an <see cref="Image"/> to an <see cref="ImageReference"/> and
    /// swaps the displayed bitmap only once that reference has a decoded
    /// image. Until then the control keeps the bitmap it is already showing,
    /// so a stage change does not flash the transparent placeholder.
    /// A different item (a new DataContext) does not keep the previous
    /// bitmap: a recycled cell must not show the last item's icon.
    /// While a bitmap is held, the incoming reference is asked for at
    /// <see cref="ImagePriority.Immediate"/> so the swap is not left
    /// behind prefetch. Pack load does not ask: the boost flag is still off.
    /// A failed decode drops the held bitmap and shows the placeholder.
    /// </summary>
    public class HeldImage
    {
        HeldImage()
        {
        }

        public enum Action
        {
            Clear,
            ShowIncoming,
            HoldPrevious,
            ShowStandIn,
        }

        /// <summary>
        /// Chooses what the control should display. <paramref name="incomingIsReady"/>
        /// is ignored when there is no incoming reference.
        /// </summary>
        public static Action Decide(bool sameOwner, bool previousIsRealImage, bool hasIncoming, bool incomingIsReady)
        {
            if (!hasIncoming)
                return Action.Clear;
            if (incomingIsReady)
                return Action.ShowIncoming;
            if (sameOwner && previousIsRealImage)
                return Action.HoldPrevious;
            return Action.ShowStandIn;
        }

        public static readonly AttachedProperty<ImageReference> ReferenceProperty =
            AvaloniaProperty.RegisterAttached<HeldImage, Image, ImageReference>("Reference");

        public static ImageReference GetReference(AvaloniaObject element) =>
            element.GetValue(ReferenceProperty);

        public static void SetReference(AvaloniaObject element, ImageReference value) =>
            element.SetValue(ReferenceProperty, value);

        static readonly ConditionalWeakTable<Image, State> sStates = new ConditionalWeakTable<Image, State>();

        static HeldImage()
        {
            ReferenceProperty.Changed.AddClassHandler<Image>(OnReferenceChanged);
        }

        static void OnReferenceChanged(Image image, AvaloniaPropertyChangedEventArgs e)
        {
            var state = sStates.GetOrCreateValue(image);
            if (!state.VisualHooked)
            {
                state.VisualHooked = true;
                image.AttachedToVisualTree += OnAttached;
                image.DetachedFromVisualTree += OnDetached;
                image.DataContextChanged += OnDataContextChanged;
            }
            Present(image, state, ownerMayHaveChanged: true);
        }

        static void OnAttached(object sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is Image image && sStates.TryGetValue(image, out var state))
                Present(image, state, ownerMayHaveChanged: false);
        }

        static void OnDataContextChanged(object sender, System.EventArgs e)
        {
            // The icon binding can land before DataContext does. Remember
            // the item once it arrives so the next stage change counts as
            // the same item. A later DataContext change (a recycled cell)
            // is left alone; the following Reference change sees the old
            // owner and does not keep that cell's bitmap.
            if (sender is Image image
                && sStates.TryGetValue(image, out var state)
                && state.Owner == null
                && image.DataContext != null
                && GetReference(image) != null)
            {
                state.Owner = image.DataContext;
            }
        }

        static void OnDetached(object sender, VisualTreeAttachmentEventArgs e)
        {
            // Drop the reference subscription while the control is out of
            // the tree. Attach presents again, so a decode that finished
            // in between is still picked up, and a removed control does
            // not keep the image reference alive.
            if (sender is Image image && sStates.TryGetValue(image, out var state))
                Unlisten(state);
        }

        static void Present(Image image, State state, bool ownerMayHaveChanged)
        {
            Unlisten(state);

            var incoming = GetReference(image);
            if (!ownerMayHaveChanged)
            {
                // Reattach. Do not clear or replace a held bitmap; only
                // publish an image that is already decoded.
                if (incoming != null && TryGetReady(incoming, out var readyOnAttach))
                    SetSource(image, readyOnAttach);
                else if (incoming != null)
                    Listen(image, state, incoming);
                return;
            }

            var owner = image.DataContext;
            bool sameOwner = ReferenceEquals(owner, state.Owner);
            state.Owner = owner;

            bool previousIsReal = image.Source is IImage current && !ImageReferenceService.IsPlaceholder(current);
            IImage readyImage = null;
            bool ready = incoming != null && TryGetReady(incoming, out readyImage);
            switch (Decide(sameOwner, previousIsReal, incoming != null, ready))
            {
                case Action.Clear:
                    SetSource(image, null);
                    break;
                case Action.ShowIncoming:
                    SetSource(image, readyImage);
                    break;
                case Action.HoldPrevious:
                    // This control is waiting on the new bitmap. Ask for it
                    // ahead of prefetch. The service ignores the ask until
                    // the visible layout is up, so pack load cannot jump the queue.
                    var images = ImageReferenceService.Instance;
                    if (images.BoostDisplayedIcons)
                        images.QueueResolution(incoming, ImagePriority.Immediate);
                    Listen(image, state, incoming);
                    break;
                default:
                    SetSource(image, incoming.ResolvedImage as IImage);
                    Listen(image, state, incoming);
                    break;
            }
        }

        static void Listen(Image image, State state, ImageReference reference)
        {
            PropertyChangedEventHandler handler = null;
            handler = (sender, args) =>
            {
                if (args.PropertyName != null
                    && args.PropertyName != nameof(ImageReference.ResolvedImage)
                    && args.PropertyName != nameof(ImageReference.ResolutionFailed))
                    return;
                Settle(image, state, reference);
            };
            state.Reference = reference;
            state.Handler = handler;
            reference.PropertyChanged += handler;

            // The decode may have finished, or failed, between the
            // ready-check above and this subscription.
            Settle(image, state, reference);
        }

        /// <summary>
        /// Applies a finished decode. A failure clears a bitmap this control
        /// was holding and shows the transparent placeholder instead.
        /// </summary>
        static void Settle(Image image, State state, ImageReference reference)
        {
            if (!ReferenceEquals(GetReference(image), reference))
            {
                Unlisten(state);
                return;
            }
            if (TryGetReady(reference, out var ready))
            {
                SetSource(image, ready);
                Unlisten(state);
                return;
            }
            if (!reference.ResolutionFailed)
                return;

            var standIn = reference.ResolvedImage as IImage;
            if (standIn == null || !ImageReferenceService.IsPlaceholder(standIn))
                standIn = ImageReferenceService.GetPlaceholder(reference);
            SetSource(image, standIn);
            Unlisten(state);
        }

        static void Unlisten(State state)
        {
            if (state.Reference != null && state.Handler != null)
                state.Reference.PropertyChanged -= state.Handler;
            state.Reference = null;
            state.Handler = null;
        }

        static bool TryGetReady(ImageReference reference, out IImage image)
        {
            image = ImageReferenceService.Instance.GetCachedImage(reference);
            if (image != null && !ImageReferenceService.IsPlaceholder(image))
                return true;

            image = reference.ResolvedImage as IImage;
            if (image != null && !ImageReferenceService.IsPlaceholder(image))
                return true;

            image = null;
            return false;
        }

        static void SetSource(Image image, IImage source)
        {
            if (!ReferenceEquals(image.Source, source))
                image.Source = source;
        }

        sealed class State
        {
            public object Owner;
            public ImageReference Reference;
            public PropertyChangedEventHandler Handler;
            public bool VisualHooked;
        }
    }
}
