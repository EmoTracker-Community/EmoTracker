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

            // Phase 7.1.h: source the pack from the reference's owning
            // PackageInstance (set at construction) — fall through to
            // the wrapped reference's PI if this layer's wasn't set,
            // since filters inherit identity from their underlying
            // ConcreteImageReference.
            var pi = concreteRef.PackageInstance ?? concreteRef.Reference?.PackageInstance;
            var pkg = pi?.GamePackage;
            var variant = pi?.ActiveVariant;

            // Convert the resolved base IImage to SKBitmap, apply the filter
            // chain entirely in SKBitmap space, then convert back once.
            SKBitmap baseSK = Utility.IconUtility.ToSkBitmapForFilter(baseImg);
            if (baseSK == null)
                return Utility.IconUtility.ApplyFilterSpecToImage(pkg, variant, baseImg, concreteRef.Filter);

            baseSK = Utility.IconUtility.ApplyFilterSpecToSKBitmap(pkg, variant, baseSK, concreteRef.Filter);

            return Utility.IconUtility.FinalizeToAvalonia(baseSK);
        }
    }
}
