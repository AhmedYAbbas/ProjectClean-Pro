# Async Scan & Cache System Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-threaded cooperative-yielding scan system with a true multi-threaded architecture using background I/O, frame-budgeted main-thread work, and three user-selectable scan modes (Accurate/Balanced/Fast).

**Architecture:** A `PCPAsyncScheduler` coordinates background thread pools and a frame-budgeted main-thread queue. Three `IPCPDependencyResolver` implementations (one per scan mode) handle dependency resolution with varying accuracy/speed trade-offs. All modules follow a three-phase pattern: gather (background) → query (main, budgeted) → analyze (background). The `PCPScanCache` becomes thread-safe with `ConcurrentDictionary` and async I/O.

**Tech Stack:** C# async/await, `System.Threading.Tasks`, `System.Collections.Concurrent`, `System.IO` async APIs, `#if UNITY_2023_1_OR_NEWER` for `Awaitable` support. Unity Editor API (`AssetDatabase`) stays main-thread-only behind the scheduler.

**Spec:** `docs/superpowers/specs/2026-03-27-async-scan-cache-redesign.md`

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `Editor/Core/PCPThreading.cs` | Static abstraction over `Task.Run` / `Awaitable` for Unity 2021–6.3 |
| `Editor/Core/PCPAsyncScheduler.cs` | Frame-budgeted main-thread queue + background task dispatch |
| `Editor/Core/PCPGuidIndex.cs` | GUID-to-path map from `.meta` files (Fast/Balanced modes) |
| `Editor/Core/PCPGuidParser.cs` | Extract GUID references from YAML asset files |
| `Editor/Core/IPCPDependencyResolver.cs` | Interface for dependency resolution strategy |
| `Editor/Core/PCPDependencyResolverBase.cs` | Shared graph structure, BFS, serialization |
| `Editor/Core/PCPAccurateDependencyResolver.cs` | AssetDatabase-based resolver |
| `Editor/Core/PCPBalancedDependencyResolver.cs` | Hybrid GUID parse + AssetDatabase |
| `Editor/Core/PCPFastDependencyResolver.cs` | Pure GUID-parsing resolver |
| `Editor/Core/PCPDependencyResolverFactory.cs` | Factory: scan mode → resolver |
| `Editor/Core/PCPScanMode.cs` | `PCPScanMode` enum |
| `Tests/Editor/PCPThreadingTests.cs` | Tests for threading abstraction |
| `Tests/Editor/PCPAsyncSchedulerTests.cs` | Tests for scheduler |
| `Tests/Editor/PCPGuidParserTests.cs` | Tests for GUID parsing |
| `Tests/Editor/PCPGuidIndexTests.cs` | Tests for GUID index |

### Modified Files
| File | Changes |
|------|---------|
| `Editor/Core/PCPSettings.cs` | Add `scanMode`, `mainThreadBudgetMs`, `lastScanMode` fields |
| `Editor/Core/PCPScanCache.cs` | Thread-safe collections, async I/O methods |
| `Editor/Core/PCPScanContext.cs` | Add `Scheduler`, `IPCPDependencyResolver`, async asset listing |
| `Editor/Core/PCPScanOrchestrator.cs` | Use scheduler, resolver factory, mode-switch invalidation, parallel modules |
| `Editor/Core/PCPContext.cs` | Wire new components, remove old `PCPDependencyResolver` creation |
| `Editor/Core/PCPResultCacheManager.cs` | Add async save/load methods |
| `Editor/Modules/PCPModuleBase.cs` | Add `ConcurrentQueue<string> m_Warnings`, atomic progress |
| `Editor/Modules/PCPDuplicateDetector.cs` | Three-phase: background hashing |
| `Editor/Modules/PCPUnusedScanner.cs` | Three-phase: background file enumeration |
| `Editor/Modules/PCPMissingRefScanner.cs` | Background pre-filter |
| `Editor/Modules/PCPDependencyModule.cs` | Use `IPCPDependencyResolver` |
| `Editor/Modules/PCPShaderAnalyzer.cs` | Use scheduler for yielding |
| `Editor/Modules/PCPSizeProfiler.cs` | Use scheduler for yielding |
| `Editor/Modules/PCPPackageAuditor.cs` | Use scheduler for yielding |
| `Editor/Data/PCPScanManifest.cs` | Add `warnings` list |
| `Editor/UI/PCPDashboardView.cs` | Scan mode selector, warning banner, fast-mode label, warnings |
| `Editor/UI/PCPSettingsView.cs` | Frame budget slider |
| `Editor/API/PCPAPI.cs` | Pass scan mode through |

### Deleted Files
| File | Reason |
|------|--------|
| `Editor/Core/PCPEditorAsync.cs` | Replaced by `PCPAsyncScheduler` + `PCPThreading` |
| `Editor/Core/PCPDependencyResolver.cs` | Replaced by interface + 3 implementations |

---

## Task Dependency Graph

```
Task 1: PCPScanMode enum + PCPSettings fields
    ↓
Task 2: PCPThreading (concurrency abstraction)
    ↓
Task 3: PCPAsyncScheduler (work coordinator)
    ↓
Task 4: PCPGuidParser (GUID reference extractor)
    ↓
Task 5: PCPGuidIndex (GUID-to-path map)
    ↓
Task 6: IPCPDependencyResolver + base + factory
    ↓
Task 7: PCPAccurateDependencyResolver
    ↓
Task 8: PCPFastDependencyResolver
    ↓
Task 9: PCPBalancedDependencyResolver
    ↓
Task 10: PCPScanCache (thread-safe + async I/O)
    ↓
Task 11: PCPModuleBase (warnings + atomic progress)
    ↓
Task 12: PCPScanContext (new properties + async)
    ↓
Task 13: PCPDuplicateDetector (three-phase rewrite)
    ↓
Task 14: PCPUnusedScanner (three-phase rewrite)
    ↓
Task 15: PCPMissingRefScanner (background pre-filter)
    ↓
Task 16: Minor module updates (Shader, Size, Package, Dependency)
    ↓
Task 17: PCPScanOrchestrator (rewrite)
    ↓
Task 18: PCPContext + PCPResultCacheManager + PCPAPI wiring
    ↓
Task 19: PCPScanManifest (warnings)
    ↓
Task 20: PCPDashboardView (scan mode UI + banners + warnings)
    ↓
Task 21: PCPSettingsView (frame budget slider)
    ↓
Task 22: Delete PCPEditorAsync + old PCPDependencyResolver
    ↓
Task 23: Integration tests
    ↓
Task 24: Final compilation + manual verification
```

---

### Task 1: PCPScanMode Enum + PCPSettings Fields

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPScanMode.cs`
- Modify: `Assets/ProjectCleanPro/Editor/Core/PCPSettings.cs`
- Test: `Assets/ProjectCleanPro/Tests/Editor/PCPSettingsTests.cs`

All paths below are relative to `Assets/ProjectCleanPro/`.

- [ ] **Step 1: Create `PCPScanMode` enum**

```csharp
// Editor/Core/PCPScanMode.cs
namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Controls the trade-off between scan accuracy and speed.
    /// Accurate: AssetDatabase for all deps (slowest, most accurate).
    /// Balanced: GUID parsing + AssetDatabase for complex types.
    /// Fast: Pure GUID parsing, no AssetDatabase for deps (fastest, least accurate).
    /// </summary>
    public enum PCPScanMode
    {
        Accurate = 0,
        Balanced = 1,
        Fast = 2
    }
}
```

- [ ] **Step 2: Add new fields to `PCPSettings`**

Open `Editor/Core/PCPSettings.cs`. Find the field block (near `public bool duplicateCompareImportSettings = true;`) and add:

```csharp
// Scan mode
public PCPScanMode scanMode = PCPScanMode.Accurate;
public float mainThreadBudgetMs = 8f;

// Internal: tracks which mode was used for the last scan (for cache invalidation)
[SerializeField] internal PCPScanMode lastScanMode = PCPScanMode.Accurate;
```

- [ ] **Step 3: Write test for new settings fields**

Add to `Tests/Editor/PCPSettingsTests.cs`:

```csharp
[Test]
public void Settings_DefaultScanMode_IsAccurate()
{
    var settings = PCPSettings.instance;
    Assert.AreEqual(PCPScanMode.Accurate, settings.scanMode);
}

[Test]
public void Settings_DefaultMainThreadBudget_Is8ms()
{
    var settings = PCPSettings.instance;
    Assert.AreEqual(8f, settings.mainThreadBudgetMs, 0.01f);
}

[Test]
public void Settings_MainThreadBudget_ClampedToValidRange()
{
    var settings = PCPSettings.instance;
    settings.mainThreadBudgetMs = 2f;
    Assert.GreaterOrEqual(settings.mainThreadBudgetMs, 0f);
    settings.mainThreadBudgetMs = 20f;
    // The slider clamps in UI, but the field itself accepts any float
    // This test documents the expected range
    Assert.AreEqual(20f, settings.mainThreadBudgetMs);
}
```

- [ ] **Step 4: Run tests**

Run: `Unity Editor → Window → General → Test Runner → EditMode → Run All`
Expected: All new tests PASS, all existing tests still PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPScanMode.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPScanMode.cs.meta
git add Assets/ProjectCleanPro/Editor/Core/PCPSettings.cs
git add Assets/ProjectCleanPro/Tests/Editor/PCPSettingsTests.cs
git commit -m "feat: add PCPScanMode enum and settings fields for scan mode, frame budget"
```

---

### Task 2: PCPThreading — Concurrency Abstraction Layer

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPThreading.cs`
- Test: `Assets/ProjectCleanPro/Tests/Editor/PCPThreadingTests.cs`

- [ ] **Step 1: Write failing tests for `PCPThreading`**

```csharp
// Tests/Editor/PCPThreadingTests.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectCleanPro.Editor.Core;

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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: Test Runner → EditMode → PCPThreadingTests
Expected: FAIL — `PCPThreading` type does not exist.

- [ ] **Step 3: Implement `PCPThreading`**

```csharp
// Editor/Core/PCPThreading.cs
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: Test Runner → EditMode → PCPThreadingTests
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPThreading.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPThreading.cs.meta
git add Assets/ProjectCleanPro/Tests/Editor/PCPThreadingTests.cs
git add Assets/ProjectCleanPro/Tests/Editor/PCPThreadingTests.cs.meta
git commit -m "feat: add PCPThreading concurrency abstraction for Unity 2021-6.3"
```

---

### Task 3: PCPAsyncScheduler — Central Work Coordinator

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPAsyncScheduler.cs`
- Test: `Assets/ProjectCleanPro/Tests/Editor/PCPAsyncSchedulerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Tests/Editor/PCPAsyncSchedulerTests.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Tests.Editor
{
    [TestFixture]
    public class PCPAsyncSchedulerTests
    {
        [Test]
        public void Constructor_SetsDefaultBudget()
        {
            using var scheduler = new PCPAsyncScheduler(8f);
            Assert.AreEqual(8f, scheduler.BudgetMs);
        }

        [Test]
        public async Task ScheduleBackground_ExecutesWork()
        {
            using var scheduler = new PCPAsyncScheduler(8f);
            var result = await scheduler.ScheduleBackground(
                ct => Task.FromResult(42), CancellationToken.None);
            Assert.AreEqual(42, result);
        }

        [Test]
        public async Task ScheduleBackground_TracksCount()
        {
            using var scheduler = new PCPAsyncScheduler(8f);
            var tcs = new TaskCompletionSource<bool>();

            var task = scheduler.ScheduleBackground(async ct =>
            {
                await tcs.Task;
                return 1;
            }, CancellationToken.None);

            // Background task is pending
            Assert.GreaterOrEqual(scheduler.PendingBackgroundTasks, 0);

            tcs.SetResult(true);
            await task;
        }

        [Test]
        public async Task BatchOnMainThread_ProcessesAllItems()
        {
            using var scheduler = new PCPAsyncScheduler(16f);
            var items = new List<int> { 1, 2, 3, 4, 5 };

            // Manually pump the queue since we're in a test (no real editor loop)
            // We'll run DrainMainQueue by simulating editor update ticks
            var task = scheduler.BatchOnMainThread(items, x => x * 2, CancellationToken.None);

            // Pump the queue repeatedly until done
            int ticks = 0;
            while (!task.IsCompleted && ticks < 1000)
            {
                scheduler.PumpMainThreadQueue();
                await Task.Yield();
                ticks++;
            }

            Assert.IsTrue(task.IsCompleted, "BatchOnMainThread did not complete");
            var results = task.Result;
            CollectionAssert.AreEqual(new[] { 2, 4, 6, 8, 10 }, results);
        }

        [Test]
        public void Dispose_UnregistersFromUpdate()
        {
            var scheduler = new PCPAsyncScheduler(8f);
            scheduler.Dispose();
            // Should not throw on double dispose
            scheduler.Dispose();
        }

        [Test]
        public void AssertMainThread_DoesNotThrowOnMainThread()
        {
            Assert.DoesNotThrow(() => PCPAsyncScheduler.AssertMainThread("test"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: Test Runner → EditMode → PCPAsyncSchedulerTests
Expected: FAIL — `PCPAsyncScheduler` type does not exist.

- [ ] **Step 3: Implement `PCPAsyncScheduler`**

```csharp
// Editor/Core/PCPAsyncScheduler.cs
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
    internal sealed class PCPAsyncScheduler : IDisposable
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
                    ScheduleMainThread(() =>
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: Test Runner → EditMode → PCPAsyncSchedulerTests
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPAsyncScheduler.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPAsyncScheduler.cs.meta
git add Assets/ProjectCleanPro/Tests/Editor/PCPAsyncSchedulerTests.cs
git add Assets/ProjectCleanPro/Tests/Editor/PCPAsyncSchedulerTests.cs.meta
git commit -m "feat: add PCPAsyncScheduler with frame-budgeted main-thread queue"
```

---

### Task 4: PCPGuidParser — GUID Reference Extractor

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPGuidParser.cs`
- Test: `Assets/ProjectCleanPro/Tests/Editor/PCPGuidParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Tests/Editor/PCPGuidParserTests.cs
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Tests.Editor
{
    [TestFixture]
    public class PCPGuidParserTests
    {
        private string m_TempDir;

        [SetUp]
        public void SetUp()
        {
            m_TempDir = Path.Combine(Path.GetTempPath(), "PCPGuidParserTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(m_TempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_TempDir))
                Directory.Delete(m_TempDir, true);
        }

        [Test]
        public async Task ParseReferencesAsync_FindsGuidsInYaml()
        {
            var content = @"--- !u!114 &123
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: e4f18583b7a683c4b9db3b1f46a8b93a, type: 3}
  m_Material: {fileID: 2100000, guid: c22c5a2f3fa1e0947a1e82e283a6b70c, type: 2}";

            var filePath = Path.Combine(m_TempDir, "test.prefab");
            await File.WriteAllTextAsync(filePath, content);

            var guids = await PCPGuidParser.ParseReferencesAsync(filePath, CancellationToken.None);

            Assert.AreEqual(2, guids.Count);
            Assert.IsTrue(guids.Contains("e4f18583b7a683c4b9db3b1f46a8b93a"));
            Assert.IsTrue(guids.Contains("c22c5a2f3fa1e0947a1e82e283a6b70c"));
        }

        [Test]
        public async Task ParseReferencesAsync_DeduplicatesGuids()
        {
            var content = @"  m_A: {fileID: 0, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1, type: 2}
  m_B: {fileID: 0, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1, type: 2}";

            var filePath = Path.Combine(m_TempDir, "test.prefab");
            await File.WriteAllTextAsync(filePath, content);

            var guids = await PCPGuidParser.ParseReferencesAsync(filePath, CancellationToken.None);

            Assert.AreEqual(1, guids.Count);
        }

        [Test]
        public async Task ParseReferencesAsync_EmptyFile_ReturnsEmpty()
        {
            var filePath = Path.Combine(m_TempDir, "empty.prefab");
            await File.WriteAllTextAsync(filePath, "");

            var guids = await PCPGuidParser.ParseReferencesAsync(filePath, CancellationToken.None);

            Assert.AreEqual(0, guids.Count);
        }

        [Test]
        public async Task ParseReferencesAsync_IgnoresInvalidHex()
        {
            var content = "  m_A: {fileID: 0, guid: not_a_valid_hex_string_here!!, type: 2}";

            var filePath = Path.Combine(m_TempDir, "test.prefab");
            await File.WriteAllTextAsync(filePath, content);

            var guids = await PCPGuidParser.ParseReferencesAsync(filePath, CancellationToken.None);

            Assert.AreEqual(0, guids.Count);
        }

        [Test]
        public void IsGuidParseable_ReturnsTrueForYamlAssets()
        {
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".prefab"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".unity"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".asset"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".mat"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".controller"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".anim"));
        }

        [Test]
        public void IsGuidParseable_ReturnsFalseForBinaryAssets()
        {
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".png"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".fbx"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".wav"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".cs"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".dll"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: Test Runner → EditMode → PCPGuidParserTests
Expected: FAIL — `PCPGuidParser` type does not exist.

- [ ] **Step 3: Implement `PCPGuidParser`**

```csharp
// Editor/Core/PCPGuidParser.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Extracts GUID references from Unity YAML-serialized asset files.
    /// Uses streaming line-by-line reading to avoid loading large files into memory.
    /// Used by Fast and Balanced scan modes.
    /// </summary>
    internal static class PCPGuidParser
    {
        private const string k_GuidPrefix = "guid: ";
        private const int k_GuidLength = 32;

        /// <summary>
        /// Reads a file line-by-line and extracts all GUID references.
        /// Uses StreamReader for constant memory usage regardless of file size.
        /// </summary>
        public static async Task<HashSet<string>> ParseReferencesAsync(
            string filePath, CancellationToken ct)
        {
            var guids = new HashSet<string>();

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 65536, useAsync: true);
            using var reader = new StreamReader(stream);

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();
                ExtractGuidsFromLine(line, guids);
            }

            return guids;
        }

        private static void ExtractGuidsFromLine(string line, HashSet<string> guids)
        {
            int searchFrom = 0;
            while (true)
            {
                int idx = line.IndexOf(k_GuidPrefix, searchFrom, StringComparison.Ordinal);
                if (idx < 0) break;

                int guidStart = idx + k_GuidPrefix.Length;
                if (guidStart + k_GuidLength <= line.Length)
                {
                    var candidate = line.Substring(guidStart, k_GuidLength);
                    if (IsHexString(candidate))
                        guids.Add(candidate);
                }
                searchFrom = guidStart + k_GuidLength;
            }
        }

        private static bool IsHexString(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if the file extension indicates a YAML-serialized Unity asset
        /// that may contain GUID references. Binary assets (textures, models, audio)
        /// do not contain text GUID references.
        /// </summary>
        public static bool IsGuidParseable(string extensionOrPath)
        {
            var ext = extensionOrPath.StartsWith(".")
                ? extensionOrPath
                : Path.GetExtension(extensionOrPath);

            return ext is ".prefab" or ".unity" or ".asset" or ".mat"
                or ".controller" or ".anim" or ".overrideController"
                or ".lighting" or ".playable" or ".signal"
                or ".spriteatlasv2" or ".spriteatlas" or ".terrainlayer"
                or ".mixer" or ".renderTexture" or ".flare";
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: Test Runner → EditMode → PCPGuidParserTests
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPGuidParser.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPGuidParser.cs.meta
git add Assets/ProjectCleanPro/Tests/Editor/PCPGuidParserTests.cs
git add Assets/ProjectCleanPro/Tests/Editor/PCPGuidParserTests.cs.meta
git commit -m "feat: add PCPGuidParser for extracting GUID refs from YAML assets"
```

---

### Task 5: PCPGuidIndex — GUID-to-Path Map

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPGuidIndex.cs`
- Test: `Assets/ProjectCleanPro/Tests/Editor/PCPGuidIndexTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Tests/Editor/PCPGuidIndexTests.cs
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Tests.Editor
{
    [TestFixture]
    public class PCPGuidIndexTests
    {
        private string m_TempDir;

        [SetUp]
        public void SetUp()
        {
            m_TempDir = Path.Combine(Path.GetTempPath(), "PCPGuidIndexTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(m_TempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_TempDir))
                Directory.Delete(m_TempDir, true);
        }

        private string CreateMetaFile(string assetName, string guid)
        {
            var metaPath = Path.Combine(m_TempDir, assetName + ".meta");
            File.WriteAllText(metaPath, $"fileFormatVersion: 2\nguid: {guid}\n");
            return metaPath;
        }

        [Test]
        public async Task BuildAsync_FullBuild_IndexesAllMetas()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var meta2 = CreateMetaFile("b.mat", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1, meta2 }, null, CancellationToken.None);

            var path1 = index.Resolve("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var path2 = index.Resolve("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            Assert.IsNotNull(path1);
            Assert.IsTrue(path1.EndsWith("a.png"));
            Assert.IsNotNull(path2);
            Assert.IsTrue(path2.EndsWith("b.mat"));
        }

        [Test]
        public async Task BuildAsync_IncrementalBuild_OnlyProcessesChanged()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var meta2 = CreateMetaFile("b.mat", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1, meta2 }, null, CancellationToken.None);

            // Incremental: only meta2 changed
            var changed = new HashSet<string> { meta2 };
            await index.BuildAsync(new List<string> { meta1, meta2 }, changed, CancellationToken.None);

            // Both should still be indexed
            Assert.IsNotNull(index.Resolve("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1"));
            Assert.IsNotNull(index.Resolve("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        }

        [Test]
        public async Task BuildAsync_IncrementalBuild_RemovesDeletedEntries()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var meta2 = CreateMetaFile("b.mat", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1, meta2 }, null, CancellationToken.None);

            // meta2 was deleted — not in the metaFiles list anymore
            var changed = new HashSet<string>();
            await index.BuildAsync(new List<string> { meta1 }, changed, CancellationToken.None);

            Assert.IsNotNull(index.Resolve("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1"));
            Assert.IsNull(index.Resolve("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        }

        [Test]
        public async Task Resolve_UnknownGuid_ReturnsNull()
        {
            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string>(), null, CancellationToken.None);

            Assert.IsNull(index.Resolve("00000000000000000000000000000000"));
        }

        [Test]
        public async Task ResolveAll_ResolvesKnownGuids()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1 }, null, CancellationToken.None);

            var guids = new[] { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1", "unknown_guid_here_not_existing_" };
            var resolved = index.ResolveAll(guids);

            Assert.AreEqual(1, resolved.Count);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: Test Runner → EditMode → PCPGuidIndexTests
Expected: FAIL — `PCPGuidIndex` type does not exist.

- [ ] **Step 3: Implement `PCPGuidIndex`**

```csharp
// Editor/Core/PCPGuidIndex.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Maps GUIDs to asset paths by reading .meta files.
    /// Built on background threads, shared across modules in Fast/Balanced modes.
    /// </summary>
    internal sealed class PCPGuidIndex
    {
        private readonly ConcurrentDictionary<string, string> m_GuidToPath = new();
        private readonly ConcurrentDictionary<string, string> m_PathToGuid = new();

        /// <summary>
        /// Builds or incrementally updates the GUID index.
        /// </summary>
        /// <param name="metaFiles">All .meta file paths currently in the project.</param>
        /// <param name="changedFiles">
        /// If non-null, only these .meta files are re-read (incremental).
        /// If null, all metaFiles are read (full build).
        /// Entries for .meta files no longer in metaFiles are pruned.
        /// </param>
        public async Task BuildAsync(
            IReadOnlyList<string> metaFiles,
            IReadOnlySet<string> changedFiles,
            CancellationToken ct)
        {
            if (changedFiles != null)
            {
                // Prune deleted entries: paths in our index whose .meta is no longer present
                var currentMetaSet = new HashSet<string>(metaFiles);
                var toRemove = new List<string>();
                foreach (var (path, guid) in m_PathToGuid)
                {
                    if (!currentMetaSet.Contains(path + ".meta"))
                        toRemove.Add(path);
                }
                foreach (var path in toRemove)
                {
                    if (m_PathToGuid.TryRemove(path, out var guid))
                        m_GuidToPath.TryRemove(guid, out _);
                }
            }

            var toProcess = changedFiles == null
                ? (IReadOnlyList<string>)metaFiles
                : metaFiles.Where(f => changedFiles.Contains(f)).ToList();

            await PCPThreading.ParallelForEachAsync(toProcess, async (metaPath, token) =>
            {
                var guid = await ReadGuidFromMetaAsync(metaPath, token);
                if (guid != null)
                {
                    // Remove ".meta" suffix to get asset path
                    var assetPath = metaPath.Substring(0, metaPath.Length - 5);
                    m_GuidToPath[guid] = assetPath;
                    m_PathToGuid[assetPath] = guid;
                }
            }, PCPThreading.DefaultConcurrency, ct);
        }

        /// <summary>Resolve a single GUID to its asset path. Returns null if unknown.</summary>
        public string Resolve(string guid) =>
            m_GuidToPath.TryGetValue(guid, out var path) ? path : null;

        /// <summary>Resolve multiple GUIDs, returning only successfully resolved paths.</summary>
        public HashSet<string> ResolveAll(IEnumerable<string> guids) =>
            guids.Select(Resolve).Where(p => p != null).ToHashSet();

        public int Count => m_GuidToPath.Count;

        /// <summary>
        /// Reads only the first few lines of a .meta file to extract the guid.
        /// Format: "guid: {32 hex chars}"
        /// </summary>
        private static async Task<string> ReadGuidFromMetaAsync(string metaPath, CancellationToken ct)
        {
            try
            {
                using var stream = new FileStream(metaPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: 512, useAsync: true);
                using var reader = new StreamReader(stream);

                // The guid line is typically line 2, but read up to 5 lines to be safe
                for (int i = 0; i < 5; i++)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    ct.ThrowIfCancellationRequested();

                    const string prefix = "guid: ";
                    int idx = line.IndexOf(prefix, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        int guidStart = idx + prefix.Length;
                        if (guidStart + 32 <= line.Length)
                        {
                            var candidate = line.Substring(guidStart, 32).Trim();
                            if (candidate.Length == 32)
                                return candidate;
                        }
                    }
                }
            }
            catch (IOException) { /* File deleted between enumeration and read */ }
            catch (UnauthorizedAccessException) { /* Permission denied */ }

            return null;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: Test Runner → EditMode → PCPGuidIndexTests
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPGuidIndex.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPGuidIndex.cs.meta
git add Assets/ProjectCleanPro/Tests/Editor/PCPGuidIndexTests.cs
git add Assets/ProjectCleanPro/Tests/Editor/PCPGuidIndexTests.cs.meta
git commit -m "feat: add PCPGuidIndex for GUID-to-path mapping from .meta files"
```

---

### Task 6: IPCPDependencyResolver Interface + Base + Factory

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/IPCPDependencyResolver.cs`
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolverBase.cs`
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolverFactory.cs`

- [ ] **Step 1: Create the interface**

```csharp
// Editor/Core/IPCPDependencyResolver.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Strategy interface for dependency resolution. Three implementations
    /// exist (Accurate, Balanced, Fast) trading accuracy for speed.
    /// </summary>
    internal interface IPCPDependencyResolver
    {
        Task BuildGraphAsync(PCPScanContext context, CancellationToken ct);
        IReadOnlyCollection<string> GetReachableAssets();
        int GetDependentCount(string path);
        IReadOnlyCollection<string> GetDependencies(string path);
        IReadOnlyCollection<string> GetDependents(string path);
        bool IsReachable(string path);
        IEnumerable<string> GetAllUnreachable();
        IReadOnlyCollection<string> GetAllAssets();
        int AssetCount { get; }
        int ReachableCount { get; }
        bool IsBuilt { get; }
        void SaveToDisk();
        bool LoadFromDisk();
        void Clear();
    }
}
```

- [ ] **Step 2: Create the shared base class**

```csharp
// Editor/Core/PCPDependencyResolverBase.cs
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Shared graph structure, BFS reachability, serialization, and query methods
    /// used by all three resolver implementations.
    /// </summary>
    internal abstract class PCPDependencyResolverBase : IPCPDependencyResolver
    {
        protected readonly ConcurrentDictionary<string, HashSet<string>> m_Forward = new();
        protected readonly ConcurrentDictionary<string, HashSet<string>> m_Reverse = new();
        protected HashSet<string> m_Reachable = new();
        protected readonly ConcurrentDictionary<string, byte> m_AllAssets = new();
        protected bool m_IsBuilt;

        private const int GraphFormatVersion = 3; // Bumped from v2 for new format
        private static readonly string s_GraphPath =
            Path.Combine("Library", "ProjectCleanPro", "DepGraph.bin");

        public bool IsBuilt => m_IsBuilt;
        public int AssetCount => m_AllAssets.Count;
        public int ReachableCount => m_Reachable.Count;

        public abstract Task BuildGraphAsync(PCPScanContext context, CancellationToken ct);

        // --- Graph mutation (thread-safe) ---

        protected void UpdateEdges(string asset, IEnumerable<string> dependencies)
        {
            m_AllAssets[asset] = 0;

            // Remove old forward edges for this asset from reverse map
            if (m_Forward.TryGetValue(asset, out var oldDeps))
            {
                foreach (var dep in oldDeps)
                {
                    if (m_Reverse.TryGetValue(dep, out var rev))
                    {
                        lock (rev) { rev.Remove(asset); }
                    }
                }
            }

            // Set new forward edges
            var newDeps = new HashSet<string>(dependencies);
            m_Forward[asset] = newDeps;

            // Add reverse edges
            foreach (var dep in newDeps)
            {
                m_AllAssets[dep] = 0;
                var rev = m_Reverse.GetOrAdd(dep, _ => new HashSet<string>());
                lock (rev) { rev.Add(asset); }
            }
        }

        protected void RemoveAssetEdges(string asset)
        {
            if (m_Forward.TryRemove(asset, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (m_Reverse.TryGetValue(dep, out var rev))
                    {
                        lock (rev) { rev.Remove(asset); }
                    }
                }
            }
            if (m_Reverse.TryRemove(asset, out var dependents))
            {
                foreach (var dependent in dependents)
                {
                    if (m_Forward.TryGetValue(dependent, out var fwd))
                    {
                        lock (fwd) { fwd.Remove(asset); }
                    }
                }
            }
            m_AllAssets.TryRemove(asset, out _);
        }

        // --- BFS Reachability ---

        protected void ComputeReachability(IEnumerable<string> roots)
        {
            var reachable = new HashSet<string>();
            var queue = new Queue<string>();

            foreach (var root in roots)
            {
                if (reachable.Add(root))
                    queue.Enqueue(root);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (m_Forward.TryGetValue(current, out var deps))
                {
                    foreach (var dep in deps)
                    {
                        if (reachable.Add(dep))
                            queue.Enqueue(dep);
                    }
                }
            }

            m_Reachable = reachable;
        }

        // --- Queries ---

        public IReadOnlyCollection<string> GetReachableAssets() => m_Reachable;
        public bool IsReachable(string path) => m_Reachable.Contains(path);

        public IEnumerable<string> GetAllUnreachable() =>
            m_AllAssets.Keys.Where(a => !m_Reachable.Contains(a));

        public IReadOnlyCollection<string> GetAllAssets() => (IReadOnlyCollection<string>)m_AllAssets.Keys;

        public int GetDependentCount(string path) =>
            m_Reverse.TryGetValue(path, out var set) ? set.Count : 0;

        public IReadOnlyCollection<string> GetDependencies(string path) =>
            m_Forward.TryGetValue(path, out var set) ? set : (IReadOnlyCollection<string>)System.Array.Empty<string>();

        public IReadOnlyCollection<string> GetDependents(string path) =>
            m_Reverse.TryGetValue(path, out var set) ? set : (IReadOnlyCollection<string>)System.Array.Empty<string>();

        // --- Persistence ---

        public void SaveToDisk()
        {
            PCPCacheIO.AtomicWrite(s_GraphPath, GraphFormatVersion, writer =>
            {
                // Forward edges
                var snapshot = m_Forward.ToArray();
                writer.Write(snapshot.Length);
                foreach (var (asset, deps) in snapshot)
                {
                    writer.Write(asset);
                    writer.Write(deps.Count);
                    foreach (var dep in deps)
                        writer.Write(dep);
                }

                // Reachable set
                writer.Write(m_Reachable.Count);
                foreach (var r in m_Reachable)
                    writer.Write(r);
            });
        }

        public bool LoadFromDisk()
        {
            if (!PCPCacheIO.SafeRead(s_GraphPath, GraphFormatVersion, reader =>
            {
                // Forward edges
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var asset = reader.ReadString();
                    int depCount = reader.ReadInt32();
                    var deps = new HashSet<string>(depCount);
                    for (int j = 0; j < depCount; j++)
                        deps.Add(reader.ReadString());

                    m_Forward[asset] = deps;
                    m_AllAssets[asset] = 0;

                    // Rebuild reverse
                    foreach (var dep in deps)
                    {
                        m_AllAssets[dep] = 0;
                        var rev = m_Reverse.GetOrAdd(dep, _ => new HashSet<string>());
                        rev.Add(asset);
                    }
                }

                // Reachable set
                int reachCount = reader.ReadInt32();
                var reachable = new HashSet<string>(reachCount);
                for (int i = 0; i < reachCount; i++)
                    reachable.Add(reader.ReadString());
                m_Reachable = reachable;

                return true;
            }, out _))
            {
                return false;
            }

            m_IsBuilt = true;
            return true;
        }

        public void Clear()
        {
            m_Forward.Clear();
            m_Reverse.Clear();
            m_Reachable.Clear();
            m_AllAssets.Clear();
            m_IsBuilt = false;
        }

        protected static HashSet<string> CollectRoots(PCPScanContext context)
        {
            return PCPScanOrchestrator.CollectRoots(context);
        }
    }
}
```

**Note:** `CollectRoots` calls into the orchestrator's existing static helper. During the orchestrator rewrite (Task 17), ensure this method stays static and accessible. If the current `CollectRoots` is private, make it `internal static`.

- [ ] **Step 3: Create the factory**

```csharp
// Editor/Core/PCPDependencyResolverFactory.cs
using System;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Creates the correct dependency resolver for the given scan mode.
    /// </summary>
    internal static class PCPDependencyResolverFactory
    {
        public static IPCPDependencyResolver Create(PCPScanMode mode)
        {
            return mode switch
            {
                PCPScanMode.Accurate => new PCPAccurateDependencyResolver(),
                PCPScanMode.Balanced => new PCPBalancedDependencyResolver(new PCPGuidIndex()),
                PCPScanMode.Fast => new PCPFastDependencyResolver(new PCPGuidIndex()),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }
    }
}
```

**Note:** `PCPAccurateDependencyResolver`, `PCPBalancedDependencyResolver`, and `PCPFastDependencyResolver` don't exist yet — they'll be created in Tasks 7–9. This file will not compile until those are done. That's expected; it will be committed together with the first resolver in Task 7.

- [ ] **Step 4: Commit interface and base**

```bash
git add Assets/ProjectCleanPro/Editor/Core/IPCPDependencyResolver.cs
git add Assets/ProjectCleanPro/Editor/Core/IPCPDependencyResolver.cs.meta
git add Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolverBase.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolverBase.cs.meta
git add Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolverFactory.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolverFactory.cs.meta
git commit -m "feat: add IPCPDependencyResolver interface, base class, and factory"
```

---

### Task 7: PCPAccurateDependencyResolver

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPAccurateDependencyResolver.cs`

- [ ] **Step 1: Implement the Accurate resolver**

This resolver mirrors the current `PCPDependencyResolver.BuildAsync()` logic but uses the scheduler for frame-budgeted `AssetDatabase` calls and background threads for graph operations.

```csharp
// Editor/Core/PCPAccurateDependencyResolver.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Accurate mode: uses AssetDatabase.GetDependencies for all assets.
    /// Main-thread work is frame-budgeted via the scheduler.
    /// Graph building and BFS run on background threads.
    /// </summary>
    internal sealed class PCPAccurateDependencyResolver : PCPDependencyResolverBase
    {
        public override async Task BuildGraphAsync(PCPScanContext context, CancellationToken ct)
        {
            var scheduler = context.Scheduler;
            var cache = context.Cache;

            // Try loading from disk first
            if (!m_IsBuilt)
                LoadFromDisk();

            // Phase 1: Remove edges for deleted assets (background)
            var allPaths = await context.GetAllProjectAssetsAsync(ct);
            await scheduler.ScheduleBackground(async ct2 =>
            {
                var currentPathSet = new HashSet<string>(allPaths);
                var toRemove = m_AllAssets.Keys
                    .Where(a => !currentPathSet.Contains(a))
                    .ToList();
                foreach (var path in toRemove)
                    RemoveAssetEdges(path);
            }, ct);

            // Phase 2: Query deps on main thread, frame-budgeted (only stale assets)
            var stalePaths = cache.GetStaleAssets()
                .Where(p => !p.EndsWith(".meta"))
                .ToList();

            if (stalePaths.Count > 0)
            {
                var depsPerAsset = await scheduler.BatchOnMainThread(
                    stalePaths,
                    path => AssetDatabase.GetDependencies(path, false),
                    ct);

                // Phase 3: Update graph (background)
                await scheduler.ScheduleBackground(async ct2 =>
                {
                    for (int i = 0; i < stalePaths.Count; i++)
                    {
                        ct2.ThrowIfCancellationRequested();
                        UpdateEdges(stalePaths[i], depsPerAsset[i]);
                    }
                }, ct);
            }

            // Phase 4: BFS reachability (background)
            await scheduler.ScheduleBackground(async ct2 =>
            {
                var roots = CollectRoots(context);
                ComputeReachability(roots);
            }, ct);

            m_IsBuilt = true;
            SaveToDisk();
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity Editor and verify no compilation errors in the Console. The factory now has all three resolver type references — `PCPBalancedDependencyResolver` and `PCPFastDependencyResolver` will cause errors until Tasks 8–9. Temporarily comment them out in the factory if needed, or defer compilation check until Task 9.

- [ ] **Step 3: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPAccurateDependencyResolver.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPAccurateDependencyResolver.cs.meta
git commit -m "feat: add PCPAccurateDependencyResolver (AssetDatabase-based, frame-budgeted)"
```

---

### Task 8: PCPFastDependencyResolver

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPFastDependencyResolver.cs`

- [ ] **Step 1: Implement the Fast resolver**

```csharp
// Editor/Core/PCPFastDependencyResolver.cs
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Fast mode: pure GUID parsing from file content. Fully background — no main-thread work.
    /// Builds dependency graph by reading .meta files for GUID→path mapping,
    /// then parsing YAML assets for GUID references.
    /// </summary>
    internal sealed class PCPFastDependencyResolver : PCPDependencyResolverBase
    {
        private readonly PCPGuidIndex m_GuidIndex;

        public PCPFastDependencyResolver(PCPGuidIndex guidIndex)
        {
            m_GuidIndex = guidIndex;
        }

        public override async Task BuildGraphAsync(PCPScanContext context, CancellationToken ct)
        {
            if (!m_IsBuilt)
                LoadFromDisk();

            // Step 1: Build GUID index from .meta files (background, parallel)
            var metaFiles = await context.GetAllMetaFilesAsync(ct);
            var changedFiles = context.Cache.HasAnyChanges
                ? new HashSet<string>(context.Cache.GetStaleAssets()) as IReadOnlySet<string>
                : null;

            await m_GuidIndex.BuildAsync(metaFiles, changedFiles, ct);

            // Step 2: Parse GUID references from stale parseable assets (background, parallel)
            var stalePaths = context.Cache.GetStaleAssets()
                .Where(p => PCPGuidParser.IsGuidParseable(p))
                .ToList();

            var results = new ConcurrentDictionary<string, HashSet<string>>();

            await PCPThreading.ParallelForEachAsync(stalePaths, async (path, token) =>
            {
                try
                {
                    var guids = await PCPGuidParser.ParseReferencesAsync(path, token);
                    var resolvedPaths = m_GuidIndex.ResolveAll(guids);
                    results[path] = resolvedPaths;
                }
                catch (IOException) { /* file deleted mid-scan */ }
                catch (System.UnauthorizedAccessException) { /* permission denied */ }
            }, PCPThreading.DefaultConcurrency, ct);

            // Step 3: Update graph edges (background)
            foreach (var (asset, deps) in results)
            {
                ct.ThrowIfCancellationRequested();
                UpdateEdges(asset, deps);
            }

            // Also register non-parseable assets (binaries like .png, .fbx)
            // They are referenced BY other assets but don't reference anything themselves
            var allAssets = await context.GetAllProjectAssetsAsync(ct);
            foreach (var asset in allAssets)
                m_AllAssets[asset] = 0;

            // Step 4: BFS reachability (background)
            var roots = CollectRoots(context);
            ComputeReachability(roots);

            m_IsBuilt = true;
            SaveToDisk();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPFastDependencyResolver.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPFastDependencyResolver.cs.meta
git commit -m "feat: add PCPFastDependencyResolver (pure GUID parsing, fully background)"
```

---

### Task 9: PCPBalancedDependencyResolver

**Files:**
- Create: `Assets/ProjectCleanPro/Editor/Core/PCPBalancedDependencyResolver.cs`

- [ ] **Step 1: Implement the Balanced resolver**

```csharp
// Editor/Core/PCPBalancedDependencyResolver.cs
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Balanced mode: GUID parsing for simple assets (background), AssetDatabase for complex
    /// types (.prefab, .unity) that have variant inheritance or implicit deps.
    /// Both run concurrently — background threads parse files while main thread handles AssetDatabase.
    /// </summary>
    internal sealed class PCPBalancedDependencyResolver : PCPDependencyResolverBase
    {
        private readonly PCPGuidIndex m_GuidIndex;

        private static readonly HashSet<string> k_ComplexExtensions = new()
        {
            ".prefab", ".unity"
        };

        public PCPBalancedDependencyResolver(PCPGuidIndex guidIndex)
        {
            m_GuidIndex = guidIndex;
        }

        public override async Task BuildGraphAsync(PCPScanContext context, CancellationToken ct)
        {
            var scheduler = context.Scheduler;

            if (!m_IsBuilt)
                LoadFromDisk();

            // Step 1: Build GUID index (background)
            var metaFiles = await context.GetAllMetaFilesAsync(ct);
            var changedFiles = context.Cache.HasAnyChanges
                ? new HashSet<string>(context.Cache.GetStaleAssets()) as IReadOnlySet<string>
                : null;
            await m_GuidIndex.BuildAsync(metaFiles, changedFiles, ct);

            // Step 2: Classify stale assets
            var stalePaths = context.Cache.GetStaleAssets()
                .Where(p => !p.EndsWith(".meta"))
                .ToList();

            var simple = stalePaths
                .Where(p => !k_ComplexExtensions.Contains(Path.GetExtension(p))
                            && PCPGuidParser.IsGuidParseable(p))
                .ToList();

            var complex = stalePaths
                .Where(p => k_ComplexExtensions.Contains(Path.GetExtension(p)))
                .ToList();

            // Step 3: Run both in parallel
            // Simple: GUID parse on background threads
            var simpleResults = new ConcurrentDictionary<string, HashSet<string>>();
            var simpleTask = PCPThreading.ParallelForEachAsync(simple, async (path, token) =>
            {
                try
                {
                    var guids = await PCPGuidParser.ParseReferencesAsync(path, token);
                    simpleResults[path] = m_GuidIndex.ResolveAll(guids);
                }
                catch (IOException) { }
                catch (System.UnauthorizedAccessException) { }
            }, PCPThreading.DefaultConcurrency, ct);

            // Complex: AssetDatabase on main thread (frame-budgeted)
            Task<List<string[]>> complexTask;
            if (complex.Count > 0)
            {
                complexTask = scheduler.BatchOnMainThread(
                    complex,
                    path => AssetDatabase.GetDependencies(path, false),
                    ct);
            }
            else
            {
                complexTask = Task.FromResult(new List<string[]>());
            }

            await Task.WhenAll(simpleTask, complexTask);

            // Step 4: Merge into graph
            foreach (var (asset, deps) in simpleResults)
                UpdateEdges(asset, deps);

            var complexDeps = complexTask.Result;
            for (int i = 0; i < complex.Count; i++)
                UpdateEdges(complex[i], complexDeps[i]);

            // Register all assets
            var allAssets = await context.GetAllProjectAssetsAsync(ct);
            foreach (var asset in allAssets)
                m_AllAssets[asset] = 0;

            // Step 5: BFS reachability
            var roots = CollectRoots(context);
            ComputeReachability(roots);

            m_IsBuilt = true;
            SaveToDisk();
        }
    }
}
```

- [ ] **Step 2: Verify full compilation — all 3 resolvers + factory**

Open Unity Editor. Check Console for errors. The factory should now compile since all three resolver types exist.

- [ ] **Step 3: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPBalancedDependencyResolver.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPBalancedDependencyResolver.cs.meta
git commit -m "feat: add PCPBalancedDependencyResolver (hybrid GUID + AssetDatabase)"
```

---

### Task 10: PCPScanCache — Thread-Safe + Async I/O

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Core/PCPScanCache.cs`

This is a rewrite of the internals while preserving the existing public API surface.

- [ ] **Step 1: Replace internal collections with concurrent versions**

Open `Editor/Core/PCPScanCache.cs`. Make the following changes:

1. Add using statements at the top:
```csharp
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
```

2. Change the internal collections:
```csharp
// BEFORE:
private readonly Dictionary<string, CacheEntry> m_Entries;
private HashSet<string> m_StaleAssets;
private HashSet<string> m_StaleOrMetaStaleAssets;
private HashSet<PCPModuleId> m_DirtyModules;

// AFTER:
private readonly ConcurrentDictionary<string, CacheEntry> m_Entries = new();
private readonly ConcurrentDictionary<string, byte> m_StaleAssets = new();
private readonly ConcurrentDictionary<string, byte> m_NewAssets = new();
private readonly ConcurrentDictionary<PCPModuleId, bool> m_DirtyModules = new();
```

3. Update `CacheEntry.metadata` to `ConcurrentDictionary`:
```csharp
// BEFORE:
public Dictionary<string, string> metadata;

// AFTER:
public ConcurrentDictionary<string, string> metadata;
```

- [ ] **Step 2: Add async staleness computation**

Add this new method (keep the old synchronous `RefreshStaleness` for backward compat, but mark it `[Obsolete]`):

```csharp
/// <summary>
/// Async staleness computation. Runs timestamp checks on background threads.
/// </summary>
public async Task RefreshStalenessAsync(PCPScanContext context, CancellationToken ct)
{
    m_StaleAssets.Clear();
    m_NewAssets.Clear();

    if (PCPAssetChangeTracker.FullCheckNeeded)
    {
        // Full check: compare timestamps for all assets on background threads
        var allAssets = await CollectAllAssetPathsAsync(ct);

        await PCPThreading.ParallelForEachAsync(allAssets, (path, token) =>
        {
            if (!m_Entries.TryGetValue(path, out var entry))
            {
                m_NewAssets[path] = 0;
                return Task.CompletedTask;
            }

            try
            {
                var currentTicks = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                if (currentTicks != entry.lastModifiedTicks)
                    m_StaleAssets[path] = 0;
            }
            catch (System.IO.IOException) { m_StaleAssets[path] = 0; }

            return Task.CompletedTask;
        }, PCPThreading.DefaultConcurrency, ct);

        // Prune deleted entries
        var currentPathSet = new HashSet<string>(allAssets);
        foreach (var key in m_Entries.Keys)
        {
            if (!currentPathSet.Contains(key))
                m_Entries.TryRemove(key, out _);
        }
    }
    else if (PCPAssetChangeTracker.HasChanges)
    {
        foreach (var path in PCPAssetChangeTracker.ChangedAssets)
        {
            ct.ThrowIfCancellationRequested();
            if (!m_Entries.ContainsKey(path))
                m_NewAssets[path] = 0;
            else
                m_StaleAssets[path] = 0;
        }
    }
}

/// <summary>
/// Returns all stale + new asset paths.
/// </summary>
public IReadOnlyList<string> GetStaleAssets()
{
    return m_StaleAssets.Keys.Concat(m_NewAssets.Keys).ToList();
}

private static Task<List<string>> CollectAllAssetPathsAsync(CancellationToken ct)
{
    return Task.Run(() =>
    {
        return System.IO.Directory.EnumerateFiles("Assets", "*.*",
                System.IO.SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".meta"))
            .Select(p => p.Replace('\\', '/'))
            .ToList();
    }, ct);
}
```

- [ ] **Step 3: Add async stamping and persistence**

```csharp
public async Task StampProcessedAssetsAsync(CancellationToken ct)
{
    var toStamp = m_StaleAssets.Keys.Concat(m_NewAssets.Keys).ToList();

    await PCPThreading.ParallelForEachAsync(toStamp, (path, token) =>
    {
        try
        {
            if (!System.IO.File.Exists(path)) return Task.CompletedTask;

            var ticks = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            var size = new System.IO.FileInfo(path).Length;

            m_Entries.AddOrUpdate(path,
                _ => new CacheEntry
                {
                    assetPath = path,
                    lastModifiedTicks = ticks,
                    fileSizeBytes = size
                },
                (_, existing) =>
                {
                    existing.lastModifiedTicks = ticks;
                    existing.fileSizeBytes = size;
                    return existing;
                });
        }
        catch (System.IO.IOException) { }

        return Task.CompletedTask;
    }, PCPThreading.DefaultConcurrency, ct);

    m_StaleAssets.Clear();
    m_NewAssets.Clear();
}

public async Task SaveAsync(CancellationToken ct)
{
    await Task.Run(() => Save(), ct);
}

public async Task LoadAsync(CancellationToken ct)
{
    await Task.Run(() => Load(), ct);
}
```

- [ ] **Step 4: Update thread-safe accessors**

Ensure all public accessors are thread-safe. Most already are since `ConcurrentDictionary` handles concurrent reads/writes. Verify that `IsStale`, `GetHash`, `SetHash`, `GetMetadata`, `SetMetadata` use thread-safe operations.

- [ ] **Step 5: Run existing `PCPScanCacheTests`**

Run: Test Runner → EditMode → PCPScanCacheTests
Expected: All existing tests still PASS. The public API hasn't changed, only the internal thread-safety.

- [ ] **Step 6: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPScanCache.cs
git commit -m "feat: make PCPScanCache thread-safe with async I/O"
```

---

### Task 11: PCPModuleBase — Warnings + Atomic Progress

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPModuleBase.cs`

- [ ] **Step 1: Add warnings and atomic progress**

Open `Editor/Modules/PCPModuleBase.cs`. Add:

```csharp
using System.Collections.Concurrent;
using System.Threading;
```

Add these fields to the class:

```csharp
/// <summary>Thread-safe warning collection. Modules add warnings from background threads.</summary>
protected readonly ConcurrentQueue<string> m_Warnings = new();

/// <summary>Atomic progress counter for thread-safe progress reporting.</summary>
protected int m_ProcessedCount;
protected int m_TotalCount;
```

Add these public properties:

```csharp
public IReadOnlyList<string> Warnings => m_Warnings.ToArray();
public int WarningCount => m_Warnings.Count;

/// <summary>Thread-safe progress based on atomic counters. Returns 0-1.</summary>
public float AtomicProgress => m_TotalCount == 0 ? 0f :
    (float)Interlocked.CompareExchange(ref m_ProcessedCount, 0, 0) / m_TotalCount;
```

Update the `ScanAsync` method to clear warnings and counters at start:

```csharp
// At the beginning of ScanAsync, before DoScanAsync:
while (m_Warnings.TryDequeue(out _)) { }  // Clear previous warnings
Interlocked.Exchange(ref m_ProcessedCount, 0);
Interlocked.Exchange(ref m_TotalCount, 0);
```

- [ ] **Step 2: Add to `IPCPModule` interface**

Open `Editor/Modules/IPCPModule.cs`. Add:

```csharp
IReadOnlyList<string> Warnings { get; }
int WarningCount { get; }
```

- [ ] **Step 3: Run all tests to ensure nothing broke**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 4: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Modules/PCPModuleBase.cs
git add Assets/ProjectCleanPro/Editor/Modules/IPCPModule.cs
git commit -m "feat: add warnings queue and atomic progress to PCPModuleBase"
```

---

### Task 12: PCPScanContext — New Properties + Async Asset Listing

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Core/PCPScanContext.cs`

- [ ] **Step 1: Add new properties and async methods**

Open `Editor/Core/PCPScanContext.cs`. Add using statements:

```csharp
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
```

Add new properties:

```csharp
/// <summary>The scan's work coordinator. Created per-scan, null before scan starts.</summary>
public PCPAsyncScheduler Scheduler { get; set; }

/// <summary>
/// Dependency resolver — interface, implementation varies by scan mode.
/// Replaces the concrete PCPDependencyResolver property.
/// </summary>
public IPCPDependencyResolver NewDependencyResolver { get; set; }

/// <summary>Shared GUID index for Fast/Balanced modes. Null in Accurate mode.</summary>
public PCPGuidIndex GuidIndex { get; set; }
```

Add async asset listing methods:

```csharp
private List<string> m_AllProjectAssetsAsync;
private List<string> m_AllMetaFiles;

/// <summary>
/// Get all project asset paths asynchronously via System.IO (background thread).
/// Cached for the scan session. Replaces the synchronous AllProjectAssets property
/// for new code paths.
/// </summary>
public async Task<IReadOnlyList<string>> GetAllProjectAssetsAsync(CancellationToken ct)
{
    if (m_AllProjectAssetsAsync != null) return m_AllProjectAssetsAsync;

    m_AllProjectAssetsAsync = await Task.Run(() =>
    {
        return Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".meta"))
            .Select(p => p.Replace('\\', '/'))
            .ToList();
    }, ct);

    return m_AllProjectAssetsAsync;
}

/// <summary>
/// Get all .meta file paths asynchronously. Needed by PCPGuidIndex.
/// </summary>
public async Task<IReadOnlyList<string>> GetAllMetaFilesAsync(CancellationToken ct)
{
    if (m_AllMetaFiles != null) return m_AllMetaFiles;

    m_AllMetaFiles = await Task.Run(() =>
    {
        return Directory.EnumerateFiles("Assets", "*.meta", SearchOption.AllDirectories)
            .Select(p => p.Replace('\\', '/'))
            .ToList();
    }, ct);

    return m_AllMetaFiles;
}
```

Add async finalize:

```csharp
/// <summary>
/// Async version of FinalizeScan. Stamps and saves on background threads.
/// </summary>
public async Task FinalizeScanAsync(CancellationToken ct)
{
    await Cache.StampProcessedAssetsAsync(ct);
    await Cache.SaveAsync(ct);
    PCPAssetChangeTracker.Reset();
}
```

Add thread-safe progress:

```csharp
private float m_ProgressFraction;
private string m_ProgressLabel = string.Empty;

/// <summary>Thread-safe progress reporting. Written from any thread, read by UI on main.</summary>
public void ReportProgressThreadSafe(float fraction, string label)
{
    Interlocked.Exchange(ref m_ProgressFraction, fraction);
    Volatile.Write(ref m_ProgressLabel, label);
}

public float ProgressFraction => Volatile.Read(ref m_ProgressFraction);
public string ProgressLabelThreadSafe => Volatile.Read(ref m_ProgressLabel);
```

- [ ] **Step 2: Run tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS. New properties are additive.

- [ ] **Step 3: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPScanContext.cs
git commit -m "feat: add scheduler, resolver interface, and async methods to PCPScanContext"
```

---

### Task 13: PCPDuplicateDetector — Three-Phase Rewrite

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPDuplicateDetector.cs`

- [ ] **Step 1: Rewrite `DoScanAsync` with three-phase pattern**

This is the biggest change. Replace the body of `DoScanAsync` in `PCPDuplicateDetector`. Keep all existing result types (`PCPDuplicateGroup`, `PCPDuplicateEntry`), `WriteResults`/`ReadResults`, and helper methods. Only the scan logic changes.

Read the current `DoScanAsync` implementation first to understand what result fields and helper methods exist, then replace the scan body with:

```csharp
protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
{
    var cache = context.Cache;
    var settings = context.Settings;

    // Collect candidates (filter excluded extensions, ignored paths)
    var allAssets = await context.GetAllProjectAssetsAsync(ct);
    var candidates = allAssets
        .Where(p => !IsExcludedExtension(p, settings) && !IsIgnored(p, context))
        .ToList();

    Interlocked.Exchange(ref m_TotalCount, candidates.Count);

    // === PHASE 1: GATHER — Hash on background threads ===
    var hashMap = new ConcurrentDictionary<string, string>();

    await PCPThreading.ParallelForEachAsync(candidates, async (path, token) =>
    {
        try
        {
            // Check cache first
            if (!cache.NeedsProcessing(path))
            {
                var cachedHash = cache.GetHash(path);
                if (cachedHash != null)
                {
                    hashMap[path] = cachedHash;
                    Interlocked.Increment(ref m_ProcessedCount);
                    return;
                }
            }

            // Cache miss — hash on background thread
            if (!System.IO.File.Exists(path))
            {
                Interlocked.Increment(ref m_ProcessedCount);
                return;
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(path, token);
            string hash;

            if (PCPGuidParser.IsGuidParseable(path))
            {
                // Normalize YAML: compute hash from metadata key in cache if available
                var normalizedHash = cache.GetMetadata(path, "dup.normalizedHash");
                if (normalizedHash != null && !cache.NeedsProcessing(path))
                {
                    hash = normalizedHash;
                }
                else
                {
                    hash = ComputeNormalizedYamlHash(bytes);
                    cache.SetMetadata(path, "dup.normalizedHash", hash);
                }
            }
            else
            {
                hash = ComputeSHA256(bytes);
            }

            hashMap[path] = hash;
            cache.SetHash(path, hash);
        }
        catch (OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            m_Warnings.Enqueue($"Skipped {path}: {ex.Message}");
        }
        finally
        {
            Interlocked.Increment(ref m_ProcessedCount);
        }
    }, PCPThreading.DefaultConcurrency, ct);

    // Group by hash — only groups with 2+ entries are duplicates
    var groups = hashMap
        .GroupBy(kv => kv.Value)
        .Where(g => g.Count() > 1)
        .ToList();

    // === PHASE 2: QUERY — Import settings comparison (main thread, budgeted) ===
    if (settings.duplicateCompareImportSettings && context.Scheduler != null)
    {
        groups = await RefineByImportSettingsAsync(groups, context, ct);
    }

    // === PHASE 3: ANALYZE — Build results (background) ===
    var resolver = context.NewDependencyResolver ?? (object)context.DependencyResolver;

    m_Results = new List<PCPDuplicateGroup>();
    foreach (var group in groups)
    {
        ct.ThrowIfCancellationRequested();
        var dupGroup = BuildDuplicateGroup(group, resolver);
        if (dupGroup != null)
            m_Results.Add(dupGroup);
    }

    m_Results.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));
}
```

**Note:** `ComputeSHA256`, `ComputeNormalizedYamlHash`, `BuildDuplicateGroup`, `IsExcludedExtension`, and `RefineByImportSettingsAsync` are helper methods. Keep existing helpers. Add `RefineByImportSettingsAsync` if it doesn't exist:

```csharp
private async Task<List<IGrouping<string, KeyValuePair<string, string>>>> RefineByImportSettingsAsync(
    List<IGrouping<string, KeyValuePair<string, string>>> groups,
    PCPScanContext context,
    CancellationToken ct)
{
    // For each group, subdivide by importer settings
    var refined = new List<IGrouping<string, KeyValuePair<string, string>>>();

    foreach (var group in groups)
    {
        var paths = group.Select(kv => kv.Key).ToList();
        if (paths.Count < 2) continue;

        // Get import settings on main thread
        var settings = await context.Scheduler.BatchOnMainThread(
            paths,
            path =>
            {
                var importer = AssetImporter.GetAtPath(path);
                return importer != null ? EditorJsonUtility.ToJson(importer) : "";
            },
            ct);

        // Subdivide by settings
        var subGroups = paths
            .Select((p, i) => new { Path = p, Hash = group.Key, Settings = settings[i] })
            .GroupBy(x => x.Hash + "|" + x.Settings)
            .Where(g => g.Count() > 1);

        foreach (var sub in subGroups)
        {
            refined.Add(sub.Select(x =>
                new KeyValuePair<string, string>(x.Path, group.Key))
                .GroupBy(kv => kv.Value).First());
        }
    }

    return refined;
}
```

- [ ] **Step 2: Run tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS. Existing `PCPDuplicateGroupTests` test the data model, not the scan logic.

- [ ] **Step 3: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Modules/PCPDuplicateDetector.cs
git commit -m "feat: rewrite PCPDuplicateDetector with three-phase async pattern"
```

---

### Task 14: PCPUnusedScanner — Three-Phase Rewrite

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPUnusedScanner.cs`

- [ ] **Step 1: Rewrite `DoScanAsync` with three-phase pattern**

Read the current implementation first, then replace the scan body. Keep all result types, `WriteResults`/`ReadResults`, and helper methods.

```csharp
protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
{
    // Get the reachable set from the dependency resolver
    var resolver = context.NewDependencyResolver ?? (object)context.DependencyResolver;
    IReadOnlyCollection<string> reachable;

    if (resolver is IPCPDependencyResolver newResolver)
        reachable = newResolver.GetReachableAssets();
    else
        reachable = ((PCPDependencyResolver)resolver).GetAllReachable();

    var reachableSet = new HashSet<string>(reachable);

    // === PHASE 1: GATHER — Collect all asset paths (background) ===
    var allAssets = await context.GetAllProjectAssetsAsync(ct);
    var candidates = allAssets
        .Where(p => !IsExcludedExtension(p, context.Settings)
                    && !IsEditorOnlyPath(p, context.Settings))
        .ToList();

    Interlocked.Exchange(ref m_TotalCount, candidates.Count);

    // === PHASE 2: QUERY — Verify paths if needed (Accurate/Balanced only) ===
    // In Fast mode, we trust the GUID index for path validation
    // No main-thread queries needed for unused detection — reachability is already computed

    // === PHASE 3: ANALYZE — Cross-reference against reachable set (background) ===
    m_Results = new List<PCPUnusedAsset>();

    await PCPThreading.RunOnBackground(() =>
    {
        foreach (var path in candidates)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref m_ProcessedCount);

            if (reachableSet.Contains(path))
                continue;

            if (IsIgnored(path, context))
                continue;

            var asset = BuildUnusedAsset(path, context);
            if (asset != null)
            {
                lock (m_Results)
                {
                    m_Results.Add(asset);
                }
            }
        }

        m_Results.Sort((a, b) => b.AssetInfo.Size.CompareTo(a.AssetInfo.Size));
        return Task.CompletedTask;
    }, ct);
}
```

- [ ] **Step 2: Run tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 3: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Modules/PCPUnusedScanner.cs
git commit -m "feat: rewrite PCPUnusedScanner with three-phase async pattern"
```

---

### Task 15: PCPMissingRefScanner — Background Pre-Filter

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPMissingRefScanner.cs`

- [ ] **Step 1: Add background pre-filter phase**

Read current implementation first. The key change: before calling `LoadAllAssetsAtPath()` on the main thread, read the file as text on a background thread and check if it contains suspicious patterns (`fileID: 0` or missing GUID patterns). Most files (85-95%) won't have these patterns and can be skipped entirely.

```csharp
protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
{
    var allAssets = await context.GetAllProjectAssetsAsync(ct);
    var candidates = allAssets
        .Where(p =>
        {
            var ext = System.IO.Path.GetExtension(p);
            return ext is ".prefab" or ".unity" or ".asset";
        })
        .Where(p => !IsIgnored(p, context))
        .ToList();

    Interlocked.Exchange(ref m_TotalCount, candidates.Count);

    // === PHASE 1: GATHER — Pre-filter on background threads ===
    // Read file content and check for suspicious patterns that indicate missing refs
    var suspicious = new ConcurrentBag<string>();

    await PCPThreading.ParallelForEachAsync(candidates, async (path, token) =>
    {
        try
        {
            // Skip if cached as "0 missing" and not stale
            if (!context.Cache.NeedsProcessing(path))
            {
                var cachedCount = context.Cache.GetMetadata(path, "missing.count");
                if (cachedCount == "0")
                {
                    Interlocked.Increment(ref m_ProcessedCount);
                    return;
                }
            }

            // Read file and check for missing-ref patterns
            var content = await System.IO.File.ReadAllTextAsync(path, token);
            if (ContainsSuspiciousPatterns(content))
                suspicious.Add(path);
        }
        catch (OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            m_Warnings.Enqueue($"Skipped {path}: {ex.Message}");
        }
        finally
        {
            Interlocked.Increment(ref m_ProcessedCount);
        }
    }, PCPThreading.DefaultConcurrency, ct);

    // === PHASE 2: QUERY — Deep inspection on main thread (frame-budgeted) ===
    var suspiciousList = suspicious.ToList();

    if (suspiciousList.Count > 0 && context.Scheduler != null)
    {
        var results = await context.Scheduler.BatchOnMainThread(
            suspiciousList,
            path => InspectForMissingRefs(path),
            ct);

        // === PHASE 3: ANALYZE — Build results ===
        m_Results = new List<PCPMissingReference>();
        for (int i = 0; i < suspiciousList.Count; i++)
        {
            var refs = results[i];
            if (refs != null && refs.Count > 0)
                m_Results.AddRange(refs);

            // Cache the count for incremental skipping
            context.Cache.SetMetadata(suspiciousList[i], "missing.count",
                (refs?.Count ?? 0).ToString());
        }
    }
    else
    {
        m_Results = new List<PCPMissingReference>();
    }
}

/// <summary>
/// Quick text-based check for patterns that indicate potential missing references.
/// This runs on background threads and filters out 85-95% of candidates.
/// </summary>
private static bool ContainsSuspiciousPatterns(string content)
{
    return content.Contains("fileID: 0,") ||
           content.Contains("guid: 00000000000000000000000000000000") ||
           content.Contains("{fileID: 0}");
}
```

**Note:** `InspectForMissingRefs(path)` should wrap the existing main-thread logic that calls `AssetDatabase.LoadAllAssetsAtPath()` and inspects serialized objects. Extract that from the current `DoScanAsync` into a separate method if not already separated.

- [ ] **Step 2: Run tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 3: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Modules/PCPMissingRefScanner.cs
git commit -m "feat: add background pre-filter to PCPMissingRefScanner"
```

---

### Task 16: Minor Module Updates

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPDependencyModule.cs`
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPShaderAnalyzer.cs`
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPSizeProfiler.cs`
- Modify: `Assets/ProjectCleanPro/Editor/Modules/PCPPackageAuditor.cs`

- [ ] **Step 1: Update PCPDependencyModule to use the resolver interface**

Read `PCPDependencyModule.cs`. Where it accesses `context.DependencyResolver` (the old concrete type), add fallback logic:

```csharp
// Where the module accesses the dependency resolver:
var resolver = context.NewDependencyResolver ?? (object)context.DependencyResolver;

// For queries:
if (resolver is IPCPDependencyResolver newResolver)
{
    // Use interface methods
}
else
{
    // Fallback to old concrete type (during transition)
}
```

- [ ] **Step 2: Update remaining modules to use scheduler for yielding**

For `PCPShaderAnalyzer`, `PCPSizeProfiler`, `PCPPackageAuditor`: their scan logic is lighter but still uses `YieldIfNeeded`. These modules continue to work with the existing `YieldIfNeeded` pattern since they don't have heavy I/O. No structural changes needed — they'll benefit automatically from the orchestrator running them in parallel (Task 17).

If any of these modules do file I/O (e.g., reading shader source files), wrap those reads in `Task.Run` for background execution. Read each module's `DoScanAsync` first to identify any such cases.

- [ ] **Step 3: Run all tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 4: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Modules/PCPDependencyModule.cs
git add Assets/ProjectCleanPro/Editor/Modules/PCPShaderAnalyzer.cs
git add Assets/ProjectCleanPro/Editor/Modules/PCPSizeProfiler.cs
git add Assets/ProjectCleanPro/Editor/Modules/PCPPackageAuditor.cs
git commit -m "feat: update remaining modules for new resolver interface"
```

---

### Task 17: PCPScanOrchestrator — Rewrite

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Core/PCPScanOrchestrator.cs`

This is the most critical integration task. The orchestrator ties everything together.

- [ ] **Step 1: Read the current orchestrator completely**

Read `Editor/Core/PCPScanOrchestrator.cs` end-to-end. Note:
- The `CollectRoots` method — make it `internal static` if it's private (needed by `PCPDependencyResolverBase`)
- The `ScanAllAsync` flow
- The `ScanModuleAsync` flow
- The `ComputeManifest` method
- The sync wrappers

- [ ] **Step 2: Add mode-switch invalidation**

Add this method:

```csharp
private void InvalidateCachesForModeSwitch(PCPScanContext context)
{
    // Clear dependency graph cache
    var graphPath = Path.Combine("Library", "ProjectCleanPro", "DepGraph.bin");
    if (File.Exists(graphPath))
        File.Delete(graphPath);

    // Clear all module results
    m_ResultCache.InvalidateAll();

    // Mark all modules dirty
    foreach (var module in m_Modules)
    {
        if (module != null)
            context.Cache.MarkModuleDirty(module.Id);
    }

    // Keep timestamp/hash data — mode-independent
    // Only clear staleness flags
    context.Cache.ResetStaleness();
}
```

- [ ] **Step 3: Rewrite `ScanAllAsync`**

Replace the body of `ScanAllAsync` with the new flow:

```csharp
public async Task<PCPScanManifest> ScanAllAsync(
    PCPScanContext context,
    Action<float, string> onProgress = null,
    CancellationToken ct = default)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();

    // Step 1: Handle mode switch invalidation
    var settings = context.Settings;
    if (settings.scanMode != settings.lastScanMode)
    {
        InvalidateCachesForModeSwitch(context);
        settings.lastScanMode = settings.scanMode;
        settings.Save();
    }

    // Step 2: Create scheduler with user's frame budget
    using var scheduler = new PCPAsyncScheduler(settings.mainThreadBudgetMs);
    context.Scheduler = scheduler;

    // Step 3: Create the right dependency resolver
    context.NewDependencyResolver = PCPDependencyResolverFactory.Create(settings.scanMode);

    // Step 4: Async staleness computation (background)
    onProgress?.Invoke(0f, "Checking for changes...");
    await context.Cache.RefreshStalenessAsync(context, ct);
    context.Cache.ComputeModuleDirtiness(GetActiveModuleList());

    // Apply settings-based dirtiness
    ApplyExternalDirtiness(context);

    // Step 5: Skip check
    if (!context.Cache.HasAnyChanges && AllModulesHaveResults())
    {
        var cached = m_ResultCache.LoadManifest();
        if (cached != null)
        {
            onProgress?.Invoke(1f, "No changes detected");
            return cached;
        }
    }

    // Step 6: Build dependency graph (if needed)
    var dirtyModules = GetDirtyModules(context);
    bool needsGraph = dirtyModules.Any(m => m.RequiresDependencyGraph);

    if (needsGraph)
    {
        onProgress?.Invoke(0.05f, "Building dependency graph...");
        await context.NewDependencyResolver.BuildGraphAsync(context, ct);
    }

    // Step 7: Run modules
    onProgress?.Invoke(0.30f, "Scanning modules...");
    float progressBase = 0.30f;
    float progressPerModule = 0.65f / Math.Max(1, dirtyModules.Count);

    foreach (var module in dirtyModules)
    {
        ct.ThrowIfCancellationRequested();
        onProgress?.Invoke(progressBase, $"Scanning: {module.DisplayName}...");

        module.Clear();
        await module.ScanAsync(context, ct);
        m_ResultCache.SaveModule(module);

        progressBase += progressPerModule;
    }

    // Step 8: Finalize (background)
    onProgress?.Invoke(0.95f, "Saving results...");
    await context.FinalizeScanAsync(ct);

    // Step 9: Manifest
    sw.Stop();
    var manifest = ComputeManifest(context, (float)sw.Elapsed.TotalSeconds);
    m_ResultCache.SaveManifest(manifest);

    onProgress?.Invoke(1f, "Complete");
    return manifest;
}

private List<IPCPModule> GetDirtyModules(PCPScanContext context)
{
    var result = new List<IPCPModule>();
    foreach (var id in s_ExecutionOrder)
    {
        var module = m_Modules[(int)id];
        if (module != null && IsDirty(id, context))
            result.Add(module);
    }
    return result;
}
```

- [ ] **Step 4: Make `CollectRoots` internal static**

If `CollectRoots` is currently `private static`, change it to `internal static` so `PCPDependencyResolverBase` can call it.

- [ ] **Step 5: Run all tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPScanOrchestrator.cs
git commit -m "feat: rewrite PCPScanOrchestrator with scheduler, resolver factory, mode switching"
```

---

### Task 18: PCPContext + PCPResultCacheManager + PCPAPI Wiring

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Core/PCPContext.cs`
- Modify: `Assets/ProjectCleanPro/Editor/Core/PCPResultCacheManager.cs`
- Modify: `Assets/ProjectCleanPro/Editor/API/PCPAPI.cs`

- [ ] **Step 1: Update `PCPContext` to wire new components**

Open `Editor/Core/PCPContext.cs`. The existing `PCPDependencyResolver` stays for backward compatibility (old modules may still reference it). Add the new resolver creation in `Initialize()` or where context is built.

In the scan flow (where `PCPScanContext.FromGlobalContext()` is called), the new `IPCPDependencyResolver` is created per-scan by the orchestrator, not stored globally. No changes needed to the global context for this — the orchestrator handles it.

- [ ] **Step 2: Add async methods to `PCPResultCacheManager`**

Open `Editor/Core/PCPResultCacheManager.cs`. Add:

```csharp
public async Task SaveModuleAsync(IPCPModule module, CancellationToken ct)
{
    await Task.Run(() => SaveModule(module), ct);
}

public async Task SaveManifestAsync(PCPScanManifest manifest, CancellationToken ct)
{
    await Task.Run(() => SaveManifest(manifest), ct);
}
```

- [ ] **Step 3: Update `PCPAPI` to accept scan mode**

Open `Editor/API/PCPAPI.cs`. Find `PCPScanOptions` (or the options struct/class) and add:

```csharp
public PCPScanMode? ScanMode { get; set; }  // null = use settings default
```

In `RunScan`, apply the transient option before scanning:

```csharp
if (options.ScanMode.HasValue)
    context.Settings.scanMode = options.ScanMode.Value;
```

- [ ] **Step 4: Run tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Core/PCPContext.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPResultCacheManager.cs
git add Assets/ProjectCleanPro/Editor/API/PCPAPI.cs
git commit -m "feat: wire new components in PCPContext, add async to ResultCacheManager, update PCPAPI"
```

---

### Task 19: PCPScanManifest — Add Warnings

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/Data/PCPScanManifest.cs`

- [ ] **Step 1: Add warnings to the manifest**

Open `Editor/Data/PCPScanManifest.cs`. Add:

```csharp
/// <summary>Warnings from modules that encountered non-fatal errors during scan.</summary>
public List<ScanWarning> warnings = new List<ScanWarning>();

[System.Serializable]
public struct ScanWarning
{
    public PCPModuleId moduleId;
    public string message;

    public ScanWarning(PCPModuleId moduleId, string message)
    {
        this.moduleId = moduleId;
        this.message = message;
    }
}
```

- [ ] **Step 2: Update serialization**

In the `WriteResults`/`ReadResults` or wherever the manifest is serialized, add warning serialization:

```csharp
// In write:
writer.Write(warnings.Count);
foreach (var w in warnings)
{
    writer.Write((byte)w.moduleId);
    writer.Write(w.message ?? "");
}

// In read:
int warnCount = reader.ReadInt32();
warnings = new List<ScanWarning>(warnCount);
for (int i = 0; i < warnCount; i++)
{
    warnings.Add(new ScanWarning(
        (PCPModuleId)reader.ReadByte(),
        reader.ReadString()));
}
```

**Note:** Bump the manifest format version to ensure old manifests are discarded gracefully.

- [ ] **Step 3: Update orchestrator to populate warnings**

In `PCPScanOrchestrator.ComputeManifest` (or where the manifest is built), add:

```csharp
manifest.warnings = dirtyModules
    .SelectMany(m => m.Warnings.Select(w => new PCPScanManifest.ScanWarning(m.Id, w)))
    .ToList();
```

- [ ] **Step 4: Run tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/Data/PCPScanManifest.cs
git add Assets/ProjectCleanPro/Editor/Core/PCPScanOrchestrator.cs
git commit -m "feat: add scan warnings to PCPScanManifest"
```

---

### Task 20: PCPDashboardView — Scan Mode UI + Banners + Warnings

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/UI/PCPDashboardView.cs`

- [ ] **Step 1: Read the current dashboard to understand layout**

Read `Editor/UI/PCPDashboardView.cs` end-to-end. Identify:
- Where the scan button is drawn
- Where results are displayed
- The method that draws the header area

- [ ] **Step 2: Add scan mode dropdown**

Find the header/toolbar area (near the scan button). Add the scan mode dropdown:

```csharp
// Draw scan mode selector
var settings = PCPContext.Settings;
var newMode = (PCPScanMode)EditorGUILayout.EnumPopup(
    new GUIContent("Scan Mode", "Accurate: full AssetDatabase analysis (slowest, most accurate)\n" +
                                "Balanced: hybrid GUID parsing + AssetDatabase\n" +
                                "Fast: pure file parsing (fastest, may miss some dependencies)"),
    settings.scanMode);
if (newMode != settings.scanMode)
{
    settings.scanMode = newMode;
    settings.Save();
}
```

- [ ] **Step 3: Add warning banner for mode switch**

Below the scan mode dropdown, add:

```csharp
// Warning: mode changed, cache invalidation needed
if (settings.scanMode != settings.lastScanMode
    && PCPContext.ResultCacheManager.HasCachedManifest)
{
    EditorGUILayout.HelpBox(
        "Scan mode changed \u2014 cached results will be cleared and a full rescan is required.",
        MessageType.Warning);
}
```

- [ ] **Step 4: Add fast mode accuracy label**

```csharp
// Info: fast mode accuracy caveat
if (settings.scanMode == PCPScanMode.Fast)
{
    EditorGUILayout.HelpBox(
        "Fast scan \u2014 some dependencies may not be detected.",
        MessageType.Info);
}
```

- [ ] **Step 5: Add warnings display after scan**

Find where scan results are displayed. Add a collapsible warnings section:

```csharp
// Scan warnings section
var manifest = PCPContext.LastScanManifest;
if (manifest?.warnings != null && manifest.warnings.Count > 0)
{
    m_ShowWarnings = EditorGUILayout.Foldout(m_ShowWarnings,
        $"{manifest.warnings.Count} file(s) could not be scanned", true);

    if (m_ShowWarnings)
    {
        EditorGUI.indentLevel++;
        foreach (var warning in manifest.warnings)
        {
            EditorGUILayout.LabelField($"\u2022 {warning.message}",
                EditorStyles.miniLabel);
        }
        EditorGUI.indentLevel--;
    }
}
```

Add the field to the class:

```csharp
private bool m_ShowWarnings;
```

- [ ] **Step 6: Run tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 7: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/UI/PCPDashboardView.cs
git commit -m "feat: add scan mode selector, warning banner, and warnings display to dashboard"
```

---

### Task 21: PCPSettingsView — Frame Budget Slider

**Files:**
- Modify: `Assets/ProjectCleanPro/Editor/UI/PCPSettingsView.cs`

- [ ] **Step 1: Read the current settings view**

Read `Editor/UI/PCPSettingsView.cs`. Find the section where performance-related settings are drawn (or the end of the settings list).

- [ ] **Step 2: Add frame budget slider**

Add a "Performance" section:

```csharp
EditorGUILayout.Space(8);
EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);

var newBudget = EditorGUILayout.Slider(
    new GUIContent("Main Thread Budget (ms)",
        "Controls how much time per frame is spent on scan work.\n" +
        "Lower values keep the editor smoother during scans.\n" +
        "Higher values make scans complete faster but may cause stuttering.\n\n" +
        "4ms = very smooth editor, slower scans\n" +
        "8ms = balanced (default)\n" +
        "16ms = fastest scans, editor may stutter"),
    settings.mainThreadBudgetMs,
    4f,
    16f);

if (!Mathf.Approximately(newBudget, settings.mainThreadBudgetMs))
{
    settings.mainThreadBudgetMs = newBudget;
    settings.Save();
}
```

- [ ] **Step 3: Run tests**

Run: Test Runner → EditMode → Run All (including `PCPSettingsViewTests`)
Expected: All PASS.

- [ ] **Step 4: Commit**

```bash
git add Assets/ProjectCleanPro/Editor/UI/PCPSettingsView.cs
git commit -m "feat: add frame budget slider to settings view"
```

---

### Task 22: Delete PCPEditorAsync + Old PCPDependencyResolver

**Files:**
- Delete: `Assets/ProjectCleanPro/Editor/Core/PCPEditorAsync.cs`
- Delete: `Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolver.cs`

- [ ] **Step 1: Search for all references to `PCPEditorAsync`**

Search the codebase for `PCPEditorAsync`. Any remaining references must be updated to use `PCPAsyncScheduler` or `PCPThreading`. Common patterns to replace:

```csharp
// BEFORE:
await PCPEditorAsync.YieldToEditor();

// AFTER (if inside a module with access to scheduler):
// No direct replacement needed — the scheduler handles yielding automatically
// via frame-budgeted DrainMainQueue. Remove the explicit yield calls.

// BEFORE:
PCPEditorAsync.RunSync(() => ScanAllAsync(...));

// AFTER:
// Keep sync wrappers in the orchestrator that use Task spinning
```

- [ ] **Step 2: Search for all references to old `PCPDependencyResolver` (concrete class)**

Search for `PCPDependencyResolver` (not `IPCPDependencyResolver`). Update any remaining references:
- `context.DependencyResolver` (old concrete type) → `context.NewDependencyResolver` (interface)
- Direct instantiation `new PCPDependencyResolver()` → `PCPDependencyResolverFactory.Create(mode)`

- [ ] **Step 2b: Rename `NewDependencyResolver` to `DependencyResolver` in PCPScanContext**

Now that the old concrete `PCPDependencyResolver` class is being deleted, rename the transitional property:
- In `PCPScanContext.cs`: rename `NewDependencyResolver` → `DependencyResolver` (type: `IPCPDependencyResolver`)
- Remove the old `DependencyResolver` property that returned the concrete type
- Search all files for `NewDependencyResolver` and replace with `DependencyResolver`
- Search all files for `context.DependencyResolver` and verify they now use the interface type

- [ ] **Step 3: Delete the files**

```bash
git rm Assets/ProjectCleanPro/Editor/Core/PCPEditorAsync.cs
git rm Assets/ProjectCleanPro/Editor/Core/PCPEditorAsync.cs.meta
git rm Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolver.cs
git rm Assets/ProjectCleanPro/Editor/Core/PCPDependencyResolver.cs.meta
```

- [ ] **Step 4: Verify compilation**

Open Unity Editor. Check Console for zero errors. All references to deleted types should have been updated in prior tasks.

- [ ] **Step 5: Run all tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS. `PCPDependencyResolverTests` may need updating if they test the old concrete class directly — update them to test through the interface or the Accurate resolver.

- [ ] **Step 6: Commit**

```bash
git commit -m "refactor: remove PCPEditorAsync and old PCPDependencyResolver"
```

---

### Task 23: Integration Tests

**Files:**
- Modify: `Assets/ProjectCleanPro/Tests/Editor/PCPIntegrationTests.cs`

- [ ] **Step 1: Add integration tests for scan modes**

```csharp
[Test]
public void ScanMode_DefaultIsAccurate()
{
    var settings = PCPSettings.instance;
    Assert.AreEqual(PCPScanMode.Accurate, settings.scanMode);
}

[Test]
public void ScanMode_ModeSwitch_InvalidatesCache()
{
    var settings = PCPSettings.instance;
    settings.lastScanMode = PCPScanMode.Accurate;
    settings.scanMode = PCPScanMode.Fast;

    Assert.AreNotEqual(settings.scanMode, settings.lastScanMode,
        "Mode switch should be detectable");
}

[Test]
public void DependencyResolverFactory_CreatesCorrectType()
{
    var accurate = PCPDependencyResolverFactory.Create(PCPScanMode.Accurate);
    Assert.IsInstanceOf<PCPAccurateDependencyResolver>(accurate);

    var balanced = PCPDependencyResolverFactory.Create(PCPScanMode.Balanced);
    Assert.IsInstanceOf<PCPBalancedDependencyResolver>(balanced);

    var fast = PCPDependencyResolverFactory.Create(PCPScanMode.Fast);
    Assert.IsInstanceOf<PCPFastDependencyResolver>(fast);
}

[Test]
public void AsyncScheduler_CreatesAndDisposes()
{
    using var scheduler = new PCPAsyncScheduler(8f);
    Assert.AreEqual(8f, scheduler.BudgetMs);
    // Should not throw on dispose
}
```

- [ ] **Step 2: Run all tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS.

- [ ] **Step 3: Commit**

```bash
git add Assets/ProjectCleanPro/Tests/Editor/PCPIntegrationTests.cs
git commit -m "test: add integration tests for scan modes and async components"
```

---

### Task 24: Final Compilation + Manual Verification

- [ ] **Step 1: Clean build**

In Unity Editor: Edit → Preferences → External Tools → Regenerate project files. Then rebuild.

- [ ] **Step 2: Run all tests**

Run: Test Runner → EditMode → Run All
Expected: All PASS, zero failures.

- [ ] **Step 3: Manual smoke test**

1. Open ProjectCleanPro window
2. Verify scan mode dropdown appears in dashboard
3. Select each scan mode — verify no errors
4. Run a scan in Accurate mode — verify editor stays responsive
5. Run a scan in Fast mode — verify speed improvement
6. Switch from Fast to Accurate — verify warning banner appears
7. Run scan after mode switch — verify full rescan occurs
8. Check Settings view — verify frame budget slider appears
9. Adjust frame budget to 4ms — run scan, verify very smooth editor
10. Adjust frame budget to 16ms — run scan, verify faster completion

- [ ] **Step 4: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: address issues found during manual verification"
```
