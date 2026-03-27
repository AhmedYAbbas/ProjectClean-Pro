# Async Scan & Cache System Redesign

## Problem

The current scan system freezes the Unity Editor during scans. Although it uses `async/await`, all work runs on the main thread with cooperative yielding every 64 iterations via `EditorApplication.delayCall`. File hashing, dependency resolution, staleness checking, and cache I/O all block the main thread. On large projects (30K–100K+ assets) this makes the editor unresponsive or completely frozen for the duration of the scan.

## Goals

1. **Never freeze the editor** — scans should run with a configurable frame budget so the editor stays responsive
2. **Support all project sizes** — from 1K indie prototypes to 100K+ AAA projects
3. **Give users control** — three scan modes (Accurate, Balanced, Fast) trading accuracy for speed
4. **Support Unity 2021.2 through Unity 6.3** — with both `Task` and `Awaitable` patterns
5. **Zero breaking changes** to the public API

## Decision: Three Scan Modes

```csharp
public enum PCPScanMode
{
    Accurate,   // AssetDatabase for all deps, background I/O for everything else
    Balanced,   // Async GUID parsing + AssetDatabase for complex types only
    Fast        // Pure async file I/O, no AssetDatabase for dependency resolution
}
```

| Mode | Dependency Source | Threading | Accuracy |
|------|-------------------|-----------|----------|
| Accurate | `AssetDatabase.GetDependencies()` | Main thread (frame-budgeted) + background I/O | ~99%* |
| Balanced | GUID file parsing + AssetDatabase for `.prefab`/`.unity` | Background + main for subset | ~95% |
| Fast | Pure GUID parsing from file content | Fully background | ~80-85% |

**Why three modes:** `AssetDatabase` is the only fully accurate dependency source, but it's main-thread-only and slow. For large projects, even frame-budgeted main-thread work can make scans take minutes. Fast mode trades accuracy for speed — useful for quick iteration during development. Users choose what fits their workflow.

*\*Accurate mode is ~99% rather than 100% because it relies on cache staleness detection via file timestamps. In rare edge cases (e.g., an external tool modifying an asset without changing its timestamp), cached dependency edges may not be refreshed. A full rescan (clearing caches) always produces fully accurate results.*

**Mode switching:** Changing the scan mode invalidates all cached results (dependency graph, module results, staleness flags) and forces a full rescan. Timestamp and hash data in the scan cache is preserved since it's mode-independent.

---

## Architecture Overview

```
                    ┌─────────────────────────────────────────────────┐
                    │              PCPAsyncScheduler                  │
                    │  (central work coordinator)                     │
                    ├─────────────────────────────────────────────────┤
                    │                                                 │
                    │  ┌──────────────┐      ┌──────────────────┐     │
                    │  │ Background   │      │ Main Thread      │     │
                    │  │ Thread Pool  │─────>│ Frame-Budgeted   │     │
                    │  │              │      │ Queue            │     │
                    │  │ - File hash  │ push │                  │     │
                    │  │ - File I/O   │─────>│ - AssetDatabase  │     │
                    │  │ - GUID parse │      │   calls          │     │
                    │  │ - Cache I/O  │<─────│ - Results merge  │     │
                    │  │ - Staleness  │ pull │                  │     │
                    │  └──────────────┘      └──────────────────┘     │
                    │                                                 │
                    │  Frame Budget: configurable 4ms-16ms            │
                    │  Concurrency: Environment.ProcessorCount / 2    │
                    └─────────────────────────────────────────────────┘
```

### Core Principle

**Separate what _must_ run on the main thread from what _can_ run on background threads.**

Unity's `AssetDatabase` API is main-thread-only — this is an engine restriction we cannot bypass. But everything else (file reading, hashing, timestamp checks, cache serialization, GUID parsing, graph traversal) is pure computation or I/O that runs perfectly well on background threads.

The new system splits every scan operation into three phases:

```
Phase 1: GATHER (background)          Phase 2: QUERY (main, budgeted)     Phase 3: ANALYZE (background)
+-------------------------+           +-------------------------+         +-------------------------+
| - Read files            |           | - AssetDatabase calls   |         | - Build results         |
| - Hash content          |    -->    | - Frame-budgeted        |  -->    | - Sort/filter           |
| - Check staleness       |           | - Only for stale assets |         | - Apply ignore rules    |
| - Pre-filter candidates |           | - Skip in Fast mode     |         | - Serialize to cache    |
+-------------------------+           +-------------------------+         +-------------------------+
```

---

## Threading Concepts Explained

This section explains the threading and concurrency concepts used throughout the design. If you're new to multithreading, read this before the component sections.

### What is a thread?

A thread is like having a second person working alongside you. Your program normally runs on one thread (the "main thread") — it does one thing at a time, step by step. When you create additional threads, multiple pieces of code run simultaneously on different CPU cores.

**Unity's constraint:** Unity's editor API (`AssetDatabase`, `EditorGUILayout`, etc.) can only be called from the main thread. If you try to call `AssetDatabase.GetDependencies()` from a background thread, Unity throws an exception. This is why the current system runs everything on the main thread — it was the simplest way to use Unity's API. But it means the editor freezes because that one thread is busy doing scan work instead of drawing the UI.

### What is `Task.Run()`?

`Task.Run()` takes a piece of code and runs it on a background thread from .NET's thread pool. The thread pool is a set of pre-created threads managed by .NET — you don't create or destroy threads manually.

```csharp
// This runs on the main thread (blocks the editor):
var hash = ComputeSHA256(File.ReadAllBytes(path));

// This runs on a background thread (editor stays free):
var hash = await Task.Run(() => ComputeSHA256(File.ReadAllBytes(path)));
```

The `await` keyword means "start this work, give control back to the caller, and resume here when the work is done." The main thread is free to draw the UI while the background thread hashes the file.

### What is `Awaitable` (Unity 2023+)?

Unity 2023 introduced `Awaitable`, which is Unity's own version of `Task` that integrates better with Unity's frame loop. `Awaitable.BackgroundThreadAsync()` moves execution to a background thread, and `Awaitable.MainThreadAsync()` moves it back. On Unity 2021/2022, we use `Task.Run()` and `EditorApplication.delayCall` instead — same effect, slightly different mechanism. The `PCPThreading` abstraction hides this difference.

### What is a `SemaphoreSlim`?

A semaphore is a counter that limits how many threads can do something simultaneously. Think of it as a bouncer at a club with a maximum capacity.

```csharp
var semaphore = new SemaphoreSlim(4);  // allow 4 threads at once

// Each thread does this:
await semaphore.WaitAsync();  // "Can I enter?" — blocks if 4 are already inside
try { await DoWork(); }
finally { semaphore.Release(); }  // "I'm leaving" — lets the next thread in
```

We use this to limit concurrent file reads. Without it, trying to hash 100K files simultaneously would overwhelm the disk and use too much memory. With `ProcessorCount / 2` as the limit, we use half the CPU cores — enough for good parallelism while leaving headroom for Unity's own threads.

### What is a `ConcurrentDictionary`?

A normal `Dictionary<K,V>` is not safe to use from multiple threads simultaneously — if two threads try to add items at the same time, the internal data structure can get corrupted, causing crashes or lost data. `ConcurrentDictionary` is a version of Dictionary that handles this internally using fine-grained locks, so multiple threads can read and write safely.

```csharp
// UNSAFE — will eventually crash or lose data:
var dict = new Dictionary<string, string>();
Parallel.ForEach(files, f => dict[f] = Hash(f));  // multiple threads writing

// SAFE:
var dict = new ConcurrentDictionary<string, string>();
Parallel.ForEach(files, f => dict[f] = Hash(f));  // ConcurrentDictionary handles it
```

We use `ConcurrentDictionary` for the scan cache entries, hash results, GUID index, and anywhere multiple background threads need shared access.

### What is a `ConcurrentQueue`?

Same idea as `ConcurrentDictionary` but for a queue (first-in, first-out). We use it for:
- The main-thread work queue in `PCPAsyncScheduler` — background threads push work items, the main thread pops and executes them
- Module warnings — background threads enqueue warning messages, the main thread reads them after the scan

### What is `Interlocked`?

Even simple operations like `count++` are unsafe across threads. `count++` is actually three steps: read the value, add 1, write it back. If two threads do this simultaneously, one increment can be lost. `Interlocked.Increment(ref count)` does it as a single atomic (indivisible) operation.

```csharp
// UNSAFE:
m_ProcessedCount++;  // two threads could read the same value and both write value+1

// SAFE:
Interlocked.Increment(ref m_ProcessedCount);  // guaranteed correct across threads
```

We use this for progress counters — modules increment their processed count from background threads, and the UI reads it from the main thread.

### What are `Volatile.Read` and `Volatile.Write`?

When a variable is read on one thread and written on another, the CPU may cache the old value and never see the update. The `Volatile.Read(ref field)` and `Volatile.Write(ref field, value)` methods ensure the read/write goes to main memory, not a stale CPU cache.

**Important:** C# also has a `volatile` keyword you can put on a field declaration, but it has subtly different semantics from `Volatile.Read/Write` and can be misleading. We use the explicit `Volatile.Read`/`Volatile.Write` methods instead — they are clearer about intent and behave consistently across all CLR configurations.

```csharp
// Writing from a background thread:
Volatile.Write(ref m_ProgressLabel, "Scanning duplicates...");

// Reading from the UI thread:
var label = Volatile.Read(ref m_ProgressLabel);
```

We use this for the progress label string, which is written by scan threads and read by the UI thread on every frame.

### What is a `TaskCompletionSource`?

A `TaskCompletionSource<T>` creates a `Task` that you complete manually by calling `SetResult()`. It's a bridge between callback-based code and async/await code.

```csharp
// Bridge: Unity's callback → async/await
public static Task ReturnToMainThread()
{
    var tcs = new TaskCompletionSource<bool>();
    EditorApplication.delayCall += () => tcs.TrySetResult(true);
    return tcs.Task;
    // Anyone who "await"s this task will resume on the next editor frame
}
```

We use this in `PCPThreading.ReturnToMainThread()` on Unity 2021/2022 to yield back to the main thread. Also in `PCPAsyncScheduler.ScheduleMainThread()` to turn frame-budgeted work items into awaitable tasks.

### What is frame budgeting?

Instead of processing a fixed number of items per frame (current: 64), we process as many as fit within a time limit. A `Stopwatch` measures elapsed time, and we stop when we hit the budget.

```
Current (fixed count):
  Frame 1: [64 items ~~~~~~~~~~~~~~~~~~~~~~~~ 200ms] → editor frozen for 200ms
  Frame 2: [64 items ~~~~~~~~~~~~~~~~~~~~~~~~ 200ms] → frozen again

New (time budget = 8ms):
  Frame 1: [N items ~~ 8ms] [editor draws UI, responds to input: 8ms]
  Frame 2: [N items ~~ 8ms] [editor draws UI: 8ms]
```

At 60fps each frame is ~16.6ms. Using 8ms for scan work leaves ~8ms for the editor — smooth and responsive. The number of items processed adapts automatically: fast operations (like looking up a path) fit hundreds per frame, slow operations (like loading a prefab) might only fit one or two.

### What is `CancellationToken`?

A cooperative cancellation mechanism. You pass a `CancellationToken` to all async methods. When the user clicks "Cancel", the token is signaled, and every method that checks it throws `OperationCanceledException`, which unwinds the entire call stack cleanly.

```csharp
// The user's cancel button:
m_CancellationSource.Cancel();  // signals all tokens

// Deep inside a module:
await PCPThreading.ParallelForEachAsync(files, async (file, token) =>
{
    token.ThrowIfCancellationRequested();  // checks if cancelled
    var bytes = await File.ReadAllBytesAsync(file, token);  // also checks internally
    // ...
}, concurrency, ct);
```

Every async method in the system accepts and forwards a `CancellationToken`. This ensures cancellation reaches all background threads, not just the main thread.

---

## Component Details

### 1. PCPThreading — Concurrency Abstraction Layer

**Purpose:** Provides a single API surface for background/main-thread switching that works across Unity 2021–6.3. Module code never needs `#if UNITY_2023_1_OR_NEWER` — they call `PCPThreading` methods and get the right behavior.

**File:** `Core/PCPThreading.cs` (new)

```csharp
internal static class PCPThreading
{
    // Background execution — Task.Run on 2021/2022, Awaitable on 2023+
    // IMPORTANT: work() may internally switch threads (e.g., call ReturnToMainThread).
    // We wrap in Task.Run on both paths to guarantee the caller's continuation
    // resumes on a thread-pool thread, not wherever work() left off.
    #if UNITY_2023_1_OR_NEWER
    public static Task RunOnBackground(Func<Task> work, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            await work();
        }, ct);
    }
    #else
    public static Task RunOnBackground(Func<Task> work, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            await work();
        }, ct);
    }
    #endif

    // Return to main thread
    #if UNITY_2023_1_OR_NEWER
    public static async Task ReturnToMainThread()
    {
        await Awaitable.MainThreadAsync();
    }
    #else
    public static Task ReturnToMainThread()
    {
        var tcs = new TaskCompletionSource<bool>();
        EditorApplication.delayCall += () => tcs.TrySetResult(true);
        return tcs.Task;
    }
    #endif

    // Parallel batch processor with concurrency throttle
    // NOTE: The semaphore is disposed in a finally block AFTER Task.WhenAll completes,
    // not via "using var". This is critical — if Task.WhenAll throws (because a task
    // faulted), other tasks may still be running and calling semaphore.Release().
    // Disposing the semaphore while tasks hold it would cause ObjectDisposedException.
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
            // Wait for all tasks to settle before disposing the semaphore
            // Task.WhenAll may have thrown, but tasks are still running
            try { await Task.WhenAll(tasks); } catch { /* already observed */ }
            semaphore.Dispose();
        }
    }

    // Default concurrency — half the CPU cores, minimum 1
    public static int DefaultConcurrency => Math.Max(1, Environment.ProcessorCount / 2);
}
```

**Design decisions:**
- `ProcessorCount / 2` leaves headroom for Unity's own threads and the editor rendering
- `SemaphoreSlim` for throttling instead of `Parallel.ForEach` — gives us proper async support and cancellation
- `TaskCompletionSource + delayCall` for the 2021/2022 main-thread return path
- No `ConfigureAwait(false)` — Unity's `SynchronizationContext` handles thread affinity, and we control it explicitly through this abstraction

---

### 2. PCPAsyncScheduler — Central Work Coordinator

**Purpose:** Replaces `PCPEditorAsync`. Manages a frame-budgeted main-thread queue and background task dispatching. Created per-scan, disposed when scan completes or is cancelled.

**File:** `Core/PCPAsyncScheduler.cs` (new, replaces `Core/PCPEditorAsync.cs`)

```csharp
internal class PCPAsyncScheduler : IDisposable
{
    readonly ConcurrentQueue<MainThreadWorkItem> m_MainQueue = new();
    readonly Stopwatch m_FrameStopwatch = new();
    float m_BudgetMs;
    bool m_Registered;
    int m_PendingBackgroundTasks;  // atomic counter
    int m_CompletedItems;          // atomic counter for progress

    public PCPAsyncScheduler(float budgetMs)
    {
        m_BudgetMs = budgetMs;
        EditorApplication.update += DrainMainQueue;
        m_Registered = true;
    }

    // Queues work to run on a background thread
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
                // Marshal exception logging to main thread (Debug.Log is main-thread-only)
                ScheduleMainThread(() => { Debug.LogException(ex); return 0; },
                    CancellationToken.None);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref m_PendingBackgroundTasks);
            }
        }, ct);
    }

    // Enqueues a small unit of work to run on main thread within frame budget
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

    // Called every editor frame — processes queued main-thread work within budget
    void DrainMainQueue()
    {
        m_FrameStopwatch.Restart();
        while (m_FrameStopwatch.Elapsed.TotalMilliseconds < m_BudgetMs
               && m_MainQueue.TryDequeue(out var item))
        {
            item.Execute();
            Interlocked.Increment(ref m_CompletedItems);
        }
    }

    // Processes a list on main thread, frame-budgeted.
    // IMPORTANT: All items are enqueued upfront so DrainMainQueue can process
    // multiple items per frame within the budget. If we awaited each item before
    // enqueuing the next, only one item would be processed per frame — defeating
    // the entire frame budget system.
    public async Task<List<TResult>> BatchOnMainThread<TItem, TResult>(
        IReadOnlyList<TItem> items,
        Func<TItem, TResult> work,
        CancellationToken ct)
    {
        // Enqueue ALL items at once — DrainMainQueue will process as many as
        // fit within the frame budget per frame
        var tasks = new List<Task<TResult>>(items.Count);
        foreach (var item in items)
            tasks.Add(ScheduleMainThread(() => work(item), ct));

        // Wait for all to complete (spread across multiple frames by DrainMainQueue)
        await Task.WhenAll(tasks);

        // Collect results in original order
        var results = new List<TResult>(items.Count);
        foreach (var task in tasks)
            results.Add(task.Result);
        return results;
    }

    // Debug assertion — catches accidental AssetDatabase calls from background threads
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

    public float BudgetMs { get => m_BudgetMs; set => m_BudgetMs = value; }
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
```

**How this solves the editor freezing:**

Current system:
```
Frame 1: [process 64 items ~~~~~~~~~~~~~~~~~~~~~~~~ 200ms] [yield]
Frame 2: [process 64 items ~~~~~~~~~~~~~~~~~~~~~~~~ 200ms] [yield]
--> Editor stutters, feels frozen
```

New system:
```
Frame 1: [process N items ~~ 8ms] [rest of frame for editor: 8ms]
Frame 2: [process N items ~~ 8ms] [rest of frame for editor: 8ms]
--> Editor stays smooth, scan takes longer wall-clock but never blocks
```

The number of items processed per frame adapts automatically — fast operations fit more into 8ms, slow operations fit fewer. No magic numbers to tune per operation type.

---

### 3. PCPGuidIndex — GUID-to-Path Map

**Purpose:** Builds a map of every GUID in the project to its asset path by reading `.meta` files on background threads. Powers the Fast and Balanced scan modes. Not used in Accurate mode.

**File:** `Core/PCPGuidIndex.cs` (new)

Every asset in Unity has a companion `.meta` file containing a GUID:
```
Assets/Textures/hero.png.meta  -->  contains "guid: a1b2c3d4e5f6..."
```

PCPGuidIndex reads all `.meta` files in parallel and builds:
```
Dictionary<"a1b2c3d4e5f6...", "Assets/Textures/hero.png">
```

**How it works:**
1. Collect all `.meta` files under `Assets/` and `Packages/` via `System.IO`
2. Distribute across background threads via `PCPThreading.ParallelForEachAsync`
3. Each thread reads only the first 2–3 lines of each `.meta` file (the `guid:` line is always near the top — fixed format, no regex needed)
4. Store in `ConcurrentDictionary<string, string>`
5. Cache to `Library/ProjectCleanPro/GuidIndex.bin` for fast reload
6. Incremental updates: on subsequent scans, only re-read `.meta` files whose timestamp changed

```csharp
internal class PCPGuidIndex
{
    readonly ConcurrentDictionary<string, string> m_GuidToPath = new();

    // Reverse map: path → guid, needed for pruning deleted entries
    readonly ConcurrentDictionary<string, string> m_PathToGuid = new();

    public async Task BuildAsync(IReadOnlyList<string> metaFiles,
        IReadOnlySet<string> changedFiles, CancellationToken ct)
    {
        if (changedFiles != null)
        {
            // Incremental: first remove entries for deleted .meta files
            // (files that were in our index but are no longer in the metaFiles list)
            var currentMetaSet = new HashSet<string>(metaFiles);
            foreach (var (path, guid) in m_PathToGuid)
            {
                if (!currentMetaSet.Contains(path + ".meta"))
                {
                    m_PathToGuid.TryRemove(path, out _);
                    m_GuidToPath.TryRemove(guid, out _);
                }
            }
        }

        var toProcess = changedFiles == null
            ? metaFiles
            : metaFiles.Where(f => changedFiles.Contains(f)).ToList();

        await PCPThreading.ParallelForEachAsync(toProcess, async (metaPath, token) =>
        {
            var guid = await ReadGuidFromMetaAsync(metaPath, token);
            if (guid != null)
            {
                var assetPath = metaPath.Substring(0, metaPath.Length - 5);
                m_GuidToPath[guid] = assetPath;
                m_PathToGuid[assetPath] = guid;
            }
        }, PCPThreading.DefaultConcurrency, ct);
    }

    public string Resolve(string guid) =>
        m_GuidToPath.TryGetValue(guid, out var path) ? path : null;

    public HashSet<string> ResolveAll(IEnumerable<string> guids) =>
        guids.Select(Resolve).Where(p => p != null).ToHashSet();
}
```

**Performance:** Reading the first 2 lines of 100K `.meta` files takes ~2–3 seconds on an SSD with parallel I/O, vs. `AssetDatabase.GUIDToAssetPath()` which must run on the main thread one-by-one.

---

### 4. PCPGuidParser — GUID Reference Extractor

**Purpose:** Extracts all GUID references from Unity YAML asset files. Used by Fast and Balanced modes to find dependencies without calling `AssetDatabase`.

**File:** `Core/PCPGuidParser.cs` (new)

Unity prefab/material/scene files contain references like:
```yaml
m_Script: {fileID: 11500000, guid: e4f18583b7a683c4b9db3b1f46a8b93a, type: 3}
m_Material: {fileID: 2100000, guid: c22c5a2f3fa1e0947a1e82e283a6b70c, type: 2}
```

The parser reads the file and returns:
```
HashSet { "e4f18583b7a683c4b9db3b1f46a8b93a", "c22c5a2f3fa1e0947a1e82e283a6b70c" }
```

Uses `string.IndexOf` in a loop (faster than regex for large files). Skips binary assets (`.png`, `.fbx`, `.wav`) since they don't contain text GUID references.

**What GUID parsing misses (why Fast mode is ~80-85% accurate):**
- Implicit dependencies (e.g., `Shader.Find("Custom/MyShader")` by string name)
- Script-to-asset runtime bindings (`Resources.Load("path")`)
- Built-in asset references (fileID references without GUIDs)
- Prefab variant inheritance (`m_Modifications` references to base prefab)
- Assembly-level dependencies (`using` statements)

```csharp
internal static class PCPGuidParser
{
    const string k_GuidPrefix = "guid: ";

    // Uses StreamReader to read line-by-line instead of loading the entire file
    // into memory. This is critical for .unity scene files which can be hundreds
    // of megabytes on large projects. Line-by-line streaming keeps memory usage
    // constant regardless of file size.
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

            int searchFrom = 0;
            while (true)
            {
                int idx = line.IndexOf(k_GuidPrefix, searchFrom, StringComparison.Ordinal);
                if (idx < 0) break;

                int guidStart = idx + k_GuidPrefix.Length;
                if (guidStart + 32 <= line.Length)
                {
                    var candidate = line.Substring(guidStart, 32);
                    if (IsHexString(candidate))
                        guids.Add(candidate);
                }
                searchFrom = guidStart + 32;
            }
        }
        return guids;
    }

    public static bool IsGuidParseable(string path)
    {
        var ext = Path.GetExtension(path);
        return ext is ".prefab" or ".unity" or ".asset" or ".mat"
            or ".controller" or ".anim" or ".overrideController"
            or ".lighting" or ".playable" or ".signal"
            or ".spriteatlasv2" or ".spriteatlas" or ".terrainlayer"
            or ".mixer" or ".renderTexture" or ".flare";
    }
}
```

---

### 5. IPCPDependencyResolver — Strategy Pattern

**Purpose:** Replaces the single concrete `PCPDependencyResolver` with an interface and three implementations, one per scan mode. All share the same graph data structure and query API.

**File:** `Core/PCPDependencyResolver.cs` (rewrite)

```csharp
internal interface IPCPDependencyResolver
{
    Task BuildGraphAsync(PCPScanContext context, CancellationToken ct);
    IReadOnlySet<string> GetReachableAssets();
    int GetDependentCount(string path);
    IReadOnlyCollection<string> GetDependencies(string path);
    void SaveToDisk(string path);
    bool LoadFromDisk(string path);
}
```

**Shared base class** holds the graph structure (`ConcurrentDictionary` for forward/reverse edges) and implements BFS reachability, query methods, and serialization. Each mode overrides `BuildGraphAsync`:

#### Accurate Resolver
1. Remove edges for deleted assets (background)
2. For each stale asset, `AssetDatabase.GetDependencies()` on main thread (frame-budgeted via scheduler)
3. Update forward + reverse maps (background)
4. BFS reachability from roots (background)
5. Save to disk (background)

Only step 2 touches the main thread.

#### Fast Resolver
1. Build GUID index from `.meta` files (background, parallel)
2. For each stale parseable asset, extract GUID references (background, parallel)
3. Resolve GUIDs to paths via index (background)
4. Build forward + reverse maps (background)
5. BFS reachability (background)

Fully background — no main-thread work at all.

#### Balanced Resolver
1. Build GUID index (background)
2. Classify stale assets: "simple" (`.mat`, `.asset`, `.controller`, etc.) vs. "complex" (`.prefab`, `.unity`)
3. Run in parallel:
   - Simple assets: GUID parse on background threads
   - Complex assets: `AssetDatabase.GetDependencies()` on main thread (frame-budgeted)
4. Merge both result sets into unified graph
5. BFS reachability (background)

The simple and complex tasks run concurrently — background threads parse files while the main thread handles AssetDatabase calls. True parallelism.

**Factory:**
```csharp
internal static class PCPDependencyResolverFactory
{
    public static IPCPDependencyResolver Create(PCPScanMode mode) => mode switch
    {
        PCPScanMode.Accurate => new PCPAccurateDependencyResolver(),
        PCPScanMode.Balanced => new PCPBalancedDependencyResolver(new PCPGuidIndex()),
        PCPScanMode.Fast     => new PCPFastDependencyResolver(new PCPGuidIndex()),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
```

---

### 6. Module Scan Flow — Three-Phase Pattern

Every module follows the gather/query/analyze pattern. The two heaviest modules transform as follows:

#### PCPDuplicateDetector

Currently the worst offender — hashes every file on the main thread.

**Phase 1 — GATHER (background, parallel):**
- Check staleness for each candidate (timestamp via `System.IO`)
- For stale files: `File.ReadAllBytesAsync()` + SHA-256 hash in parallel
- For non-stale files: pull hash from cache
- For YAML assets: normalize content before hashing (strip fileID lines)

**Phase 2 — QUERY (main thread, frame-budgeted, skipped in Fast mode):**
- Group by hash to find potential duplicate groups
- If import settings comparison enabled: read importer settings via `EditorJsonUtility` (frame-budgeted)
- Subdivide groups by importer settings

**Phase 3 — ANALYZE (background):**
- Elect canonical per group (highest ref count from dependency resolver)
- Compute wasted bytes, sort, apply ignore rules, serialize

**Performance impact:** File hashing goes from ~45s (main thread, sequential on 50K assets) to ~3–5s (8 background threads). The editor never freezes.

#### PCPUnusedScanner

**Phase 1 — GATHER (background):**
- Collect all asset paths via `System.IO.Directory.EnumerateFiles` (faster than `AssetDatabase.GetAllAssetPaths()`, runs on background)
- Filter by excluded extensions, check staleness

**Phase 2 — QUERY (main thread, frame-budgeted, skipped in Fast mode):**
- Verify ambiguous asset paths via `AssetDatabase`

**Phase 3 — ANALYZE (background):**
- Cross-reference against reachable set from dependency resolver
- Categorize, apply ignore rules, sort by size

#### PCPMissingRefScanner

**Phase 1 — GATHER (background):**
- Read `.prefab`, `.unity`, `.asset` files as text on background threads
- Pre-filter: search for `fileID: 0` or `guid: 0000000` patterns
- Skip files with no suspicious patterns (typically 85–95% of candidates)

**Phase 2 — QUERY (main thread, frame-budgeted):**
- Only for suspicious files from Phase 1
- `LoadAllAssetsAtPath()` + inspect SerializedObjects (frame-budgeted)

**Phase 3 — ANALYZE (background):**
- Build results, cache clean files, serialize

#### Module Base Class

```csharp
internal abstract class PCPModuleBase : IPCPModule
{
    protected int m_ProcessedCount;
    protected int m_TotalCount;
    protected readonly ConcurrentQueue<string> m_Warnings = new();

    // Thread-safe progress via atomic counter
    public float Progress => m_TotalCount == 0 ? 0f :
        (float)Interlocked.CompareExchange(ref m_ProcessedCount, 0, 0) / m_TotalCount;

    public IReadOnlyList<string> Warnings => m_Warnings.ToArray();
    public int WarningCount => m_Warnings.Count;
}
```

---

### 7. PCPScanCache — Thread-Safe with Background I/O

**File:** `Core/PCPScanCache.cs` (rewrite)

**Key changes:**
- All internal collections become `ConcurrentDictionary` (thread-safe)
- Staleness computation moves to background threads via `System.IO`
- Uses `Directory.EnumerateFiles` instead of `AssetDatabase.GetAllAssetPaths()` — faster and thread-safe
- Disk persistence (load/save) runs on background threads
- Stamping after scan runs on background threads

**Staleness computation:**
- **Full check (domain reload):** Distributes all assets across background threads, each compares `File.GetLastWriteTimeUtc` against cached timestamp. O(N) but parallelized across cores.
- **Incremental:** Only checks assets tracked by `PCPAssetChangeTracker`. O(changed).
- **No changes:** Instant O(1) return.

**Thread-safe accessors:**
- `IsStale(path)`, `GetHash(path)`, `SetHash(path, hash)`, `GetMetadata(path, key)`, `SetMetadata(path, key, value)` — all safe to call from any thread via `ConcurrentDictionary` operations.

**Async disk persistence:**
- `SaveToDiskAsync`: Snapshots the `ConcurrentDictionary` to array, serializes to `MemoryStream`, writes via atomic temp-file-then-rename pattern. All on background thread.
- `LoadFromDiskAsync`: Reads bytes via `File.ReadAllBytes`, deserializes, populates dictionary. All on background thread.

---

### 8. PCPScanOrchestrator — Revised

**File:** `Core/PCPScanOrchestrator.cs` (rewrite)

**Changes from current:**
1. Creates `PCPAsyncScheduler` per scan with user's frame budget
2. Picks dependency resolver via factory based on scan mode
3. Handles mode-switch cache invalidation
4. Runs independent modules in parallel (background phases overlap)
5. Aggregates progress from atomic counters

**Mode-switch invalidation:**
When `settings.scanMode != settings.lastScanMode`:
- Clear dependency graph cache
- Clear all module results
- Mark all modules dirty
- Keep timestamp/hash data (mode-independent)
- Update `lastScanMode`

**Module parallelization:**
After the dependency graph is built, modules split into two groups that run concurrently:
- **Graph-dependent:** Unused, Dependencies, Duplicates — read from shared graph (read-only)
- **Independent:** Missing Refs, Shader Analyzer, Size Profiler, Package Auditor

In Fast mode, all modules can run fully parallel since there's no main-thread contention. In Accurate/Balanced, background phases overlap while main-thread work queues up naturally through the scheduler.

**Progress:** Each module reports its own 0-1 progress via atomic counter. Orchestrator aggregates: `overallProgress = modules.Sum(m => m.Progress) / modules.Count`. UI polls on each frame — cheap, lock-free.

---

### 9. PCPScanContext — Updated

**File:** `Core/PCPScanContext.cs` (modify)

**New properties:**
- `PCPAsyncScheduler Scheduler` — the scan's work coordinator
- `IPCPDependencyResolver DependencyResolver` — interface instead of concrete class
- `PCPGuidIndex GuidIndex` — shared GUID index, null in Accurate mode

**Changed:**
- `AllProjectAssets` becomes `GetAllProjectAssetsAsync(ct)` — computed on background via `System.IO`, cached for session
- New `GetAllMetaFilesAsync(ct)` — for GUID index building
- `FinalizeScanAsync(ct)` — stamps and saves on background threads
- `ReportProgress` — thread-safe via `Interlocked.Exchange` and `Volatile.Write`

---

### 10. Settings Additions

**File:** `Core/PCPSettings.cs` (modify)

```csharp
public PCPScanMode scanMode = PCPScanMode.Accurate;       // Default: safest option
public float mainThreadBudgetMs = 8f;                      // Slider: 4-16ms
internal PCPScanMode lastScanMode = PCPScanMode.Accurate;  // Not user-visible
```

Unity's `ScriptableSingleton` handles missing fields gracefully — new fields get their default values.

---

### 11. UI Changes

#### PCPDashboardView (modify)

**Scan mode dropdown** at the top of the dashboard near the Scan button:
- `EditorGUILayout.EnumPopup("Scan Mode", settings.scanMode)`

**Warning banner** (conditional):
- Shows when `scanMode != lastScanMode` AND cached results exist
- Text: "Scan mode changed — cached results will be cleared and a full rescan is required."
- Uses `EditorGUILayout.HelpBox(..., MessageType.Warning)`
- Disappears after scan completes (because `lastScanMode` gets updated)

**Fast mode label** (conditional):
- Shows whenever Fast mode is selected
- Text: "Fast scan — some dependencies may not be detected."
- Uses `EditorGUILayout.HelpBox(..., MessageType.Info)`

**Warnings section** (after scan):
- Collapsed by default: "N files could not be scanned"
- Expands to show per-file path + error message
- Per-module result views show "N files skipped during scan" with tooltip

#### PCPSettingsView (modify)

**Frame budget slider** in Performance section:
- `EditorGUILayout.Slider("Main Thread Budget (ms)", settings.mainThreadBudgetMs, 4f, 16f)`
- Tooltip: "Lower = smoother editor, slower scans. Higher = faster scans, editor may stutter."

---

### 12. Error Handling & Cancellation

**Cancellation flow:**
User clicks Cancel → `CancellationTokenSource.Cancel()` → all background tasks check `ct.ThrowIfCancellationRequested()` → `OperationCanceledException` unwinds → orchestrator catches it → does NOT stamp or save (partial scan = invalid) → keeps cache intact → **disposes the scheduler** (critical — unregisters from `EditorApplication.update`) → reports "Scan cancelled"

The orchestrator MUST use `using var scheduler = new PCPAsyncScheduler(...)` or a `try/finally` block to guarantee the scheduler is disposed on both success and cancellation paths. Without this, the `DrainMainQueue` handler remains registered on `EditorApplication.update` indefinitely, running pointlessly on every editor frame.

**Per-asset resilience:**
Individual file failures (permission denied, file deleted mid-read, corrupt data) are caught at the item level, logged to `ConcurrentQueue<string> m_Warnings`, and the scan continues. Warnings surface in the UI after scan completion.

**Thread safety assertion:**
`PCPAsyncScheduler.AssertMainThread(operation)` — debug-only check that throws if Unity API is called from a background thread. Catches threading bugs during development.

**Domain reload safety:**
If Unity domain-reloads mid-scan (user edits a script), all background threads die. Since we only persist on successful completion, the cache stays valid from the previous scan. `[InitializeOnLoadMethod]` resets the "scan in progress" flag.

---

## File Changes Summary

| File | Action | Purpose |
|------|--------|---------|
| `Core/PCPAsyncScheduler.cs` | **Create** | Central thread coordinator with frame-budgeted main-thread queue |
| `Core/PCPThreading.cs` | **Create** | Abstraction over Task.Run / Awaitable for Unity 2021-6.3 |
| `Core/PCPGuidIndex.cs` | **Create** | GUID-to-path map from .meta files for Fast/Balanced modes |
| `Core/PCPGuidParser.cs` | **Create** | Extracts GUID references from YAML asset files |
| `Core/PCPDependencyResolver.cs` | **Rewrite** | Interface + 3 implementations (Accurate/Balanced/Fast) |
| `Core/PCPScanOrchestrator.cs` | **Rewrite** | Scheduler integration, parallel modules, mode switching |
| `Core/PCPEditorAsync.cs` | **Delete** | Replaced by PCPAsyncScheduler |
| `Core/PCPScanCache.cs` | **Rewrite** | Thread-safe collections, async I/O, background staleness |
| `Core/PCPScanContext.cs` | **Modify** | New scheduler/resolver properties, async asset listing |
| `Core/PCPSettings.cs` | **Modify** | Add scanMode, mainThreadBudgetMs, lastScanMode |
| `Modules/PCPModuleBase.cs` | **Modify** | Warnings queue, atomic progress counter |
| `Modules/PCPUnusedScanner.cs` | **Rewrite** | Three-phase pattern with background file enumeration |
| `Modules/PCPDuplicateDetector.cs` | **Rewrite** | Background parallel hashing |
| `Modules/PCPMissingRefScanner.cs` | **Modify** | Background pre-filter for suspicious files |
| `Modules/PCPDependencyModule.cs` | **Modify** | Use resolver interface |
| `Modules/PCPShaderAnalyzer.cs` | **Minor** | Use scheduler for yielding |
| `Modules/PCPSizeProfiler.cs` | **Minor** | Use scheduler for yielding |
| `Modules/PCPPackageAuditor.cs` | **Minor** | Use scheduler for yielding |
| `Data/PCPScanManifest.cs` | **Modify** | Add warnings list |
| `UI/PCPDashboardView.cs` | **Modify** | Scan mode selector, warning banner, fast-mode label, warnings section |
| `UI/PCPSettingsView.cs` | **Modify** | Frame budget slider |
| `API/PCPAPI.cs` | **Modify** | Pass scan mode through, additive ScanMode option |
