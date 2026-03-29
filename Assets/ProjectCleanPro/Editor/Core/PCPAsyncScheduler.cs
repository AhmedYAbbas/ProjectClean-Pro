using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Central work coordinator for async scans. Replaces PCPEditorAsync.
    /// Manages a frame-budgeted main-thread queue and background task dispatch.
    /// Created per-scan, disposed when scan completes or is cancelled.
    /// </summary>
    public sealed class PCPAsyncScheduler : IDisposable
    {
        private sealed class MainThreadWorkItem
        {
            private readonly Action m_Action;
            public MainThreadWorkItem(Action action) => m_Action = action;
            public void Execute() => m_Action();
        }

        private readonly ConcurrentQueue<MainThreadWorkItem> m_MainQueue = new();
        private readonly Stopwatch m_FrameStopwatch = new();
        private float m_BudgetMs;
        private bool m_Registered;
        private int m_PendingBackgroundTasks;
        private int m_CompletedItems;

        public PCPAsyncScheduler(float budgetMs)
        {
            m_BudgetMs = budgetMs;
            EditorApplication.update += DrainMainQueue;
            m_Registered = true;
        }

        /// <summary>
        /// Queues work to run on a background thread.
        /// Exceptions are logged via main thread (Debug.LogException is main-thread-only).
        /// OperationCanceledException is re-thrown, not logged.
        /// </summary>
        public Task<TResult> ScheduleBackground<TResult>(
            Func<CancellationToken, Task<TResult>> work,
            CancellationToken ct)
        {
            Interlocked.Increment(ref m_PendingBackgroundTasks);
            return Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    return await work(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _ = ScheduleMainThread(() =>
                    {
                        Debug.LogException(ex);
                        return 0;
                    }, CancellationToken.None);
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref m_PendingBackgroundTasks);
                }
            }, ct);
        }

        /// <summary>
        /// Convenience overload for background work that returns no value.
        /// </summary>
        public Task ScheduleBackground(
            Func<CancellationToken, Task> work,
            CancellationToken ct)
        {
            return ScheduleBackground(async ct2 =>
            {
                await work(ct2);
                return 0;
            }, ct);
        }

        /// <summary>
        /// Enqueues a small unit of work to run on the main thread within frame budget.
        /// Returns a Task that completes when DrainMainQueue processes the item.
        /// </summary>
        public Task<TResult> ScheduleMainThread<TResult>(
            Func<TResult> work,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<TResult>();
            m_MainQueue.Enqueue(new MainThreadWorkItem(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    return;
                }
                try { tcs.TrySetResult(work()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }));
            return tcs.Task;
        }

        /// <summary>
        /// Processes a list on main thread, frame-budgeted.
        /// All items are enqueued upfront so DrainMainQueue can process multiple
        /// items per frame within the budget. Results are returned in input order.
        /// </summary>
        public async Task<List<TResult>> BatchOnMainThread<TItem, TResult>(
            IReadOnlyList<TItem> items,
            Func<TItem, TResult> work,
            CancellationToken ct)
        {
            var tasks = new List<Task<TResult>>(items.Count);
            foreach (var item in items)
                tasks.Add(ScheduleMainThread(() => work(item), ct));

            await Task.WhenAll(tasks);

            var results = new List<TResult>(items.Count);
            foreach (var task in tasks)
                results.Add(task.Result);
            return results;
        }

        /// <summary>
        /// Called every editor frame by EditorApplication.update.
        /// Processes queued main-thread work items within the frame budget.
        /// </summary>
        private void DrainMainQueue()
        {
            m_FrameStopwatch.Restart();
            while (m_FrameStopwatch.Elapsed.TotalMilliseconds < m_BudgetMs
                   && m_MainQueue.TryDequeue(out var item))
            {
                item.Execute();
                Interlocked.Increment(ref m_CompletedItems);
            }
        }

        /// <summary>
        /// Manual queue pump for testing. In production, DrainMainQueue is called
        /// automatically via EditorApplication.update.
        /// </summary>
        internal void PumpMainThreadQueue()
        {
            DrainMainQueue();
        }

        /// <summary>
        /// Debug-only assertion. Throws if called from a background thread.
        /// Use this to guard AssetDatabase calls during development.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void AssertMainThread(string operation)
        {
#if UNITY_EDITOR
            if (!UnityEditorInternal.InternalEditorUtility.CurrentThreadIsMainThread())
                throw new InvalidOperationException(
                    $"[PCP] {operation} must run on the main thread. " +
                    "Use scheduler.ScheduleMainThread() instead.");
#endif
        }

        public float BudgetMs
        {
            get => m_BudgetMs;
            set => m_BudgetMs = value;
        }

        public int PendingBackgroundTasks => m_PendingBackgroundTasks;
        public int CompletedItems => m_CompletedItems;

        public void Dispose()
        {
            if (m_Registered)
            {
                EditorApplication.update -= DrainMainQueue;
                m_Registered = false;
            }
        }
    }
}