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
        /// <summary>
        /// Cache of decoded base SKBitmaps keyed by the pack-relative file path.
        /// Multiple <see cref="ConcreteImageReference"/> instances that point to
        /// the same source image but with different filters share the decoded
        /// base bitmap.  Cleared on pack unload via <see cref="ClearSourceCache"/>.
        /// </summary>
        static readonly Dictionary<string, SKBitmap> sSourceCache
            = new Dictionary<string, SKBitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Clears the source image cache.  Called by
        /// <see cref="ImageReferenceService.ClearImageCache"/> on pack unload.
        /// </summary>
        public static void ClearSourceCache()
        {
            lock (sSourceCache)
            {
                foreach (var kvp in sSourceCache)
                    kvp.Value?.Dispose();
                sSourceCache.Clear();
            }
        }

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
                if (EmoTracker.Data.Sessions.ActiveSession.Primary?.Package == null)
                    return null;

                string filePath = string.Format("{0}{1}",
                    Uri.UnescapeDataString(concreteRef.URI.Host),
                    Uri.UnescapeDataString(concreteRef.URI.AbsolutePath));

                // Acquire-and-clone the cached base bitmap under the same lock that
                // ClearSourceCache holds when it disposes entries. Without this scope
                // a pack-load event can fire ClearSourceCache between our cache lookup
                // and the Copy(), freeing the SKBitmap's native pixel-ref while we're
                // still pointing at it — leading to a 0xC0000005 inside Skia's shader
                // fallback path (sk_bitmap_make_shader). The decode + Copy are both
                // fast (~ms scale) and only block other ConcreteImageReference work,
                // so widening the lock here is acceptable.
                SKBitmap working;
                lock (sSourceCache)
                {
                    if (!sSourceCache.TryGetValue(filePath, out SKBitmap baseSK))
                    {
                        using (Stream s = EmoTracker.Data.Sessions.ActiveSession.Primary?.Package.Open(filePath))
                        {
                            if (s == null)
                                return null;

                            baseSK = Utility.IconUtility.DecodeSKBitmap(s);
                        }

                        if (baseSK == null)
                            return null;

                        sSourceCache[filePath] = baseSK;
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
                    EmoTracker.Data.Sessions.ActiveSession.Primary?.Package, working, concreteRef.Filter);

                // Convert to Avalonia IImage once at the end, computing the
                // alpha mask for InputMaskingImage hit-testing.
                return Utility.IconUtility.FinalizeToAvalonia(working);
            }

            return Utility.IconUtility.GetImageRaw(concreteRef.URI);
        }
    }
}
