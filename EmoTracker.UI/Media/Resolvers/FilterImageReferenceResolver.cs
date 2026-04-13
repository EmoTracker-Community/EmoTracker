using EmoTracker.Data;
using EmoTracker.Data.Media;

using Avalonia.Media;
using SkiaSharp;

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
            if (baseImg == null)
                return null;

            // Convert the resolved base IImage to SKBitmap, apply the filter
            // chain entirely in SKBitmap space, then convert back once.
            SKBitmap baseSK = Utility.IconUtility.ToSkBitmapForFilter(baseImg);
            if (baseSK == null)
                return Utility.IconUtility.ApplyFilterSpecToImage(Tracker.Instance.ActiveGamePackage, baseImg, concreteRef.Filter);

            baseSK = Utility.IconUtility.ApplyFilterSpecToSKBitmap(
                Tracker.Instance.ActiveGamePackage, baseSK, concreteRef.Filter);

            return Utility.IconUtility.FinalizeToAvalonia(baseSK);
        }
    }
}
