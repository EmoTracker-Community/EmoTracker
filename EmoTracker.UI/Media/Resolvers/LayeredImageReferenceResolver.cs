using EmoTracker.Data.Media;

using Avalonia.Media;

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

            IImage img = null;
            foreach (ImageReference layerRef in concreteRef.Layers)
            {
                var layerImg = ImageReferenceService.Instance.ResolveImageReference(layerRef);
                img = Utility.IconUtility.ApplyOverlayImage(img, layerImg);
            }


            return img;
        }
    }
}
