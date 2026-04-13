using EmoTracker.Core;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media.Resolvers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media;

namespace EmoTracker.UI.Media
{
    public class ImageReferenceService : ObservableSingleton<ImageReferenceService>
    {
        ConcurrentDictionary<ImageReference, IImage> mCache = new ConcurrentDictionary<ImageReference, IImage>();
        readonly object mResolutionLock = new object();
        CancellationTokenSource mPreCacheCts;

        public void ClearImageCache()
        {
            mPreCacheCts?.Cancel();
            mPreCacheCts = null;
            mCache.Clear();
        }

        public IImage ResolveImageReference(ImageReference imageRef)
        {
            if (imageRef == null)
                return null;

            // Fast path: lock-free cache read
            if (mCache.TryGetValue(imageRef, out IImage cachedSrc))
                return cachedSrc;

            // Slow path: acquire lock for resolution
            lock (mResolutionLock)
            {
                // Double-check after acquiring lock (background may have resolved it)
                if (mCache.TryGetValue(imageRef, out cachedSrc))
                    return cachedSrc;

                return ResolveAndCache(imageRef);
            }
        }

        IImage ResolveAndCache(ImageReference imageRef)
        {
            foreach (ImageReferenceResolver entry in TypedObjectRegistry<ImageReferenceResolver>.SupportRegistry)
            {
                if (entry.CanResolveReference(imageRef))
                {
                    IImage src = entry.ResolveReference(imageRef);
                    if (src != null)
                        mCache[imageRef] = src;
                    return src;
                }
            }

            return null;
        }

        public Task PreCacheImagesAsync(List<ImageReference> refs)
        {
            mPreCacheCts?.Cancel();

            var cts = new CancellationTokenSource();
            mPreCacheCts = cts;
            var token = cts.Token;

            // Sort: ConcreteImageReference first (base images), then Filter, then Layered
            // This avoids redundant recursive resolution during pre-cache
            var sorted = refs
                .OrderBy(r => r is ConcreteImageReference ? 0 : r is FilterImageReference ? 1 : 2)
                .ToList();

            return Task.Run(() =>
            {
                foreach (var imageRef in sorted)
                {
                    if (token.IsCancellationRequested)
                        break;

                    // Skip if already cached (lock-free read)
                    if (mCache.ContainsKey(imageRef))
                        continue;

                    lock (mResolutionLock)
                    {
                        // Double-check after lock
                        if (mCache.ContainsKey(imageRef))
                            continue;

                        try
                        {
                            ResolveAndCache(imageRef);
                        }
                        catch
                        {
                            // Swallow individual failures during pre-cache;
                            // they will surface if the UI requests them later.
                        }
                    }
                }
            }, token);
        }
    }
}
