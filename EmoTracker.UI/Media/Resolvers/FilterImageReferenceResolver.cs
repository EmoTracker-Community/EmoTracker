using EmoTracker.Data;
using EmoTracker.Data.Media;

using Avalonia.Media;

namespace EmoTracker.UI.Media.Resolvers
{
    class FilterImageReferenceResolver : ImageReferenceResolver
    {
        public override bool CanResolveReference(ImageReference imageRef)
        {
            return imageRef as FilterImageReference != null;
        }

        public override IImage ResolveReference(ImageReference imageRef)
        {
            FilterImageReference concreteRef = imageRef as FilterImageReference;
            if (concreteRef == null)
                return null;

            var baseImg = ImageReferenceService.Instance.ResolveImageReference(concreteRef.Reference);
            return Utility.IconUtility.ApplyFilterSpecToImage(Tracker.Instance.ActiveGamePackage, baseImg, concreteRef.Filter);
        }
    }
}
