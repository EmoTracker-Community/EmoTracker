using EmoTracker.Data.Media;

using Avalonia.Media;
using SkiaSharp;

namespace EmoTracker.UI.Media.Resolvers
{
    class LayeredImageReferenceResolver : ImageReferenceResolver
    {
        public override bool CanResolveReference(ImageReference imageRef)
        {
            return imageRef as LayeredImageReference != null;
        }

        public override IImage ResolveReference(ImageReference imageRef)
        {
            LayeredImageReference concreteRef = imageRef as LayeredImageReference;
            if (concreteRef == null)
                return null;

            if (concreteRef.Layers.Count == 0)
                return null;

            // Composite all layers in SKBitmap space — one PNG conversion at the end
            // instead of N round-trips through PNG encode→decode per layer.
            SKBitmap composite = null;
            foreach (ImageReference layerRef in concreteRef.Layers)
            {
                var layerImg = ImageReferenceService.Instance.ResolveImageReference(layerRef);
                if (layerImg == null)
                    continue;

                SKBitmap layerSK = Utility.IconUtility.ToSkBitmapForFilter(layerImg);
                if (layerSK == null)
                    continue;

                if (composite == null)
                {
                    composite = layerSK;
                }
                else
                {
                    var prev = composite;
                    composite = Utility.IconUtility.ApplyOverlaySK(composite, layerSK);
                    // ApplyOverlaySK disposes overlay (layerSK) and may return a new
                    // bitmap; dispose the old composite if it changed.
                    if (composite != prev)
                        prev.Dispose();
                }
            }

            if (composite == null)
                return null;

            return Utility.IconUtility.FinalizeToAvalonia(composite);
        }
    }
}
