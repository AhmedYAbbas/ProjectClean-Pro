using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Concurrency abstraction that works across Unity 2021–6.3.
    /// Uses Task.Run on all versions for consistent thread affinity guarantees.
    /// On Unity 2023+, Awaitable could be used but Task.Run gives us predictable
    /// behavior: the caller's continuation always resumes on a thread-pool thread,
    /// regardless of what work() does internally.
    /// </summary>
    internal static class PCPThreading
    {
        /// <summary>
        /// Runs work on a background thread.
        /// Uses Task.Run on all Unity versions for consistent behavior.
        /// </summary>
        public static Task RunOnBackground(Func<Task> work, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                await work();
            }, ct);
        }

        /// <summary>
        /// Returns to the main thread. On Unity 2023+ uses Awaitable,
        /// on older versions uses EditorApplication.delayCall via TaskCompletionSource.
        /// </summary>
        public static Task ReturnToMainThread()
        {
#if UNITY_2023_1_OR_NEWER
            return ReturnToMainThreadAwaitable();
#else
            var tcs = new TaskCompletionSource<bool>();
            UnityEditor.EditorApplication.delayCall += () => tcs.TrySetResult(true);
            return tcs.Task;
#endif
        }

#if UNITY_2023_1_OR_NEWER
        private static async Task ReturnToMainThreadAwaitable()
        {
            await UnityEngine.Awaitable.MainThreadAsync();
        }
#endif

        /// <summary>
        /// Processes items in parallel with a concurrency limit.
        /// Uses SemaphoreSlim to throttle. The semaphore is disposed only after
        /// all tasks have settled to avoid ObjectDisposedException.
        /// </summary>
        public static async Task ParallelForEachAsync<T>(
            IReadOnlyList<T> items,
            Func<T, CancellationToken, Task> body,
            int maxConcurrency,
            CancellationToken ct)
        {
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>(items.Count);
            try
            {
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    await semaphore.WaitAsync(ct);
                    tasks.Add(Task.Run(async () =>
                    {
                        try { await body(item, ct); }
                        finally { semaphore.Release(); }
                    }, ct));
                }
                await Task.WhenAll(tasks);
            }
            finally
            {
                // Wait for all tasks to settle before disposing semaphore
                if (tasks.Count > 0)
                {
                    try { await Task.WhenAll(tasks); }
                    catch { /* already observed above or will be observed by caller */ }
                }
                semaphore.Dispose();
            }
        }

        /// <summary>
        /// Default concurrency: half the CPU cores, minimum 1.
        /// Leaves headroom for Unity's own threads and the editor.
        /// </summary>
        public static int DefaultConcurrency => Math.Max(1, Environment.ProcessorCount / 2);
    }
}