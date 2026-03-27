using NUnit.Framework;
using ProjectCleanPro.Editor.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Tests.Editor
{
    [TestFixture]
    public class PCPThreadingTests
    {
        [Test]
        public async Task RunOnBackground_ExecutesWork()
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            int bgThreadId = 0;

            await PCPThreading.RunOnBackground(() =>
            {
                bgThreadId = Thread.CurrentThread.ManagedThreadId;
                return Task.CompletedTask;
            }, CancellationToken.None);

            Assert.AreNotEqual(0, bgThreadId);
        }

        [Test]
        public void RunOnBackground_CancellationThrows()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await PCPThreading.RunOnBackground(() => Task.CompletedTask, cts.Token);
            });
        }

        [Test]
        public async Task ParallelForEachAsync_ProcessesAllItems()
        {
            var items = new List<int> { 1, 2, 3, 4, 5 };
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();

            await PCPThreading.ParallelForEachAsync(items, (item, ct) =>
            {
                results.Add(item * 2);
                return Task.CompletedTask;
            }, maxConcurrency: 2, CancellationToken.None);

            Assert.AreEqual(5, results.Count);
            CollectionAssert.AreEquivalent(new[] { 2, 4, 6, 8, 10 }, results);
        }

        [Test]
        public async Task ParallelForEachAsync_RespectsMaxConcurrency()
        {
            int concurrent = 0;
            int maxConcurrent = 0;
            var items = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

            await PCPThreading.ParallelForEachAsync(items, async (item, ct) =>
            {
                var current = Interlocked.Increment(ref concurrent);
                // Track max concurrent — atomic read of peak
                int peak;
                do { peak = maxConcurrent; }
                while (current > peak && Interlocked.CompareExchange(ref maxConcurrent, current, peak) != peak);

                await Task.Delay(50, ct);
                Interlocked.Decrement(ref concurrent);
            }, maxConcurrency: 2, CancellationToken.None);

            Assert.LessOrEqual(maxConcurrent, 2,
                $"Max concurrency should be 2, was {maxConcurrent}");
        }

        [Test]
        public void ParallelForEachAsync_CancellationStopsProcessing()
        {
            var cts = new CancellationTokenSource();
            var items = new List<int> { 1, 2, 3, 4, 5 };
            int processed = 0;

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await PCPThreading.ParallelForEachAsync(items, async (item, ct) =>
                {
                    if (Interlocked.Increment(ref processed) >= 2)
                        cts.Cancel();
                    await Task.Delay(100, ct);
                }, maxConcurrency: 1, cts.Token);
            });
        }

        [Test]
        public void DefaultConcurrency_IsAtLeastOne()
        {
            Assert.GreaterOrEqual(PCPThreading.DefaultConcurrency, 1);
        }
    }
}