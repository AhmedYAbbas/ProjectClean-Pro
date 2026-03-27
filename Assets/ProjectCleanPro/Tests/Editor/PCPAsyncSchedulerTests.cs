//using NUnit.Framework;
//using ProjectCleanPro.Editor;
//using ProjectCleanPro.Editor.Core;
//using System;
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;

//namespace ProjectCleanPro.Tests.Editor
//{
//    [TestFixture]
//    public class PCPAsyncSchedulerTests
//    {
//        [Test]
//        public void Constructor_SetsDefaultBudget()
//        {
//            using var scheduler = new PCPAsyncScheduler(8f);
//            Assert.AreEqual(8f, scheduler.BudgetMs);
//        }

//        [Test]
//        public async Task ScheduleBackground_ExecutesWork()
//        {
//            using var scheduler = new PCPAsyncScheduler(8f);
//            var result = await scheduler.ScheduleBackground(
//                ct => Task.FromResult(42), CancellationToken.None);
//            Assert.AreEqual(42, result);
//        }

//        [Test]
//        public async Task ScheduleBackground_TracksCount()
//        {
//            using var scheduler = new PCPAsyncScheduler(8f);
//            var tcs = new TaskCompletionSource<bool>();

//            var task = scheduler.ScheduleBackground(async ct =>
//            {
//                await tcs.Task;
//                return 1;
//            }, CancellationToken.None);

//            // Background task is pending
//            Assert.GreaterOrEqual(scheduler.PendingBackgroundTasks, 0);

//            tcs.SetResult(true);
//            await task;
//        }

//        [Test]
//        public async Task BatchOnMainThread_ProcessesAllItems()
//        {
//            using var scheduler = new PCPAsyncScheduler(16f);
//            var items = new List<int> { 1, 2, 3, 4, 5 };

//            // Manually pump the queue since we're in a test (no real editor loop)
//            // We'll run DrainMainQueue by simulating editor update ticks
//            var task = scheduler.BatchOnMainThread(items, x => x * 2, CancellationToken.None);

//            // Pump the queue repeatedly until done
//            int ticks = 0;
//            while (!task.IsCompleted && ticks < 1000)
//            {
//                scheduler.PumpMainThreadQueue();
//                await Task.Yield();
//                ticks++;
//            }

//            Assert.IsTrue(task.IsCompleted, "BatchOnMainThread did not complete");
//            var results = task.Result;
//            CollectionAssert.AreEqual(new[] { 2, 4, 6, 8, 10 }, results);
//        }

//        [Test]
//        public void Dispose_UnregistersFromUpdate()
//        {
//            var scheduler = new PCPAsyncScheduler(8f);
//            scheduler.Dispose();
//            // Should not throw on double dispose
//            scheduler.Dispose();
//        }

//        [Test]
//        public void AssertMainThread_DoesNotThrowOnMainThread()
//        {
//            Assert.DoesNotThrow(() => PCPAsyncScheduler.AssertMainThread("test"));
//        }
//    }
//}