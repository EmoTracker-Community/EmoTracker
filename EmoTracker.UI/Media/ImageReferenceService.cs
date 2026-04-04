using EmoTracker.Core;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media.Resolvers;
using System.Collections.Generic;

#if WINDOWS
using System.Windows.Media;
#else
using Avalonia.Media;
#endif

namespace EmoTracker.UI.Media
{
    public class ImageReferenceService : ObservableSingleton<ImageReferenceService>
    {
#if WINDOWS
        Dictionary<ImageReference, ImageSource> mCache = new Dictionary<ImageReference, ImageSource>();

        public void ClearImageCache()
        {
            mCache.Clear();
        }

        public ImageSource ResolveImageReference(ImageReference imageRef)
        {
            if (imageRef == null)
                return null;

            ImageSource cachedSrc;
            if (mCache.TryGetValue(imageRef, out cachedSrc))
                return cachedSrc;

            foreach (ImageReferenceResolver entry in TypedObjectRegistry<ImageReferenceResolver>.SupportRegistry)
            {
                if (entry.CanResolveReference(imageRef))
                {
                    ImageSource src = entry.ResolveReference(imageRef);
                    try
                    {
                        if (src != null && src.CanFreeze)
                            src.Freeze();
                    }
                    catch { }
                    mCache[imageRef] = src;
                    return src;
                }
            }

            return null;
        }
#else
        Dictionary<ImageReference, IImage> mCache = new Dictionary<ImageReference, IImage>();

        public void ClearImageCache()
        {
            mCache.Clear();
        }

        public IImage ResolveImageReference(ImageReference imageRef)
        {
            if (imageRef == null)
                return null;

            if (mCache.TryGetValue(imageRef, out IImage cachedSrc))
                return cachedSrc;

            foreach (ImageReferenceResolver entry in TypedObjectRegistry<ImageReferenceResolver>.SupportRegistry)
            {
                if (entry.CanResolveReference(imageRef))
                {
                    IImage src = entry.ResolveReference(imageRef);
                    mCache[imageRef] = src;
                    return src;
                }
            }

            return null;
        }
#endif
    }
}
