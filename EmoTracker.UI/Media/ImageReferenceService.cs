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
        readonly ConcurrentDictionary<ImageReference, IImage> mCache = new ConcurrentDictionary<ImageReference, IImage>();
        readonly object mResolutionLock = new object();

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

        // ── Worker Thread ───────────────────────────────────────────────
        Thread mWorkerThread;
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
            if (mWorkerThread != null)
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

            mWorkerThread = new Thread(WorkerLoop)
            {
                Name = "ImageReferenceService Worker",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            mWorkerThread.Start();
        }

        /// <summary>
        /// Signals the background worker to stop and waits for it to finish.
        /// Call at application shutdown.
        /// </summary>
        public void Stop()
        {
            mShutdown = true;
            mQueueSignal.Set();

            ImageReference.OnImageReferenceCreated = null;

            // Don't block indefinitely – the thread is IsBackground so it
            // will be torn down if the process exits.
            mWorkerThread?.Join(2000);
            mWorkerThread = null;
        }

        /// <summary>
        /// Clears all cached images and drains the work queue.
        /// Called on pack unload so stale images are discarded.
        /// </summary>
        public void ClearImageCache()
        {
            lock (mQueueLock)
            {
                mQueue.Clear();
                mQueueIndex.Clear();
                mQueueSignal.Reset();
            }
            mCache.Clear();

            // Clear the source image cache used by ConcreteImageReferenceResolver
            ConcreteImageReferenceResolver.ClearSourceCache();
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
            if (mCache.ContainsKey(imageRef))
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
            mCache.TryGetValue(imageRef, out IImage cached);
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

            // Fast path: lock-free cache read
            if (mCache.TryGetValue(imageRef, out IImage cachedSrc))
                return cachedSrc;

            // Slow path: acquire lock for resolution
            IImage result;
            lock (mResolutionLock)
            {
                // Double-check after acquiring lock
                if (mCache.TryGetValue(imageRef, out cachedSrc))
                    return cachedSrc;

                result = ResolveAndCache(imageRef);
            }

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

            if (mCache.TryGetValue(imageRef, out IImage cached))
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
        /// Returns the number of resolved images in the cache.
        /// </summary>
        public int CacheCount => mCache.Count;

        // ── Internal Resolution ─────────────────────────────────────────

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
                    if (mCache.ContainsKey(imageRef))
                    {
                        PostResolvedImage(imageRef, mCache[imageRef]);
                        continue;
                    }

                    try
                    {
                        IImage resolved;
                        lock (mResolutionLock)
                        {
                            // Double-check under lock
                            if (mCache.TryGetValue(imageRef, out resolved))
                            {
                                PostResolvedImage(imageRef, resolved);
                                continue;
                            }

                            resolved = ResolveAndCache(imageRef);
                        }

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

            // ResolvedImage must be set on the UI thread so that
            // PropertyChanged fires there and Avalonia bindings update.
            Dispatcher.UIThread.Post(() =>
            {
                imageRef.ResolvedImage = resolved;
            });
        }
    }
}
