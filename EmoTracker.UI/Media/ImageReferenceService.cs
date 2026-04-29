using EmoTracker.Core;
using EmoTracker.Core.Threading;
using EmoTracker.Data.Media;
using EmoTracker.UI.Media.Resolvers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace EmoTracker.UI.Media
{
    /// <summary>
    /// Priority levels for image resolution work items.
    /// Lower numeric value = higher priority.
    /// </summary>
    public enum ImagePriority
    {
        /// <summary>The UI is waiting to display this image right now.</summary>
        Immediate = 0,
        /// <summary>Standard pre-cache priority during pack load.</summary>
        Normal = 100,
    }

    /// <summary>
    /// Image-resolution scheduler. Owns the per-PI <see cref="IImage"/>
    /// cache, the placeholder bitmaps, the equality-key fan-out, and
    /// the OnCreated hook that auto-queues new <see cref="ImageReference"/>
    /// instances. Scheduling itself (worker pool, priority queue,
    /// per-key dedupe, queue-drain notification) lives on a generic
    /// <see cref="PriorityWorkQueue{TKey}"/>.
    /// </summary>
    public class ImageReferenceService : ObservableSingleton<ImageReferenceService>
    {
        // ── Cache ───────────────────────────────────────────────────────
        // Phase 7.1.h: per-PackageInstance image caches replaced the
        // previous process-wide cache. The active cache for an
        // ImageReference is owned by its PackageInstance back-ref. For
        // refs without a PI (FromExternalURI, tests), we fall back to
        // mFallbackCache below.
        readonly ConcurrentDictionary<ImageReference, IImage> mFallbackCache = new ConcurrentDictionary<ImageReference, IImage>();
        // Note: we deliberately do NOT hold a process-wide resolution
        // lock. With multiple worker threads, the safety story is:
        //   * IImage cache: ConcurrentDictionary handles concurrent
        //     get/set on different keys; same-key races just store the
        //     same image twice (idempotent).
        //   * Per-PI source-bitmap cache: protected by
        //     PackageInstance.SourceImageCacheLock inside the
        //     ConcreteImageReferenceResolver, so pack reads + base
        //     SKBitmap retention are serialised per-PI.
        //   * Same-key dedupe: the PriorityWorkQueue keeps at most one
        //     entry per key; recursive sub-resolves go through the
        //     cache fast path on hit.
        // Wasted CPU on a same-key race is bounded by the rare case of
        // a request being re-queued while a worker is mid-resolve,
        // which collapses to "two threads computed the same image".

        bool TryGetCached(ImageReference imageRef, out IImage image)
        {
            image = null;
            if (imageRef == null) return false;
            var pi = imageRef.PackageInstance;
            if (pi != null)
            {
                if (pi.ImageCache.TryGetValue(imageRef, out var boxed) && boxed is IImage img)
                {
                    image = img;
                    return true;
                }
                return false;
            }
            return mFallbackCache.TryGetValue(imageRef, out image);
        }

        bool ContainsCached(ImageReference imageRef)
        {
            if (imageRef == null) return false;
            var pi = imageRef.PackageInstance;
            if (pi != null) return pi.ImageCache.ContainsKey(imageRef);
            return mFallbackCache.ContainsKey(imageRef);
        }

        void StoreCached(ImageReference imageRef, IImage image)
        {
            if (imageRef == null || image == null) return;
            var pi = imageRef.PackageInstance;
            if (pi != null)
                pi.ImageCache[imageRef] = image;
            else
                mFallbackCache[imageRef] = image;
        }

        // ── Instance Tracking ───────────────────────────────────────────
        // Multiple distinct ImageReference objects can share the same equality
        // key (e.g. 10 items that all use "items/small_key.png" each create
        // their own ConcreteImageReference).  Only one object per equality key
        // enters the work queue, but ALL objects need their ResolvedImage set
        // when resolution completes.  This dictionary tracks every unresolved
        // instance keyed by equality so PostResolvedImage can fan out to all.
        readonly Dictionary<ImageReference, List<ImageReference>> mPendingInstances = new Dictionary<ImageReference, List<ImageReference>>();
        readonly object mInstancesLock = new object();

        // ── Sized Placeholders ──────────────────────────────────────────
        static readonly Dictionary<(int w, int h), IImage> sPlaceholderCache = new Dictionary<(int, int), IImage>();
        static readonly object sPlaceholderLock = new object();

        /// <summary>
        /// Returns a transparent placeholder image of the specified size.
        /// Reuses cached instances for identical dimensions.
        /// Falls back to 1×1 if width or height is zero.
        /// </summary>
        public static IImage GetPlaceholder(int width, int height)
        {
            if (width <= 0 || height <= 0)
                width = height = 1;

            lock (sPlaceholderLock)
            {
                var key = (width, height);
                if (sPlaceholderCache.TryGetValue(key, out IImage existing))
                    return existing;

                var bmp = new WriteableBitmap(
                    new Avalonia.PixelSize(width, height),
                    new Avalonia.Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888,
                    AlphaFormat.Premul);
                sPlaceholderCache[key] = bmp;
                return bmp;
            }
        }

        /// <summary>
        /// Returns a correctly-sized transparent placeholder for the given
        /// image reference, using its <see cref="ImageReference.SourceWidth"/>
        /// and <see cref="ImageReference.SourceHeight"/> to determine size.
        /// </summary>
        public static IImage GetPlaceholder(ImageReference imageRef)
        {
            if (imageRef == null)
                return GetPlaceholder(1, 1);
            return GetPlaceholder(imageRef.SourceWidth, imageRef.SourceHeight);
        }

        // ── Worker Queue ─────────────────────────────────────────────────
        // Generic priority queue + worker pool. One worker per logical
        // CPU core (image decode + filter is CPU-bound). Drain event
        // forces a UI relayout so controls sized for placeholders adopt
        // the resolved image dimensions.
        PriorityWorkQueue<ImageReference> mQueue;

        /// <summary>
        /// When true, <see cref="RequestImage"/> resolves synchronously on the
        /// calling thread (the pre-refactor behavior).  The background worker
        /// thread is not started.  Set before calling <see cref="Start"/>.
        /// </summary>
        public bool SyncMode { get; set; }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Starts the background worker threads and wires up the
        /// <see cref="ImageReference.OnImageReferenceCreated"/> callback
        /// so newly-created references are automatically queued.
        /// Call once at application startup.
        /// </summary>
        public void Start()
        {
            if (mQueue != null && mQueue.IsRunning)
                return;

            if (SyncMode)
            {
                // Sync mode: resolve images on creation so path-through
                // bindings ({Binding Icon.ResolvedImage}) see the result
                // immediately. No worker pool started.
                ImageReference.OnImageReferenceCreated = (imageRef) =>
                {
                    ResolveImageReference(imageRef);
                };
                return;
            }

            mQueue = new PriorityWorkQueue<ImageReference>(
                name: "ImageReferenceService",
                workerCount: -1, // ProcessorCount
                threadPriority: ThreadPriority.BelowNormal,
                onException: null /* swallowed; UI keeps placeholder */);

            // Once a batch of work drains, force the UI to re-measure
            // layouts so controls sized for placeholders adopt the
            // resolved image dimensions. Posted to the UI thread at
            // Background priority so it doesn't preempt user input.
            mQueue.QueueDrained += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.MainWindow?.InvalidateMeasure();
                    }
                }, DispatcherPriority.Background);
            };

            ImageReference.OnImageReferenceCreated = (imageRef) =>
            {
                // Multiple ImageReference objects can share the same equality
                // key (same URI + filter) while being distinct object
                // instances. The cache-check + pending-register sequence
                // must be ATOMIC w.r.t. the worker's StoreCached +
                // TakePendingInstances. Without atomicity:
                //   T1 (worker): StoreCached(K, img)
                //   T2 (here):   GetCachedImage(K) -> null  (sees stale empty)
                //   T1 (worker): TakePendingInstances(K) -> []
                //   T2 (here):   RegisterPendingInstance(K) -> creates new empty list
                //   T2 (here):   QueueResolution -> sees cache hit, no-op
                // and the new instance orphans on the placeholder forever.
                //
                // Locking the registration sequence + having the worker
                // store-cache under the same lock closes the window: T2
                // either sees the cache hit and applies directly, or
                // registers in time for T1's TakePending to find it.
                IImage cached;
                bool shouldQueue = false;
                lock (mInstancesLock)
                {
                    if (TryGetCached(imageRef, out cached))
                    {
                        // Cache hit — apply directly outside the lock
                        // (don't hold mInstancesLock across a property
                        // setter that fires PropertyChanged).
                    }
                    else
                    {
                        // Cache miss — register so the worker fans out
                        // its result to us. Only the first registrant
                        // for this key needs to enqueue work; subsequent
                        // distinct instances ride along on the same
                        // resolution.
                        if (!mPendingInstances.TryGetValue(imageRef, out var list))
                        {
                            list = new List<ImageReference>();
                            mPendingInstances[imageRef] = list;
                            shouldQueue = true;
                        }
                        list.Add(imageRef);
                    }
                }

                if (cached != null)
                {
                    imageRef.ResolvedImage = cached;
                    return;
                }

                // Set a correctly-sized placeholder as ResolvedImage immediately
                // so Avalonia's layout system measures controls at the right
                // size before the real image is resolved on the worker thread.
                if (imageRef.ResolvedImage == null &&
                    imageRef.SourceWidth > 0 && imageRef.SourceHeight > 0)
                {
                    imageRef.ResolvedImage = GetPlaceholder(imageRef);
                }

                if (shouldQueue)
                    QueueResolution(imageRef, ImagePriority.Normal);
            };

            mQueue.Start();
        }

        /// <summary>
        /// Signals the background workers to stop and waits briefly for them
        /// to finish. Call at application shutdown.
        /// </summary>
        public void Stop()
        {
            ImageReference.OnImageReferenceCreated = null;
            if (mQueue != null)
            {
                mQueue.Dispose();
                mQueue = null;
            }
        }

        /// <summary>
        /// Clears the work queue and the service-wide fallback cache for
        /// non-pack-bound refs. Per-PI image / source-bitmap caches are
        /// owned by <see cref="EmoTracker.Data.Sessions.PackageInstance"/>
        /// and torn down on its Dispose; NOT cleared here.
        /// </summary>
        public void ClearImageCache()
        {
            mQueue?.Clear();
            mFallbackCache.Clear();
            lock (mInstancesLock)
            {
                mPendingInstances.Clear();
            }
        }

        /// <summary>
        /// Adds an <see cref="ImageReference"/> to the background work queue
        /// at the specified priority. If the reference is already queued at a
        /// lower priority (higher numeric value), it is boosted.
        /// Thread-safe; may be called from any thread.
        /// </summary>
        public void QueueResolution(ImageReference imageRef, ImagePriority priority)
        {
            if (imageRef == null) return;
            if (ContainsCached(imageRef)) return;
            if (mQueue == null) return; // sync mode or pre-Start

            // Capture the imageRef into the work delegate; the queue
            // dedupes so the closure is created once per (key, boost).
            var captured = imageRef;
            mQueue.Enqueue(captured, (int)priority, () => ResolveAndPost(captured));
        }

        /// <summary>
        /// Returns the cached image for the given reference, or <c>null</c>
        /// if it has not been resolved yet.
        /// </summary>
        public IImage GetCachedImage(ImageReference imageRef)
        {
            if (imageRef == null) return null;
            TryGetCached(imageRef, out IImage cached);
            return cached;
        }

        /// <summary>
        /// Synchronously resolves an image reference. Used by composite
        /// resolvers (Filter, Layered) that need resolved sub-images
        /// during background resolution. Not intended for UI-thread callers —
        /// those should read <see cref="ImageReference.ResolvedImage"/> or call
        /// <see cref="RequestImage"/> instead.
        /// </summary>
        public IImage ResolveImageReference(ImageReference imageRef)
        {
            if (imageRef == null) return null;

            // Fast path: lock-free cache read.
            if (TryGetCached(imageRef, out IImage cachedSrc))
            {
                if (SyncMode && imageRef.ResolvedImage as IImage != cachedSrc)
                    imageRef.ResolvedImage = cachedSrc;
                return cachedSrc;
            }

            // Slow path: resolve. No global lock — per-key safety comes
            // from the ConcurrentDictionary cache + per-PI source lock
            // inside ConcreteImageReferenceResolver. Two threads racing
            // on the same key duplicate the resolve work but store
            // semantically-equivalent results.
            IImage result = ResolveAndCache(imageRef);

            // In sync mode, set ResolvedImage directly so path-through
            // bindings see the image immediately. Safe because sync-mode
            // callers are on the UI thread.
            if (result != null && SyncMode)
                imageRef.ResolvedImage = result;

            return result;
        }

        /// <summary>
        /// Called by UI-thread code (converters, bindings) when an image is
        /// needed for display. Returns the cached image if available, otherwise
        /// boosts the reference to <see cref="ImagePriority.Immediate"/> and
        /// returns a correctly-sized placeholder.
        /// </summary>
        public IImage RequestImage(ImageReference imageRef)
        {
            if (imageRef == null) return null;

            if (TryGetCached(imageRef, out IImage cached))
                return cached;

            if (SyncMode)
                return ResolveImageReference(imageRef);

            QueueResolution(imageRef, ImagePriority.Immediate);
            return GetPlaceholder(imageRef);
        }

        /// <summary>
        /// Number of items currently in the background work queue.
        /// </summary>
        public int QueueCount => mQueue?.QueueCount ?? 0;

        /// <summary>
        /// Number of resolved images in the service-wide fallback cache
        /// (per-PI caches are not summed here).
        /// </summary>
        public int CacheCount => mFallbackCache.Count;

        // ── Instance Tracking ───────────────────────────────────────────

        void RegisterPendingInstance(ImageReference imageRef)
        {
            lock (mInstancesLock)
            {
                if (!mPendingInstances.TryGetValue(imageRef, out var list))
                {
                    list = new List<ImageReference>();
                    mPendingInstances[imageRef] = list;
                }
                list.Add(imageRef);
            }
        }

        List<ImageReference> TakePendingInstances(ImageReference imageRef)
        {
            lock (mInstancesLock)
            {
                if (mPendingInstances.TryGetValue(imageRef, out var list))
                {
                    mPendingInstances.Remove(imageRef);
                    return list;
                }
                return null;
            }
        }

        // ── Internal Resolution ─────────────────────────────────────────

        IImage ResolveAndCache(ImageReference imageRef)
        {
            foreach (ImageReferenceResolver entry in TypedObjectRegistry<ImageReferenceResolver>.SupportRegistry)
            {
                if (entry.CanResolveReference(imageRef))
                {
                    IImage src = entry.ResolveReference(imageRef);
                    if (src != null)
                        StoreCached(imageRef, src);
                    return src;
                }
            }
            return null;
        }

        // Worker-thread entry point: resolve the image and post the
        // result to all instances that share the same equality key.
        // Failures are silently swallowed (the UI keeps showing the
        // placeholder).
        void ResolveAndPost(ImageReference imageRef)
        {
            // Skip if already resolved (recursive sub-resolves from
            // Filter / Layered resolvers may have populated this key).
            if (TryGetCached(imageRef, out IImage already))
            {
                PostResolvedImage(imageRef, already);
                return;
            }

            try
            {
                if (!TryGetCached(imageRef, out IImage resolved))
                    resolved = ResolveAndCache(imageRef);
                PostResolvedImage(imageRef, resolved);
            }
            catch
            {
                // Individual resolution failures are silently ignored.
            }
        }

        void PostResolvedImage(ImageReference imageRef, IImage resolved)
        {
            if (resolved == null) return;

            // Collect ALL object instances that share this equality key,
            // and re-StoreCached under the same lock that
            // OnImageReferenceCreated uses for its cache-check +
            // pending-register sequence. Atomicity here is what stops a
            // newly-created reference with the same key from registering
            // in pending AFTER we've taken pending — it instead sees the
            // cache hit (because we re-store under the lock) and applies
            // the resolved image directly without ever touching pending.
            // (StoreCached is idempotent — ResolveAndCache already wrote
            // it earlier; this re-store is just to guarantee the cache
            // visibility ordering w.r.t. the lock.)
            List<ImageReference> instances;
            lock (mInstancesLock)
            {
                StoreCached(imageRef, resolved);
                if (mPendingInstances.TryGetValue(imageRef, out var list))
                {
                    mPendingInstances.Remove(imageRef);
                    instances = list;
                }
                else
                {
                    instances = null;
                }
            }

            // ResolvedImage must be set on the UI thread so that
            // PropertyChanged fires there and Avalonia bindings update.
            Dispatcher.UIThread.Post(() =>
            {
                if (instances != null)
                {
                    foreach (var instance in instances)
                        instance.ResolvedImage = resolved;
                }
                else
                {
                    imageRef.ResolvedImage = resolved;
                }
            });
        }
    }
}
