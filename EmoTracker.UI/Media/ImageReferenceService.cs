using EmoTracker.Core;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media.Resolvers;
using System.Collections.Generic;

using Avalonia.Media;

namespace EmoTracker.UI.Media
{
    public class ImageReferenceService : ObservableSingleton<ImageReferenceService>
    {
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
    }
}
