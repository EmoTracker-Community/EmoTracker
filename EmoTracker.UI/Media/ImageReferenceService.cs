using EmoTracker.Core;
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
        //   * Same-key dedupe: at most one entry per key sits in the
        //     work queue (mQueueIndex); recursive sub-resolves go
        //     through the cache fast path on hit.
        // Wasted CPU on a same-key race is bounded by the rare case of
        // a request being re-queued while a worker is mid-resolve,
        // which collapses to "two threads computed the same image".

        // Returns the (typed) ImageReference cache for the supplied
        // reference: the back-referenced PackageInstance's ImageCache, or
        // mFallbackCache if no PI is bound. The PackageInstance side
        // stores values as <c>object</c> because it lives in
        // EmoTracker.Data and can't see Avalonia's IImage type — we
        // round-trip through that boxing here.
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
        // Cache of transparent placeholder bitmaps keyed by (width, height).
        // Shared across all references with the same source dimensions so
        // we don't create thousands of identical bitmaps.
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

        // ── Priority Queue ──────────────────────────────────────────────
        readonly object mQueueLock = new object();
        readonly SortedDictionary<(int Priority, long Order), ImageReference> mQueue = new SortedDictionary<(int, long), ImageReference>();
        readonly Dictionary<ImageReference, (int Priority, long Order)> mQueueIndex = new Dictionary<ImageReference, (int, long)>();
        long mInsertionOrder;
        readonly ManualResetEventSlim mQueueSignal = new ManualResetEventSlim(false);

        // ── Worker Threads ──────────────────────────────────────────────
        // One worker per logical core so image resolution can use the
        // full CPU on systems where the user is loading large packs (or
        // many packs across multi-state workspaces). Image decode +
        // filter passes are CPU-bound for typical PNG icon work; per-PI
        // source-bitmap loading is serialised by SourceImageCacheLock,
        // and the IImage cache is a ConcurrentDictionary, so the workers
        // can run in parallel without a global resolution lock.
        List<Thread> mWorkerThreads;
        volatile bool mShutdown;

        /// <summary>
        /// When true, <see cref="RequestImage"/> resolves synchronously on the
        /// calling thread (the pre-refactor behavior).  The background worker
        /// thread is not started.  Set before calling <see cref="Start"/>.
        /// </summary>
        public bool SyncMode { get; set; }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Starts the background worker thread and wires up the
        /// <see cref="ImageReference.OnImageReferenceCreated"/> callback
        /// so that newly-created references are automatically queued.
        /// Call once at application startup.
        /// </summary>
        public void Start()
        {
            if (mWorkerThreads != null)
                return;

            mShutdown = false;

            if (SyncMode)
            {
                // In sync mode, resolve images immediately on creation so
                // path-through bindings ({Binding Icon.ResolvedImage}) see
                // the resolved image right away.
                ImageReference.OnImageReferenceCreated = (imageRef) =>
                {
                    ResolveImageReference(imageRef);
                };
                return;
            }

            ImageReference.OnImageReferenceCreated = (imageRef) =>
            {
                // Multiple ImageReference objects can share the same equality key
                // (same URI + filter) while being distinct object instances (e.g.
                // 10 items that use the same small-key icon each create their own
                // ConcreteImageReference).  If the image is already cached (first
                // instance was resolved), set the result immediately.
                IImage cached = GetCachedImage(imageRef);
                if (cached != null)
                {
                    imageRef.ResolvedImage = cached;
                    return;
                }

                // Register this instance so that when resolution completes for
                // any equal key, ALL instances get their ResolvedImage updated.
                RegisterPendingInstance(imageRef);

                // Set a correctly-sized placeholder as ResolvedImage immediately
                // so Avalonia's layout system measures controls at the right size
                // before the real image is resolved on the background thread.
                if (imageRef.ResolvedImage == null &&
                    imageRef.SourceWidth > 0 && imageRef.SourceHeight > 0)
                {
                    imageRef.ResolvedImage = GetPlaceholder(imageRef);
                }

                QueueResolution(imageRef, ImagePriority.Normal);
            };

            // One worker thread per logical CPU core. Image decode +
            // filter passes are CPU-bound, so saturating the available
            // cores cuts pack-load time noticeably on multi-state /
            // multi-pack workspaces. ProcessorCount of 0 (very rare in
            // sandboxed environments) is clamped to 1.
            int workerCount = Math.Max(1, Environment.ProcessorCount);
            mWorkerThreads = new List<Thread>(workerCount);
            for (int i = 0; i < workerCount; i++)
            {
                var t = new Thread(WorkerLoop)
                {
                    Name = "ImageReferenceService Worker " + i,
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                };
                mWorkerThreads.Add(t);
                t.Start();
            }
        }

        /// <summary>
        /// Signals the background workers to stop and waits for them to finish.
        /// Call at application shutdown.
        /// </summary>
        public void Stop()
        {
            mShutdown = true;
            mQueueSignal.Set();

            ImageReference.OnImageReferenceCreated = null;

            if (mWorkerThreads != null)
            {
                // Don't block indefinitely on any single thread — they're
                // IsBackground so the process tear-down will catch any
                // straggler that ignored the shutdown flag (e.g. one that's
                // mid-Skia decode and won't yield until that finishes).
                foreach (var t in mWorkerThreads)
                {
                    try { t.Join(2000); } catch { /* defensive */ }
                }
                mWorkerThreads = null;
            }
        }

        /// <summary>
        /// Clears all cached images and drains the work queue.
        /// Called on pack unload so stale images are discarded.
        /// </summary>
        public void ClearImageCache()
        {
            // Phase 7.1.h: per-PI caches are owned by PackageInstance
            // and torn down on its Dispose; this method now only clears
            // the work queue, pending-instance bookkeeping, and the
            // service-wide fallback cache for non-pack-bound refs.
            // Per-PI image caches and the per-PI source-bitmap cache are
            // NOT cleared here.
            lock (mQueueLock)
            {
                mQueue.Clear();
                mQueueIndex.Clear();
                mQueueSignal.Reset();
            }
            mFallbackCache.Clear();

            lock (mInstancesLock)
            {
                mPendingInstances.Clear();
            }
        }

        /// <summary>
        /// Adds an <see cref="ImageReference"/> to the background work queue
        /// at the specified priority.  If the reference is already queued at a
        /// lower priority (higher numeric value), it is boosted.
        /// Thread-safe; may be called from any thread.
        /// </summary>
        public void QueueResolution(ImageReference imageRef, ImagePriority priority)
        {
            if (imageRef == null)
                return;

            // Already resolved – nothing to do.
            if (ContainsCached(imageRef))
                return;

            int pri = (int)priority;

            lock (mQueueLock)
            {
                if (mQueueIndex.TryGetValue(imageRef, out var existing))
                {
                    if (pri >= existing.Priority)
                        return; // already at same or higher priority

                    // Remove old entry and re-insert at higher priority
                    mQueue.Remove(existing);
                    mQueueIndex.Remove(imageRef);
                }

                var key = (pri, Interlocked.Increment(ref mInsertionOrder));
                mQueue[key] = imageRef;
                mQueueIndex[imageRef] = key;
                mQueueSignal.Set();
            }
        }

        /// <summary>
        /// Returns the cached image for the given reference, or <c>null</c>
        /// if it has not been resolved yet.
        /// </summary>
        public IImage GetCachedImage(ImageReference imageRef)
        {
            if (imageRef == null)
                return null;
            TryGetCached(imageRef, out IImage cached);
            return cached;
        }

        /// <summary>
        /// Synchronously resolves an image reference.  Used by composite
        /// resolvers (Filter, Layered) that need resolved sub-images
        /// during background resolution.  Not intended for UI-thread callers –
        /// those should read <see cref="ImageReference.ResolvedImage"/> or call
        /// <see cref="RequestImage"/> instead.
        /// </summary>
        public IImage ResolveImageReference(ImageReference imageRef)
        {
            if (imageRef == null)
                return null;

            // Fast path: lock-free cache read.
            // In sync mode, also set ResolvedImage on the requesting object so
            // duplicate ImageReference instances (same URI+filter, different object)
            // get the resolved image immediately.
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
            // bindings see the image immediately.  This is safe because
            // sync-mode callers are on the UI thread.
            if (result != null && SyncMode)
                imageRef.ResolvedImage = result;

            return result;
        }

        /// <summary>
        /// Called by UI-thread code (converters, bindings) when an image is
        /// needed for display.  Returns the cached image if available, otherwise
        /// boosts the reference to <see cref="ImagePriority.Immediate"/> and
        /// returns a correctly-sized placeholder.
        /// </summary>
        public IImage RequestImage(ImageReference imageRef)
        {
            if (imageRef == null)
                return null;

            if (TryGetCached(imageRef, out IImage cached))
                return cached;

            if (SyncMode)
                return ResolveImageReference(imageRef);

            QueueResolution(imageRef, ImagePriority.Immediate);
            return GetPlaceholder(imageRef);
        }

        /// <summary>
        /// Returns the number of items currently in the background work queue.
        /// </summary>
        public int QueueCount
        {
            get { lock (mQueueLock) { return mQueue.Count; } }
        }

        /// <summary>
        /// Returns the number of resolved images in the service-wide
        /// fallback cache (per-PI caches are not summed here).
        /// </summary>
        public int CacheCount => mFallbackCache.Count;

        // ── Instance Tracking ───────────────────────────────────────────

        /// <summary>
        /// Registers an ImageReference object so that when an equal key is
        /// resolved, this specific object's ResolvedImage gets set too.
        /// </summary>
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

        /// <summary>
        /// Removes and returns all pending instances for the given equality key.
        /// </summary>
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

        // ── Background Worker ───────────────────────────────────────────

        void WorkerLoop()
        {
            while (!mShutdown)
            {
                // Wait for work or shutdown signal
                mQueueSignal.Wait();

                if (mShutdown)
                    break;

                // Process items until the queue is drained
                while (TryDequeueNext(out var imageRef))
                {
                    if (mShutdown)
                        break;

                    // Skip if already resolved (may have been resolved by a
                    // recursive call from a Filter/Layered resolver).
                    if (TryGetCached(imageRef, out IImage already))
                    {
                        PostResolvedImage(imageRef, already);
                        continue;
                    }

                    try
                    {
                        IImage resolved;
                        // No global lock here — multiple workers can
                        // resolve different keys concurrently. Same-key
                        // races (rare; only when a request is re-queued
                        // while another worker is mid-resolve) collapse
                        // to "two threads computed the same bitmap" —
                        // the cache stores semantically-equivalent
                        // results either way.
                        if (!TryGetCached(imageRef, out resolved))
                            resolved = ResolveAndCache(imageRef);

                        PostResolvedImage(imageRef, resolved);
                    }
                    catch
                    {
                        // Individual resolution failures are silently ignored;
                        // the UI will continue showing the placeholder.
                    }
                }

                // All Immediate-priority items have been resolved.  Force
                // the UI to re-measure layouts so controls that were sized
                // for placeholders adopt the final image dimensions.
                Dispatcher.UIThread.Post(() =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.MainWindow?.InvalidateMeasure();
                    }
                }, DispatcherPriority.Background);
            }
        }

        bool TryDequeueNext(out ImageReference imageRef)
        {
            lock (mQueueLock)
            {
                if (mQueue.Count == 0)
                {
                    mQueueSignal.Reset();
                    imageRef = null;
                    return false;
                }

                // SortedDictionary enumerator yields items in key order
                // (lowest priority value first, then lowest insertion order).
                using (var enumerator = mQueue.GetEnumerator())
                {
                    enumerator.MoveNext();
                    var key = enumerator.Current.Key;
                    imageRef = enumerator.Current.Value;
                    mQueue.Remove(key);
                    mQueueIndex.Remove(imageRef);
                }

                return true;
            }
        }

        void PostResolvedImage(ImageReference imageRef, IImage resolved)
        {
            if (resolved == null)
                return;

            // Collect ALL object instances that share this equality key.
            // Only one instance enters the queue, but many may exist (e.g.
            // 10 items using the same small-key icon).  Every instance's
            // ResolvedImage must be set so their bindings update.
            var instances = TakePendingInstances(imageRef);

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
                    // Fallback: set on the specific object that was dequeued
                    imageRef.ResolvedImage = resolved;
                }
            });
        }
    }
}
