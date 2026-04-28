using System;
using System.Collections.Generic;
using System.Threading;

namespace EmoTracker.Core.Threading
{
    /// <summary>
    /// General-purpose worker pool with a priority queue + per-key
    /// dedupe. Extracted from <c>EmoTracker.UI.Media.ImageReferenceService</c>
    /// where the same shape evolved organically; consumers needing the
    /// same scheduling story (worker pool, lower-numeric = higher
    /// priority, FIFO within priority, same-key boost-on-resubmit) can
    /// build on this rather than re-implement.
    ///
    /// <para>
    /// Threading contract:
    /// <list type="bullet">
    ///   <item>The work delegate runs on one of <c>workerCount</c>
    ///         background threads. Multiple workers can run different
    ///         keys concurrently — the queue does NOT serialize per-key
    ///         work. If your work needs per-key serialization, hold the
    ///         lock inside the delegate.</item>
    ///   <item>Exceptions thrown by a work delegate are swallowed; one
    ///         bad task can't crash the worker thread. Pass an
    ///         <c>onException</c> handler to surface them (logging,
    ///         telemetry, etc.).</item>
    ///   <item>Cancellation cancels QUEUED items only — work that's
    ///         already running runs to completion. If you need to stop
    ///         in-flight work, your delegate should poll a cooperative
    ///         cancellation token of its own.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <typeparam name="TKey">
    /// Identity key. Two enqueues with the same key collapse into one
    /// queue entry — the second submission either boosts the priority
    /// (lower numeric value wins) or no-ops if the existing entry is
    /// already at equal-or-better priority. Equality is the key type's
    /// default equality. Must support being a Dictionary key.
    /// </typeparam>
    public sealed class PriorityWorkQueue<TKey> : IDisposable
        where TKey : notnull
    {
        readonly string mName;
        readonly int mWorkerCount;
        readonly ThreadPriority mThreadPriority;
        readonly Action<Exception> mOnException;

        readonly object mLock = new object();
        readonly SortedDictionary<(int Priority, long Order), Entry> mQueue = new();
        readonly Dictionary<TKey, (int Priority, long Order)> mIndex = new();
        long mInsertionOrder;
        readonly ManualResetEventSlim mSignal = new ManualResetEventSlim(false);

        List<Thread> mWorkers;
        volatile bool mShutdown;

        readonly struct Entry
        {
            public readonly TKey Key;
            public readonly Action Work;
            public Entry(TKey key, Action work) { Key = key; Work = work; }
        }

        /// <summary>
        /// Fired (on a worker thread) once when the queue transitions
        /// from non-empty to empty AND no worker is mid-task. Useful
        /// for "redo any layout that depended on the just-completed
        /// batch" hooks. Subscribe before <see cref="Start"/>.
        /// </summary>
        public event Action QueueDrained;

        /// <param name="name">
        /// Used as the worker thread name prefix (helps debugging:
        /// <c>"&lt;name&gt; Worker 0..N"</c>).
        /// </param>
        /// <param name="workerCount">
        /// Number of worker threads. <c>-1</c> means
        /// <c>Environment.ProcessorCount</c> (clamped to ≥1).
        /// </param>
        /// <param name="threadPriority">
        /// OS thread priority for the worker threads. Defaults to
        /// <see cref="ThreadPriority.BelowNormal"/> so background work
        /// yields to the UI thread.
        /// </param>
        /// <param name="onException">
        /// Optional callback invoked when a work delegate throws. The
        /// throw doesn't propagate further; the worker continues with
        /// the next item.
        /// </param>
        public PriorityWorkQueue(
            string name,
            int workerCount = -1,
            ThreadPriority threadPriority = ThreadPriority.BelowNormal,
            Action<Exception> onException = null)
        {
            mName = string.IsNullOrEmpty(name) ? "PriorityWorkQueue" : name;
            mWorkerCount = workerCount > 0
                ? workerCount
                : Math.Max(1, Environment.ProcessorCount);
            mThreadPriority = threadPriority;
            mOnException = onException;
        }

        /// <summary>
        /// Number of items currently in the queue (NOT counting work
        /// that's already running on a worker thread).
        /// </summary>
        public int QueueCount
        {
            get { lock (mLock) { return mQueue.Count; } }
        }

        /// <summary>True iff <see cref="Start"/> has been called and Stop hasn't.</summary>
        public bool IsRunning => mWorkers != null;

        /// <summary>
        /// Start the worker threads. Idempotent — repeated calls are
        /// no-ops while the queue is already running.
        /// </summary>
        public void Start()
        {
            if (mWorkers != null) return;
            mShutdown = false;

            var workers = new List<Thread>(mWorkerCount);
            for (int i = 0; i < mWorkerCount; ++i)
            {
                var t = new Thread(WorkerLoop)
                {
                    Name = $"{mName} Worker {i}",
                    IsBackground = true,
                    Priority = mThreadPriority,
                };
                workers.Add(t);
                t.Start();
            }
            mWorkers = workers;
        }

        /// <summary>
        /// Signal the workers to stop and wait briefly for them to
        /// finish. Workers are <see cref="Thread.IsBackground"/>=true,
        /// so any straggler that ignores the shutdown flag (e.g. one
        /// stuck in unmanaged code) is caught at process tear-down.
        /// </summary>
        public void Stop()
        {
            mShutdown = true;
            mSignal.Set();
            if (mWorkers != null)
            {
                foreach (var t in mWorkers)
                {
                    try { t.Join(2000); } catch { /* defensive */ }
                }
                mWorkers = null;
            }
        }

        /// <summary>
        /// Enqueue (or boost an existing entry for) <paramref name="key"/>
        /// at the given priority. Lower numeric value = higher priority.
        /// FIFO within the same priority (insertion order breaks ties).
        ///
        /// <para>
        /// If a queued entry with the same key already exists:
        /// <list type="bullet">
        ///   <item>If the new priority is BETTER (lower), the entry's
        ///         priority is boosted and the supplied
        ///         <paramref name="work"/> replaces the old delegate.</item>
        ///   <item>Otherwise the call is a no-op (existing entry kept).</item>
        /// </list>
        /// </para>
        /// </summary>
        public void Enqueue(TKey key, int priority, Action work)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (work == null) throw new ArgumentNullException(nameof(work));

            lock (mLock)
            {
                if (mIndex.TryGetValue(key, out var existing))
                {
                    if (priority >= existing.Priority)
                        return; // same or worse → keep what's there

                    mQueue.Remove(existing);
                    mIndex.Remove(key);
                }

                var orderKey = (priority, Interlocked.Increment(ref mInsertionOrder));
                mQueue[orderKey] = new Entry(key, work);
                mIndex[key] = orderKey;
                mSignal.Set();
            }
        }

        /// <summary>
        /// True iff a queued (not-yet-running) entry exists for
        /// <paramref name="key"/>.
        /// </summary>
        public bool Contains(TKey key)
        {
            if (key == null) return false;
            lock (mLock)
            {
                return mIndex.ContainsKey(key);
            }
        }

        /// <summary>
        /// Remove <paramref name="key"/> from the queue. No effect on
        /// work that's already running. Returns true iff a queued
        /// entry was removed.
        /// </summary>
        public bool Cancel(TKey key)
        {
            if (key == null) return false;
            lock (mLock)
            {
                if (!mIndex.TryGetValue(key, out var existing)) return false;
                mQueue.Remove(existing);
                mIndex.Remove(key);
                if (mQueue.Count == 0) mSignal.Reset();
                return true;
            }
        }

        /// <summary>Drop every queued item.</summary>
        public void Clear()
        {
            lock (mLock)
            {
                mQueue.Clear();
                mIndex.Clear();
                mSignal.Reset();
            }
        }

        public void Dispose()
        {
            Stop();
            mSignal.Dispose();
        }

        // ------------------------------------------------------------------
        //  Worker
        // ------------------------------------------------------------------

        // Counts workers currently mid-task. The QueueDrained event
        // fires exactly once when (queue empty AND counter == 0)
        // transitions from false to true after a task completes.
        int mActiveWorkerCount;

        void WorkerLoop()
        {
            while (!mShutdown)
            {
                mSignal.Wait();
                if (mShutdown) break;

                while (TryDequeue(out var entry))
                {
                    if (mShutdown) break;

                    Interlocked.Increment(ref mActiveWorkerCount);
                    try
                    {
                        entry.Work();
                    }
                    catch (Exception ex)
                    {
                        try { mOnException?.Invoke(ex); } catch { /* defensive */ }
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref mActiveWorkerCount) == 0)
                        {
                            // We were the last in-flight worker. If the
                            // queue is also empty NOW, this batch has
                            // drained — fire the event.
                            bool drained;
                            lock (mLock) { drained = mQueue.Count == 0; }
                            if (drained)
                            {
                                try { QueueDrained?.Invoke(); } catch { /* defensive */ }
                            }
                        }
                    }
                }
            }
        }

        bool TryDequeue(out Entry entry)
        {
            lock (mLock)
            {
                if (mQueue.Count == 0)
                {
                    mSignal.Reset();
                    entry = default;
                    return false;
                }

                using (var enumerator = mQueue.GetEnumerator())
                {
                    enumerator.MoveNext();
                    var key = enumerator.Current.Key;
                    entry = enumerator.Current.Value;
                    mQueue.Remove(key);
                    mIndex.Remove(entry.Key);
                }
                return true;
            }
        }
    }
}
