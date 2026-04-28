using EmoTracker.Core.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Unit tests for the generic <see cref="PriorityWorkQueue{TKey}"/>.
    /// Single-worker queues are used wherever ordering needs to be
    /// deterministic; multi-worker is exercised separately for the
    /// drain event + concurrency safety.
    /// </summary>
    public class PriorityWorkQueueTests
    {
        // -------- Helpers ------------------------------------------------

        // Wait for the queue's count to drop to zero — used as a coarse
        // "all queued items have been picked up" gate for tests that
        // drive a small fixed number of items. Tighter than Thread.Sleep
        // and self-extending if the test machine is slow.
        static void WaitForQueueDrain(PriorityWorkQueue<int> q, int timeoutMs = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (q.QueueCount == 0) return;
                Thread.Sleep(10);
            }
            throw new TimeoutException($"Queue did not drain within {timeoutMs} ms (still {q.QueueCount} items).");
        }

        // -------- Priority ordering --------------------------------------

        [Fact]
        public void PriorityOrderedExecution()
        {
            // Single-worker queue → deterministic execution order.
            // Enqueue items in non-priority order; verify the worker
            // picks them up in priority order (lowest first), with FIFO
            // breaking ties.
            using var q = new PriorityWorkQueue<int>("test-priority", workerCount: 1);
            var output = new List<int>();
            var done = new ManualResetEventSlim(false);
            int target = 5;

            void Record(int id)
            {
                lock (output) { output.Add(id); if (output.Count == target) done.Set(); }
            }

            // Order: tie-priority FIFO inside same bucket; lower priority bucket first.
            q.Enqueue(1, priority: 200, () => Record(1));   // normal, first
            q.Enqueue(2, priority: 200, () => Record(2));   // normal, second
            q.Enqueue(3, priority: 100, () => Record(3));   // higher, first in bucket
            q.Enqueue(4, priority:   0, () => Record(4));   // immediate
            q.Enqueue(5, priority: 100, () => Record(5));   // higher, second in bucket

            q.Start();
            Assert.True(done.Wait(2000), "Items did not all execute within 2 s.");
            q.Stop();

            // Expected: 4 (priority 0), then 3, 5 (priority 100, FIFO),
            // then 1, 2 (priority 200, FIFO).
            Assert.Equal(new[] { 4, 3, 5, 1, 2 }, output);
        }

        // -------- Same-key dedupe ----------------------------------------

        [Fact]
        public void SameKeyEnqueueDedupesAndBoostsPriority()
        {
            // Stop the queue before running so we can inspect the queue
            // contents without races. Enqueue is thread-safe even when
            // workers aren't running.
            using var q = new PriorityWorkQueue<int>("test-dedupe", workerCount: 1);
            int callCount = 0;
            void Inc() => Interlocked.Increment(ref callCount);

            q.Enqueue(42, priority: 200, Inc);
            Assert.Equal(1, q.QueueCount);
            Assert.True(q.Contains(42));

            // Same key, worse priority — no-op, queue unchanged.
            q.Enqueue(42, priority: 300, Inc);
            Assert.Equal(1, q.QueueCount);

            // Same key, equal priority — no-op (existing entry kept).
            q.Enqueue(42, priority: 200, Inc);
            Assert.Equal(1, q.QueueCount);

            // Same key, BETTER priority — boost; still single entry.
            q.Enqueue(42, priority: 50, Inc);
            Assert.Equal(1, q.QueueCount);

            q.Start();
            WaitForQueueDrain(q);
            // Wait briefly for the worker to actually run the action.
            // The queue dropping to 0 doesn't strictly mean the work
            // delegate has finished — TryDequeue removes BEFORE invoke.
            // Add a small grace period.
            Thread.Sleep(50);
            q.Stop();

            // Despite four Enqueue calls, the work delegate runs once.
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void CancelRemovesQueuedKey()
        {
            using var q = new PriorityWorkQueue<int>("test-cancel", workerCount: 1);
            int callCount = 0;
            q.Enqueue(7, 100, () => Interlocked.Increment(ref callCount));
            Assert.True(q.Contains(7));

            Assert.True(q.Cancel(7));
            Assert.False(q.Contains(7));
            Assert.Equal(0, q.QueueCount);

            // Cancelling absent key returns false.
            Assert.False(q.Cancel(7));

            q.Start();
            Thread.Sleep(50);
            q.Stop();

            Assert.Equal(0, callCount);
        }

        // -------- Drain event --------------------------------------------

        [Fact]
        public void QueueDrainedFiresAfterBatchCompletes()
        {
            using var q = new PriorityWorkQueue<int>("test-drain", workerCount: 2);
            int drainCount = 0;
            q.QueueDrained += () => Interlocked.Increment(ref drainCount);

            int finished = 0;
            int target = 4;
            var done = new ManualResetEventSlim(false);
            for (int i = 0; i < target; ++i)
            {
                int id = i;
                q.Enqueue(id, 100, () =>
                {
                    Thread.Sleep(20);
                    if (Interlocked.Increment(ref finished) == target) done.Set();
                });
            }
            q.Start();
            Assert.True(done.Wait(3000));

            // Drain event fires asynchronously after the LAST worker
            // exits its task. Give it a moment.
            Thread.Sleep(100);
            q.Stop();

            // At least one drain fire — depending on how the workers
            // raced, we might also see one or two extras if items
            // settled into separate batches. The key invariant: at
            // least one fire after a non-empty queue empties.
            Assert.True(drainCount >= 1, $"Expected at least 1 drain event, got {drainCount}.");
        }

        // -------- Exception handling -------------------------------------

        [Fact]
        public void ExceptionsSurfaceToOnException_AndDoNotKillWorker()
        {
            var exceptions = new List<Exception>();
            using var q = new PriorityWorkQueue<int>(
                "test-throw",
                workerCount: 1,
                onException: ex => { lock (exceptions) exceptions.Add(ex); });

            int after = 0;
            var done = new ManualResetEventSlim(false);

            // First task throws; second task should still run.
            q.Enqueue(1, 100, () => throw new InvalidOperationException("boom"));
            q.Enqueue(2, 100, () => { Interlocked.Exchange(ref after, 1); done.Set(); });

            q.Start();
            Assert.True(done.Wait(2000), "Worker did not run the second task — exception likely killed it.");
            q.Stop();

            Assert.Single(exceptions);
            Assert.IsType<InvalidOperationException>(exceptions[0]);
            Assert.Equal(1, after);
        }

        // -------- Stop while items pending -------------------------------

        [Fact]
        public void StopReleasesWorkers()
        {
            using var q = new PriorityWorkQueue<int>("test-stop", workerCount: 2);
            int ran = 0;
            for (int i = 0; i < 3; ++i)
            {
                int id = i;
                q.Enqueue(id, 100, () =>
                {
                    Thread.Sleep(15);
                    Interlocked.Increment(ref ran);
                });
            }
            q.Start();
            // Give workers a chance to start one task each.
            Thread.Sleep(20);
            q.Stop();

            // After Stop, IsRunning is false. Some items may have run
            // (they were already in flight or pulled fast); we only
            // assert lifecycle, not scheduling.
            Assert.False(q.IsRunning);
        }
    }
}
