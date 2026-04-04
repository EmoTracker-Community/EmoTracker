using EmoTracker.Data.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace EmoTracker.UI.Media.Resolvers
{
    class LayeredImageReferenceResolver : ImageReferenceResolver
    {
        public override bool CanResolveReference(ImageReference imageRef)
        {
            return imageRef as LayeredImageReference != null;
        }

        public override ImageSource ResolveReference(ImageReference imageRef)
        {
            LayeredImageReference concreteRef = imageRef as LayeredImageReference;
            if (concreteRef == null)
                return null;

            if (concreteRef.Layers.Count == 0)
                return null;

            ImageSource img = null;
            foreach (ImageReference layerRef in concreteRef.Layers)
            {
                ImageSource layerImg = ImageReferenceService.Instance.ResolveImageReference(layerRef);
                img = Utility.IconUtility.ApplyOverlayImage(img, layerImg);
            }

            if (img != null)
                img.Freeze();

            return img;
        }
    }
}
