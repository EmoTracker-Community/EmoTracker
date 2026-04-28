using EmoTracker.Data;
using EmoTracker.Data.Media;
using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Media;
using SkiaSharp;

namespace EmoTracker.UI.Media.Resolvers
{
    public class ConcreteImageReferenceResolver : ImageReferenceResolver
    {
        public override bool CanResolveReference(ImageReference imageRef)
        {
            return imageRef as ConcreteImageReference != null;
        }

        public override IImage ResolveReference(ImageReference imageRef)
        {
            ConcreteImageReference concreteRef = imageRef as ConcreteImageReference;
            if (concreteRef == null)
                return null;

            if (concreteRef.URI == null)
                return null;

            if (concreteRef.URI.Scheme.Equals("gamepackage", StringComparison.OrdinalIgnoreCase))
            {
                // Phase 7.1.h: pack data is owned by the ImageReference's
                // PackageInstance — use it directly instead of asking the
                // active session, so loads against a PI's DefinitionalState
                // (before any of its primary states exist) resolve correctly.
                var pi = concreteRef.PackageInstance;
                var pkg = pi?.GamePackage;
                if (pkg == null)
                    return null;

                string filePath = string.Format("{0}{1}",
                    Uri.UnescapeDataString(concreteRef.URI.Host),
                    Uri.UnescapeDataString(concreteRef.URI.AbsolutePath));

                // Acquire-and-clone the cached base bitmap under the same lock that
                // SourceImageCache disposal holds when it disposes entries. Without
                // this scope, a pack-load event firing PackageInstance.Dispose can
                // free the SKBitmap's native pixel-ref while we're still pointing
                // at it — leading to a 0xC0000005 inside Skia's shader fallback path
                // (sk_bitmap_make_shader). The decode + Copy are both fast (~ms
                // scale) and only block other ConcreteImageReference work, so
                // widening the lock here is acceptable.
                SKBitmap working;
                lock (pi.SourceImageCacheLock)
                {
                    if (!pi.SourceImageCache.TryGetValue(filePath, out object boxedBase)
                        || boxedBase is not SKBitmap baseSK)
                    {
                        using (Stream s = pkg.Open(filePath, pi?.ActiveVariant))
                        {
                            if (s == null)
                                return null;

                            baseSK = Utility.IconUtility.DecodeSKBitmap(s);
                        }

                        if (baseSK == null)
                            return null;

                        pi.SourceImageCache[filePath] = baseSK;
                    }

                    // Clone the base bitmap so filter operations don't mutate the
                    // cached original. The Copy() must happen inside the lock so
                    // the source can't be disposed between cache hit and clone.
                    working = baseSK.Copy();
                }

                if (working == null)
                    return null;

                // Filtering happens outside the lock — it operates on `working`
                // (our exclusive copy) and doesn't touch the cache.
                working = Utility.IconUtility.ApplyFilterSpecToSKBitmap(
                    pkg, pi?.ActiveVariant, working, concreteRef.Filter);

                // Convert to Avalonia IImage once at the end, computing the
                // alpha mask for InputMaskingImage hit-testing.
                return Utility.IconUtility.FinalizeToAvalonia(working);
            }

            return Utility.IconUtility.GetImageRaw(concreteRef.URI);
        }
    }
}
