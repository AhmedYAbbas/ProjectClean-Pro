using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Async helpers for non-blocking editor scanning.
    /// Uses standard C# async/await with <see cref="EditorApplication.delayCall"/>
    /// to yield back to the editor event loop — no extra packages required.
    /// <para>
    /// In batch mode (<see cref="Application.isBatchMode"/>), yields are no-ops
    /// so scans run synchronously for CI / command-line usage.
    /// </para>
    /// </summary>
    public static class PCPEditorAsync
    {
        /// <summary>
        /// True when running in Unity batch mode (CI, command line).
        /// In batch mode, <see cref="YieldToEditor"/> returns immediately.
        /// </summary>
        public static bool IsBatchMode => Application.isBatchMode;

        // ----------------------------------------------------------------
        // Core yield
        // ----------------------------------------------------------------

        /// <summary>
        /// Yields one editor frame. The editor repaints, processes input,
        /// and runs other callbacks before resuming the caller.
        /// <para>
        /// In batch mode this returns <see cref="Task.CompletedTask"/> so the
        /// scan runs synchronously without blocking on a non-existent event loop.
        /// </para>
        /// </summary>
        public static Task YieldToEditor()
        {
            if (IsBatchMode)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();
            EditorApplication.delayCall += () => tcs.TrySetResult(true);
            return tcs.Task;
        }

        // ----------------------------------------------------------------
        // Convenience helpers for tight loops
        // ----------------------------------------------------------------

        /// <summary>
        /// Call inside a tight asset-processing loop. Every <paramref name="interval"/>
        /// iterations it reports progress, checks for cancellation, and yields
        /// one editor frame so the UI stays responsive.
        /// <para>
        /// For iterations that are not on the interval boundary this is a no-op
        /// (zero allocations, zero overhead).
        /// </para>
        /// </summary>
        /// <param name="index">Current loop index (0-based).</param>
        /// <param name="total">Total number of items to process.</param>
        /// <param name="interval">
        /// How often to yield (e.g. 64 means every 64th iteration). Must be &gt; 0.
        /// </param>
        /// <param name="onProgress">Optional progress callback (normalised 0-1, label).</param>
        /// <param name="label">Progress label shown in the UI.</param>
        /// <param name="ct">Cancellation token checked at each yield point.</param>
        public static async Task YieldIfNeeded(
            int index, int total, int interval,
            Action<float, string> onProgress, string label,
            CancellationToken ct)
        {
            if (interval <= 0 || index % interval != 0)
                return;

            float progress = total > 0 ? index / (float)total : 0f;
            onProgress?.Invoke(progress, label);
            ct.ThrowIfCancellationRequested();
            await YieldToEditor();
        }

        // ----------------------------------------------------------------
        // Synchronous runner
        // ----------------------------------------------------------------

        /// <summary>
        /// Runs an async scan task to completion on the current thread.
        /// In batch mode this is straightforward (yields are no-ops).
        /// In interactive mode the task is kicked off and we pump the
        /// editor update loop until it completes.
        /// </summary>
        public static void RunSync(Func<Task> asyncAction)
        {
            if (asyncAction == null)
                throw new ArgumentNullException(nameof(asyncAction));

            Task task = asyncAction();

            if (task.IsCompleted)
            {
                // Fast path — already done (batch mode or instant skip)
                if (task.IsFaulted && task.Exception != null)
                    throw task.Exception.GetBaseException();
                return;
            }

            // Spin-wait with editor tick pumping.
            // This is only used by PCPAPI.RunScan in interactive mode,
            // which is rare. Prefer the async path whenever possible.
            while (!task.IsCompleted)
            {
                // Let the editor process one tick so our delayCall fires.
                System.Threading.Thread.Sleep(1);
            }

            if (task.IsFaulted && task.Exception != null)
                throw task.Exception.GetBaseException();
        }

        /// <summary>
        /// Runs an async scan task that returns a value to completion.
        /// See <see cref="RunSync(Func{Task})"/> for details.
        /// </summary>
        public static T RunSync<T>(Func<Task<T>> asyncAction)
        {
            if (asyncAction == null)
                throw new ArgumentNullException(nameof(asyncAction));

            Task<T> task = asyncAction();

            if (task.IsCompleted)
            {
                if (task.IsFaulted && task.Exception != null)
                    throw task.Exception.GetBaseException();
                return task.Result;
            }

            while (!task.IsCompleted)
            {
                System.Threading.Thread.Sleep(1);
            }

            if (task.IsFaulted && task.Exception != null)
                throw task.Exception.GetBaseException();

            return task.Result;
        }
    }
}
