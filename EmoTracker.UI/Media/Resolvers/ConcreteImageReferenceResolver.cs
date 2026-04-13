using EmoTracker.Data;
using EmoTracker.Data.Media;
using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Media;

namespace EmoTracker.UI.Media.Resolvers
{
    public class ConcreteImageReferenceResolver : ImageReferenceResolver
    {
        /// <summary>
        /// Weak-reference cache of decoded base images keyed by the
        /// pack-relative file path (the gamepackage:// URI path component).
        /// Multiple <see cref="ConcreteImageReference"/> instances that point
        /// to the same source image but with different filters will share the
        /// decoded base image as long as at least one reference is alive.
        /// </summary>
        static readonly Dictionary<string, WeakReference<IImage>> sSourceCache
            = new Dictionary<string, WeakReference<IImage>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Clears the source image cache.  Called by
        /// <see cref="ImageReferenceService.ClearImageCache"/> on pack unload.
        /// </summary>
        public static void ClearSourceCache()
        {
            lock (sSourceCache)
            {
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
                if (Tracker.Instance.ActiveGamePackage == null)
                    return null;

                string filePath = string.Format("{0}{1}",
                    Uri.UnescapeDataString(concreteRef.URI.Host),
                    Uri.UnescapeDataString(concreteRef.URI.AbsolutePath));

                // Try to reuse a previously decoded base image for the same path
                IImage baseImage = GetCachedSource(filePath);
                if (baseImage == null)
                {
                    using (Stream s = Tracker.Instance.ActiveGamePackage.Open(filePath))
                    {
                        if (s == null)
                            return null;

                        baseImage = Utility.IconUtility.GetImage(s);
                    }

                    if (baseImage != null)
                        PutCachedSource(filePath, baseImage);
                }

                return Utility.IconUtility.ApplyFilterSpecToImage(
                    Tracker.Instance.ActiveGamePackage, baseImage, concreteRef.Filter);
            }

            return Utility.IconUtility.GetImageRaw(concreteRef.URI);
        }

        static IImage GetCachedSource(string filePath)
        {
            lock (sSourceCache)
            {
                if (sSourceCache.TryGetValue(filePath, out var weakRef) &&
                    weakRef.TryGetTarget(out IImage image))
                {
                    return image;
                }

                // Entry expired or not present – clean up stale entry
                sSourceCache.Remove(filePath);
                return null;
            }
        }

        static void PutCachedSource(string filePath, IImage image)
        {
            lock (sSourceCache)
            {
                sSourceCache[filePath] = new WeakReference<IImage>(image);
            }
        }
    }
}
