using EmoTracker.Data;
using EmoTracker.Data.Media;

#if WINDOWS
using System.Windows.Media;
#else
using Avalonia.Media;
#endif

namespace EmoTracker.UI.Media.Resolvers
{
    class FilterImageReferenceResolver : ImageReferenceResolver
    {
        public override bool CanResolveReference(ImageReference imageRef)
        {
            return imageRef as FilterImageReference != null;
        }

#if WINDOWS
        public override ImageSource ResolveReference(ImageReference imageRef)
#else
        public override IImage ResolveReference(ImageReference imageRef)
#endif
        {
            FilterImageReference concreteRef = imageRef as FilterImageReference;
            if (concreteRef == null)
                return null;

            var baseImg = ImageReferenceService.Instance.ResolveImageReference(concreteRef.Reference);
            return Utility.IconUtility.ApplyFilterSpecToImage(Tracker.Instance.ActiveGamePackage, baseImg, concreteRef.Filter);
        }
    }
}
