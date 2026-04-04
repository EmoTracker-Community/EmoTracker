using EmoTracker.Data.Media;

#if WINDOWS
using System.Windows.Media;
#else
using Avalonia.Media;
#endif

namespace EmoTracker.UI.Media.Resolvers
{
    class LayeredImageReferenceResolver : ImageReferenceResolver
    {
        public override bool CanResolveReference(ImageReference imageRef)
        {
            return imageRef as LayeredImageReference != null;
        }

#if WINDOWS
        public override ImageSource ResolveReference(ImageReference imageRef)
#else
        public override IImage ResolveReference(ImageReference imageRef)
#endif
        {
            LayeredImageReference concreteRef = imageRef as LayeredImageReference;
            if (concreteRef == null)
                return null;

            if (concreteRef.Layers.Count == 0)
                return null;

#if WINDOWS
            ImageSource img = null;
#else
            IImage img = null;
#endif
            foreach (ImageReference layerRef in concreteRef.Layers)
            {
                var layerImg = ImageReferenceService.Instance.ResolveImageReference(layerRef);
                img = Utility.IconUtility.ApplyOverlayImage(img, layerImg);
            }

#if WINDOWS
            if (img != null)
                img.Freeze();
#endif

            return img;
        }
    }
}
