using EmoTracker.Data;
using EmoTracker.Data.Media;
using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Media;
using SkiaSharp;
using EmoTracker.Data.Session;

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
                if (TrackerSession.Current.Tracker.ActiveGamePackage == null)
                    return null;

                string filePath = string.Format("{0}{1}",
                    Uri.UnescapeDataString(concreteRef.URI.Host),
                    Uri.UnescapeDataString(concreteRef.URI.AbsolutePath));

                // Get the decoded base SKBitmap from cache, or decode it
                SKBitmap baseSK = GetCachedSource(filePath);
                if (baseSK == null)
                {
                    using (Stream s = TrackerSession.Current.Tracker.ActiveGamePackage.Open(filePath))
                    {
                        if (s == null)
                            return null;

                        baseSK = Utility.IconUtility.DecodeSKBitmap(s);
                    }

                    if (baseSK == null)
                        return null;

                    PutCachedSource(filePath, baseSK);
                }

                // Clone the base bitmap so filter operations don't mutate the
                // cached original, then run the entire filter chain in SKBitmap
                // space (no intermediate PNG round-trips).
                SKBitmap working = baseSK.Copy();
                working = Utility.IconUtility.ApplyFilterSpecToSKBitmap(
                    TrackerSession.Current.Tracker.ActiveGamePackage, working, concreteRef.Filter);

                // Convert to Avalonia IImage once at the end, computing the
                // alpha mask for InputMaskingImage hit-testing.
                return Utility.IconUtility.FinalizeToAvalonia(working);
            }

            return Utility.IconUtility.GetImageRaw(concreteRef.URI);
        }

        static SKBitmap GetCachedSource(string filePath)
        {
            lock (sSourceCache)
            {
                if (sSourceCache.TryGetValue(filePath, out SKBitmap bmp))
                    return bmp;
                return null;
            }
        }

        static void PutCachedSource(string filePath, SKBitmap bmp)
        {
            lock (sSourceCache)
            {
                sSourceCache[filePath] = bmp;
            }
        }
    }
}
