using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media.Resolvers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace EmoTracker.UI.Media
{
    public class ImageReferenceService : ObservableSingleton<ImageReferenceService>
    {
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
    }
}
