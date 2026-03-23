# ProjectCleanPro: Scan & Cache System

This document describes the architecture, data flow, and optimization strategies
of the scanning and caching system that powers every analysis module in
ProjectCleanPro.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Service Registry (PCPContext)](#2-service-registry-pcpcontext)
3. [Asset Change Tracking (PCPAssetChangeTracker)](#3-asset-change-tracking-pcpassetchangetracker)
4. [Scan Cache (PCPScanCache)](#4-scan-cache-pcpscancache)
5. [Staleness Computation](#5-staleness-computation)
6. [Module-Level Dirty Tracking](#6-module-level-dirty-tracking)
7. [Scan Context (PCPScanContext)](#7-scan-context-pcpscancontext)
8. [Dependency Graph (PCPDependencyResolver)](#8-dependency-graph-pcpdependencyresolver)
9. [Result Cache (PCPResultCache)](#9-result-cache-pcpresultcache)
10. [Full Scan Flow (ScanAll)](#10-full-scan-flow-scanall)
11. [Individual Module Scan Flow](#11-individual-module-scan-flow)
12. [Smart Delete Flow](#12-smart-delete-flow)
13. [Binary Persistence Formats](#13-binary-persistence-formats)
14. [Lifecycle & Persistence Chain](#14-lifecycle--persistence-chain)
15. [Public Scripting API (PCPAPI)](#15-public-scripting-api-pcpapi)
16. [Performance Characteristics](#16-performance-characteristics)

---

## 1. Architecture Overview

The system is built around a layered architecture where each layer provides
services to the one above it:

```
 UI Layer           PCPWindow, PCPModuleView, per-module views
                         |
 Orchestration      PCPAPI.RunScan(), PCPWindow.ScanAll()
                         |
 Session            PCPScanContext  (bundles all deps for one scan)
                         |
 Core Services      PCPScanCache           PCPDependencyResolver
                    PCPAssetChangeTracker   PCPResultCache
                         |
 Registry           PCPContext  (lazy singleton access to all services)
```

### Key Files

| File | Location | Purpose |
|------|----------|---------|
| `PCPContext.cs` | `Editor/Core/` | Lazy-initialized singleton registry for all services |
| `PCPAssetChangeTracker.cs` | `Editor/Core/` | Unity AssetPostprocessor tracking file changes |
| `PCPScanCache.cs` | `Editor/Core/` | Per-asset incremental cache with binary persistence |
| `PCPScanContext.cs` | `Editor/Core/` | Session container bundling all scan dependencies |
| `PCPDependencyResolver.cs` | `Editor/Core/` | Forward/reverse dependency graph with BFS reachability |
| `PCPResultCache.cs` | `Editor/Core/` | Persists scan results to disk across domain reloads |
| `PCPAPI.cs` | `Editor/API/` | Public scripting API for CI and external tools |
| `PCPWindow.cs` | `Editor/UI/` | Main editor window with ScanAll orchestration |
| `PCPModuleView.cs` | `Editor/UI/Views/` | Abstract base class for module-specific views |

### Disk Layout

All persistent data lives under `Library/ProjectCleanPro/`:

```
Library/ProjectCleanPro/
    ScanCache.bin           Binary scan cache (per-asset timestamps, hashes, deps, metadata)
    DepGraph.bin            Binary dependency graph (forward adjacency list)
    Results/
        ScanResult.json     Last full scan result (survives domain reload)
```

---

## 2. Service Registry (PCPContext)

`PCPContext` is a static class providing lazy-initialized access to every core
service. Properties are created on first access and survive across window
close/reopen within the same editor session.

```
PCPContext
    .Settings               PCPSettings (ScriptableSingleton)
    .DependencyResolver     PCPDependencyResolver (loads graph from disk on creation)
    .ScanCache              PCPScanCache (loads binary cache from disk on creation)
    .IgnoreRules            PCPIgnoreRules
    .RenderPipelineDetector PCPRenderPipelineDetector
    .LastScanResult         PCPScanResult (static, preserved across window reopen)
```

### Initialization

```csharp
PCPContext.Initialize();
// Touches each property to force lazy init.
// DependencyResolver.LoadFromDisk() runs automatically.
// ScanCache.Load() runs automatically.
```

### Persistence

- `SaveCache()` - Writes scan cache to disk without tearing down services.
  Called on window close (`PCPWindow.OnDisable`).
- `Dispose()` - Saves cache, saves dependency graph, nulls all references.
  Called only on domain unload.

---

## 3. Asset Change Tracking (PCPAssetChangeTracker)

An `AssetPostprocessor` that listens to Unity's import pipeline and maintains
a set of changed asset paths since the last scan.

### State

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| `s_FullCheckNeeded` | `bool` | `true` | Set after domain reload; forces full timestamp comparison |
| `s_ChangedAssets` | `HashSet<string>` | empty | Paths changed since last `Reset()` |

### How It Works

1. **Domain Reload**: `s_FullCheckNeeded = true` (C# static initializer).
   The tracker cannot know what changed while the domain was unloaded, so the
   next scan must do a full timestamp check.

2. **Asset Import/Delete/Move**: Unity calls `OnPostprocessAllAssets()`.
   All affected paths are added to `s_ChangedAssets`.

3. **Scan Starts**: `PCPScanCache.RefreshStaleness()` reads the tracker state:
   - `HasChanges == false` and cache exists -> instant skip (O(1))
   - `FullCheckNeeded == true` -> full timestamp check (O(N))
   - `ChangedAssets` has items -> check only those paths (O(changed))

4. **Scan Ends**: `PCPAssetChangeTracker.Reset()` clears `s_ChangedAssets`
   and sets `s_FullCheckNeeded = false`. The next scan with no intervening
   changes will hit the instant O(1) path.

### State Machine

```
[Domain Reload]
    s_FullCheckNeeded = true
    s_ChangedAssets = empty
         |
         v
[Asset changes happen]
    s_ChangedAssets += {path1, path2, ...}
         |
         v
[Scan runs]
    RefreshStaleness reads FullCheckNeeded + ChangedAssets
         |
         v
[Scan finishes]
    Reset() -> s_FullCheckNeeded = false, s_ChangedAssets.Clear()
         |
         v
[More asset changes]
    s_ChangedAssets += {path3, ...}
         |
         v
[Next scan]
    RefreshStaleness sees FullCheckNeeded=false, ChangedAssets has items
    -> incremental check (O(changed))
         |
         v
[Next scan with no changes]
    RefreshStaleness sees HasChanges=false, cache exists
    -> instant O(1) return, no modules run
```

---

## 4. Scan Cache (PCPScanCache)

The scan cache is the central persistence layer. It stores per-asset data that
modules produce during scans so that subsequent scans can skip unchanged assets.

### What Is Cached Per Asset

Each `CacheEntry` stores:

| Field | Type | Used By |
|-------|------|---------|
| `assetPath` | `string` | Key |
| `lastModifiedTicks` | `long` | Staleness detection (file write time) |
| `sha256Hash` | `string` | Duplicate detection |
| `fileSizeBytes` | `long` | Size profiler, duplicate pre-filter |
| `dependencies` | `string[]` | Dependency graph (avoids GetDependencies calls) |
| `metadata` | `List<MetadataPair>` | Module-specific key-value pairs |

### Module Metadata Keys

Modules store their own data using the generic metadata system:

| Key | Module | Value |
|-----|--------|-------|
| `cache.metaTicks` | Cache system | .meta file timestamp (import settings) |
| `missing.count` | MissingRefScanner | Number of missing refs (skip if "0" and not stale) |
| `mat.shader` | ShaderAnalyzer | Shader name for each material |
| `shader.keywords` | ShaderAnalyzer | Cached keyword list |
| `shader.passCount` | ShaderAnalyzer | Cached pass count |
| `shader.variants` | ShaderAnalyzer | Estimated variant count |
| `size.type` | SizeProfiler | Asset type name |
| `size.suggestions` | SizeProfiler | Cached compression/optimization suggestions |

### Dirty Flag

A `bool m_Dirty` flag tracks whether the cache has been mutated since the last
`Load()` or `Save()`. Every mutating method (`SetHash`, `SetFileSize`,
`SetDependencies`, `SetMetadata`, `RemoveEntry`, etc.) sets `m_Dirty = true`.

`Save()` is a no-op when `!m_Dirty`, preventing unnecessary disk writes when
a scan is skipped due to no changes.

---

## 5. Staleness Computation

Staleness computation is the first thing that happens in every scan. It answers:
"which assets have changed since the last scan?"

### RefreshStaleness(string[] currentAssetPaths)

Three code paths, selected based on tracker state:

#### Path 1: Fast Path (O(1))

```
Condition: !PCPAssetChangeTracker.HasChanges && m_Entries.Count > 0
Result:    m_StaleAssets = empty, m_DirtyModules = empty
Effect:    Entire scan can be skipped
```

Nothing has changed since the last scan completed and reset the tracker.
Both `m_StaleAssets` and `m_DirtyModules` are initialized to empty sets,
meaning `HasAnyChanges` returns false and `IsModuleDirty()` returns false
for every module.

#### Path 2: Full Check (O(N))

```
Condition: PCPAssetChangeTracker.FullCheckNeeded || m_Entries.Count == 0
Result:    Compares every cached entry's timestamp against the filesystem
```

Runs after domain reload or on first-ever scan. For each cached entry:

1. If the asset no longer exists in the project -> mark as deleted, remove entry
2. If `File.GetLastWriteTimeUtc(path).Ticks != entry.lastModifiedTicks` -> stale
3. If the asset is not stale but its `.meta` file timestamp changed -> meta-stale

New assets (present in `currentAssetPaths` but absent from cache) are added to
both `m_StaleAssets` and `m_StaleOrMetaStaleAssets`.

After the full check, `ComputeModuleDirtiness()` runs to determine which
modules are affected.

#### Path 3: Incremental (O(changed))

```
Condition: PCPAssetChangeTracker.ChangedAssets has items
Result:    Only checks the tracked paths
```

For each path in `ChangedAssets`:
- If still in project -> add to `m_StaleAssets`
- If no longer in project -> remove from cache, mark dirty

### After Computation

Once `RefreshStaleness` completes:
- `IsStale(path)` and `IsStaleOrMetaStale(path)` are O(1) HashSet lookups
- `HasAnyChanges` tells whether any assets changed
- `IsModuleDirty(moduleId)` tells whether a specific module needs re-running
- `StaleCount` gives the number of changed assets

---

## 6. Module-Level Dirty Tracking

After staleness computation, `ComputeModuleDirtiness()` determines which
modules need to run based on the file types that changed.

### Rules

| Module | Dirty When |
|--------|-----------|
| `unused` | Any asset changed (unconditional) |
| `dependencies` | Any asset changed (unconditional) |
| `size` | Any asset changed (unconditional) |
| `duplicates` | Any asset changed (unconditional) |
| `missing` | A `.prefab`, `.unity`, or `.asset` file changed |
| `shaders` | A `.shader`, `.mat`, `.shadergraph`, `.shadersubgraph`, or `.compute` file changed |
| `packages` | Never dirty from file changes (package changes are handled externally) |

### How It Is Used

**In ScanAll:**
```
for each module:
    if (!cache.IsModuleDirty(module.ModuleId))
        continue;  // Skip module, preserve existing results

    ClearModuleResults(moduleId);
    module.Scan(context);
    CollectModuleResults(module);
```

**In individual module scans:**
```
context.EnsureStaleness();
if (!context.Cache.HasAnyChanges && HasModuleResults())
    return;  // Skip entirely
```

### Example Scenario

User modifies a `.shader` file, then clicks Scan All:

1. `RefreshStaleness` -> 1 stale asset (the shader)
2. `ComputeModuleDirtiness`:
   - `unused`, `dependencies`, `size`, `duplicates` -> dirty (unconditional)
   - `shaders` -> dirty (`.shader` extension match)
   - `missing` -> NOT dirty (no prefab/scene/asset changed)
   - `packages` -> NOT dirty
3. ScanAll skips `missing` and `packages`, runs the other 5 modules
4. `missing` and `packages` results are preserved from the previous scan

---

## 7. Scan Context (PCPScanContext)

A session container created fresh for each scan operation. Bundles all
dependencies so modules get consistent access to shared services.

### What It Provides

| Property | Source | Purpose |
|----------|--------|---------|
| `Settings` | PCPContext | Scan scope, ignore rules, delete settings |
| `IgnoreRules` | PCPContext | Path/extension filtering |
| `DependencyResolver` | PCPContext | Shared dependency graph |
| `Cache` | PCPContext | Incremental per-asset cache |
| `RenderPipeline` | PCPContext | BuiltIn/URP/HDRP detection |
| `AlwaysUsedRoots` | Settings | Paths never flagged as unused |
| `AllProjectAssets` | Lazy (one call) | All `Assets/` paths, computed once |
| `OnProgress` | Caller | Progress callback |
| `CancellationToken` | Caller | Cancellation support |

### Lifecycle Methods

| Method | When Called | What It Does |
|--------|-----------|--------------|
| `EnsureStaleness()` | Start of scan | Calls `Cache.RefreshStaleness()` exactly once per context |
| `FinalizeScan()` | End of scan | `StampStaleAssets()` + `Cache.Save()` + `ChangeTracker.Reset()` |
| `ReportProgress()` | During scan | Forwards to OnProgress callback |
| `ThrowIfCancelled()` | During scan | Throws if cancellation requested |

### Idempotent Staleness

`EnsureStaleness()` uses a `bool m_StalenessComputed` flag to ensure
`RefreshStaleness` runs at most once per context. This prevents redundant O(N)
work when multiple code paths create the same context.

### Factory

```csharp
var context = PCPScanContext.FromGlobalContext(alwaysUsedRoots);
// Pulls Settings, DependencyResolver, ScanCache, IgnoreRules,
// RenderPipelineDetector from PCPContext.
```

---

## 8. Dependency Graph (PCPDependencyResolver)

Builds a full forward and reverse dependency graph for every asset under
`Assets/` and computes reachability from a set of root assets via BFS.

### Data Structures

```
m_Forward:  asset -> HashSet<assets it depends on>
m_Reverse:  asset -> HashSet<assets that depend on it>
m_Reachable: HashSet<all assets reachable from roots via BFS>
m_AllAssets: HashSet<every asset in the graph>
```

### Build Phases

#### Phase 1: Gather Asset Paths

Uses the pre-fetched `allAssetPaths` array from `PCPScanContext.AllProjectAssets`
to avoid an extra `AssetDatabase.GetAllAssetPaths()` call.

#### Phase 2: Build/Update Adjacency Lists

**Full Build** (first scan or cache unavailable):
- For every asset, call `AssetDatabase.GetDependencies(path, false)`
- Cache the result via `cache.SetDependencies(path, deps)`
- Build forward and reverse edges

**Incremental Build** (`IsBuilt && cache != null`):
- Keep existing adjacency lists, only clear reachability
- Detect deleted assets (in graph but not in current project) -> remove edges
- For each asset:
  - If `!cache.IsStale(path) && m_Forward.ContainsKey(path)` -> skip (O(1))
  - Otherwise: remove old edges, re-query dependencies, rebuild edges
- Only stale/new assets call `AssetDatabase.GetDependencies()`

This is the single biggest optimization. On a 10,000-asset project where 5 files
changed, the incremental path makes ~5 GetDependencies calls instead of ~10,000.

#### Phase 3: BFS Reachability

Starting from root assets (build scenes, Resources, Addressables, etc.),
performs breadth-first search through forward edges. Every visited asset is
marked as reachable. Assets not in the reachable set are candidates for the
unused scanner.

After Build completes, the graph is automatically saved to disk via `SaveToDisk()`.

### Disk Persistence

The forward adjacency list is serialized to `Library/ProjectCleanPro/DepGraph.bin`:

```
Version (int32)
AssetCount (int32)
For each asset:
    Path (string)
    DependencyCount (int32)
    DependencyPaths (string[])
```

Reverse edges and reachability are NOT stored. On `LoadFromDisk()`:
- Forward edges are deserialized
- Reverse edges are rebuilt from forward edges (O(E), no Unity API calls)
- `IsBuilt` is set to `true` so the next `Build()` takes the incremental path

This means that after a domain reload:
1. `PCPContext.DependencyResolver` getter creates a new resolver
2. `LoadFromDisk()` restores the full forward+reverse graph in milliseconds
3. The next `Build()` sees `IsBuilt == true` and only re-queries stale assets
4. Instead of ~10,000 GetDependencies calls, it makes ~5 (for changed files)

### Queries

| Method | Returns | Cost |
|--------|---------|------|
| `GetDependencies(path)` | Direct dependencies | O(1) |
| `GetDependents(path)` | Assets depending on this | O(1) |
| `IsReachable(path)` | Whether reachable from roots | O(1) |
| `GetAllReachable()` | Entire reachable set | O(1) |
| `GetAllUnreachable()` | Unreachable assets | O(N) iteration |
| `GetAllAssets()` | All graph vertices | O(1) |

### Edge Removal

`RemoveAssetEdges(path)` removes all forward and reverse edges for an asset.
Used by:
- Incremental build (clearing stale edges before re-adding)
- Smart delete (removing deleted assets from graph without rebuild)

`RemoveAssets(paths)` calls `RemoveAssetEdges` for each path. Used by the
smart delete flow.

---

## 9. Result Cache (PCPResultCache)

Persists the full `PCPScanResult` to disk as JSON so that results survive
domain reloads and window close/reopen.

### File Location

`Library/ProjectCleanPro/Results/ScanResult.json`

### Methods

| Method | Purpose |
|--------|---------|
| `Save(PCPScanResult)` | Serialize to JSON, write to disk |
| `Load()` | Deserialize from disk, return null if missing/invalid |
| `InvalidateAll()` | Delete the cached file |
| `HasCachedResult` | Check if file exists |

### When It Is Used

- **ScanAll completes**: `PCPResultCache.Save(m_LastScanResult)` persists results
- **Window opens**: `PCPResultCache.Load()` restores results if `LastScanResult` is null
- **Smart delete**: `PCPResultCache.Save(m_ScanResult)` updates after path removal

This enables the following workflow:
1. Run a full scan
2. Close the editor
3. Reopen -> results are loaded from disk
4. Click Scan All -> "No changes detected" (instant skip)

---

## 10. Full Scan Flow (ScanAll)

```
User clicks "Scan All" (PCPWindow.ScanAll)
    |
    v
[1] CreateScanContext()
    -> PCPScanContext.FromGlobalContext(alwaysUsedRoots)
    -> Bundles Settings, DependencyResolver, ScanCache, IgnoreRules
    |
    v
[2] context.EnsureStaleness()
    -> Cache.RefreshStaleness(AllProjectAssets)
    -> Determines which assets are stale (O(1), O(changed), or O(N))
    -> ComputeModuleDirtiness() marks which modules need re-running
    |
    v
[3] No-changes check
    if (!Cache.HasAnyChanges && hasExistingResults)
        -> Log "No changes detected"
        -> Update status bar + refresh UI
        -> Return immediately (no modules run)
    |
    v
[4] Clear dirty module results
    For each module:
        if IsModuleDirty(moduleId):
            ClearModuleResults(moduleId)  // Clear only affected results
        else:
            // Preserve existing results
    |
    v
[5] Run dirty modules
    For each module:
        if !IsModuleDirty(moduleId):
            continue  // Skip clean module

        UpdateProgress(...)
        module.Scan(scanContext)
        CollectModuleResults(module, result)
    |
    v
[6] Finalize
    context.FinalizeScan()
        -> Cache.StampStaleAssets(allAssets)  // O(stale + new)
        -> Cache.Save()                       // Binary write (no-op if !dirty)
        -> PCPAssetChangeTracker.Reset()      // Clear tracked changes

    PCPResultCache.Save(result)  // Persist results for domain reload
    UpdateStatusBar()
    RefreshActiveView()
```

---

## 11. Individual Module Scan Flow

When the user clicks the "Scan" button on a specific module tab:

### Views Extending PCPModuleView (Unused, Shaders)

```
PCPModuleView.OnScanClicked()
    |
    v
[1] context = m_CreateContext()
[2] context.EnsureStaleness()
    |
    v
[3] No-changes check
    if (!Cache.HasAnyChanges && HasModuleResults())
        -> Log "No changes — skipping module scan"
        -> Skip to finally block
    |
    v
[4] DoModuleScan(context)  // Subclass override
    -> Creates scanner, runs Scan(context)
    -> Copies results to m_ScanResult
    |
    v
[5] context.FinalizeScan()
    |
    v
[finally] OnScanComplete() -> Refresh UI
          onScanComplete()  -> Notify window (dashboard refresh)
```

### Custom Views (Missing, Duplicates, Packages, Size)

These views don't extend PCPModuleView but follow the same pattern:

```
OnScanClicked()
    |
    v
[1] context = m_CreateContext()
[2] context.EnsureStaleness()
    |
    v
[3] No-changes check
    if (!Cache.HasAnyChanges && existingResults.Count > 0)
        -> Log "No changes — skipping [module] scan"
        -> Skip to finally block
    |
    v
[4] Run module scanner
    scanner.Scan(context)
    Copy results to m_ScanResult
    |
    v
[5] context.FinalizeScan()
    |
    v
[finally] header.IsScanning = false
          Refresh UI
```

---

## 12. Smart Delete Flow

When the user selects assets and clicks "Delete Selected":

```
PCPModuleView.OnDeleteSelected()
    |
    v
[1] Collect selected paths from result list
    |
    v
[2] Create context, get resolver
    preview = PCPSafeDelete.Preview(paths, resolver)
    -> For each path, count dependents via resolver.GetDependents()
    -> Each item gets a referenceCount
    |
    v
[3] Show delete preview dialog
    User sees: paths, sizes, reference counts, warnings
    User confirms or cancels
    |
    v
[4] PCPSafeDelete.ArchiveAndDelete(preview, settings, resolver)
    -> Archive to trash folder (if archiveBeforeDelete)
    -> AssetDatabase.DeleteAsset() for each path
    |
    v
[5] Check: did ANY deleted item have referenceCount > 0?
    |
    +--> YES: RescanAfterChange()
    |         -> Full module re-scan (dependency topology changed)
    |         -> EnsureStaleness + DoModuleScan + FinalizeScan
    |
    +--> NO:  Smart path (no rescan needed)
              |
              v
              m_ScanResult.RemovePaths(deletedPaths)
                  -> Remove from unusedAssets, sizeEntries, etc.
                  -> Remove entries from duplicate groups
                  -> Drop groups with fewer than 2 entries
              |
              v
              For each deleted path:
                  cache.RemoveEntry(path)       // Remove from cache
              resolver.RemoveAssets(deletedPaths) // Remove from graph
              cache.Save()                        // Persist cache
              PCPResultCache.Save(m_ScanResult)   // Persist results
              |
              v
              OnScanComplete() -> Instant UI refresh
```

### Why This Is Fast

When deleted assets have no dependents:
- No other asset references them, so the dependency graph topology is unchanged
  (except for the removal of leaf nodes)
- No module needs to re-analyze anything
- Results are updated by simple list removal
- Total cost: O(deleted) instead of O(N)

---

## 13. Binary Persistence Formats

### Scan Cache (ScanCache.bin)

```
Header:
    Version       int32    (CurrentVersion = 2)
    EntryCount    int32

Per Entry:
    AssetPath         string (BinaryWriter length-prefixed UTF-8)
    LastModifiedTicks int64
    FileSizeBytes     int64
    HasHash           bool
    [SHA256Hash]      string (only if HasHash)
    DependencyCount   int32
    Dependencies      string[] (DependencyCount items)
    MetadataCount     int32
    Metadata          (key: string, value: string)[] (MetadataCount pairs)
```

Performance vs the old JSON format:
- Serialization: 5-10x faster (no reflection, no string escaping)
- File size: 3-5x smaller (no JSON syntax overhead)
- For 50K assets: ~30ms save vs ~300ms with JSON

### Dependency Graph (DepGraph.bin)

```
Header:
    Version     int32    (GraphFormatVersion = 1)
    AssetCount  int32

Per Asset:
    AssetPath         string
    DependencyCount   int32
    Dependencies      string[] (DependencyCount items)
```

Only forward edges are stored. On load, reverse edges are rebuilt by iterating
all forward edges and inserting reverse entries. This is O(E) with no Unity API
calls -- purely in-memory dictionary operations.

Reachability is NOT stored because it depends on which roots are configured
(build scenes, Resources, etc.), which can change between scans.

---

## 14. Lifecycle & Persistence Chain

### Editor Opens (Domain Load)

```
PCPContext service properties accessed for the first time:
    |
    +-> ScanCache = new PCPScanCache()
    |       .Load()  -> Reads ScanCache.bin (binary)
    |
    +-> DependencyResolver = new PCPDependencyResolver()
            .LoadFromDisk()  -> Reads DepGraph.bin (binary)
            Sets IsBuilt = true (enables incremental builds)
```

### Window Opens (PCPWindow.OnEnable)

```
PCPContext.Initialize()  -> Forces lazy init of all services
    |
    v
Try PCPContext.LastScanResult  -> In-memory (same session)
    |
    v (null)
Try PCPResultCache.Load()  -> From disk (survives domain reload)
    |
    v (null)
new PCPScanResult()  -> Empty results
```

### Scan Completes

```
context.FinalizeScan()
    |
    +-> Cache.StampStaleAssets()  -> Updates timestamps for changed assets
    +-> Cache.Save()              -> Writes ScanCache.bin (if dirty)
    +-> ChangeTracker.Reset()     -> Clears s_ChangedAssets, s_FullCheckNeeded=false
    |
    v
DependencyResolver.Build() already called SaveToDisk() -> DepGraph.bin
    |
    v
PCPResultCache.Save(result) -> Results/ScanResult.json
```

### Window Closes (PCPWindow.OnDisable)

```
PCPContext.SaveCache()
    -> s_ScanCache.Save()  -> ScanCache.bin (if dirty)

Context stays alive (services persist in static fields)
```

### Editor Closes / Domain Unload

```
PCPContext.Dispose()
    -> s_ScanCache.Save()              -> ScanCache.bin
    -> s_DependencyResolver.SaveToDisk() -> DepGraph.bin
    -> Null all references
```

### Next Session Reopens

Everything is restored from disk:
- Cache with all per-asset timestamps, hashes, sizes, dependencies
- Dependency graph with full forward+reverse edges
- Previous scan results

First scan after reopen:
- `FullCheckNeeded = true` -> full timestamp check (O(N) filesystem calls)
- But dependency graph is already loaded -> incremental build
- Only stale assets re-query `AssetDatabase.GetDependencies()`

---

## 15. Public Scripting API (PCPAPI)

### Running Scans

```csharp
// Run all modules with defaults:
PCPScanResult result = PCPAPI.RunScan();

// Run specific modules:
var result = PCPAPI.RunScan(new PCPScanOptions
{
    Modules = new[] { "unused", "duplicates" },
    VerboseLogging = true,
});

// Single-module convenience:
var unused = PCPAPI.GetUnusedAssets();
var missing = PCPAPI.GetMissingReferences();
var dupes = PCPAPI.GetDuplicateGroups();
var packages = PCPAPI.GetPackageAudit();
```

### Exporting Reports

```csharp
PCPAPI.ExportReport(result, PCPReportFormat.JSON, "C:/Reports/scan.json");
PCPAPI.ExportReport(result, PCPReportFormat.CSV,  "C:/Reports/scan.csv");
PCPAPI.ExportReport(result, PCPReportFormat.HTML, "C:/Reports/scan.html");
```

### Module IDs

| ID | Scanner Class | Findings |
|----|--------------|----------|
| `unused` | PCPUnusedScanner | Unreferenced assets |
| `missing` | PCPMissingRefScanner | Broken serialized references |
| `duplicates` | PCPDuplicateDetector | Content-identical assets |
| `dependencies` | PCPDependencyModule | Circular deps + orphans |
| `packages` | PCPPackageAuditor | Unused/transitive packages |
| `shaders` | PCPShaderAnalyzer | Pipeline mismatches, variant counts |
| `size` | PCPSizeProfiler | Optimization suggestions |

---

## 16. Performance Characteristics

### Complexity Summary

| Operation | First Scan | Repeat (no changes) | Repeat (10 files changed) |
|-----------|-----------|---------------------|--------------------------|
| RefreshStaleness | O(N) full check | O(1) instant | O(10) incremental |
| Dependency Build | O(N) GetDependencies calls | Skipped | O(10) GetDependencies calls |
| BFS Reachability | O(V+E) | Skipped | O(V+E) |
| Module Execution | All 7 modules | None | Only dirty modules |
| StampAssets | O(N) first time | No-op | O(10) stale only |
| Cache Save | O(N) binary write | No-op (not dirty) | O(N) binary write |
| Smart Delete (no deps) | N/A | O(deleted) | O(deleted) |

### Expected Times (10,000-asset project)

| Scenario | Expected Duration |
|----------|------------------|
| First full scan | 8-15 seconds (dominated by GetDependencies) |
| Full scan, nothing changed | < 100ms (instant skip) |
| Full scan, 10 files changed | 0.5-2 seconds (incremental graph + dirty modules) |
| Module scan, nothing changed | < 50ms (instant skip) |
| Delete 5 unused files (0 refs) | < 100ms (smart path, no rescan) |
| Domain reload + scan, nothing changed | 0.5-1 second (load graph from disk, full timestamp check, no modules) |
| Window reopen, nothing changed | < 50ms (load results from disk) |

### Key Optimization Strategies

1. **Idempotent Staleness**: `EnsureStaleness()` computes once per context,
   all subsequent `IsStale()` calls are O(1) HashSet lookups.

2. **Module Dirty Tracking**: Extension-based heuristic determines which
   modules to skip. Shader-only changes don't re-run the missing ref scanner.

3. **Incremental Dependency Graph**: Graph persists to disk. Only stale assets
   re-query `AssetDatabase.GetDependencies()`. The expensive BFS runs only when
   modules that need reachability data are dirty.

4. **Smart Delete**: Assets with zero dependents are removed from results, cache,
   and graph without any module re-running.

5. **Binary Cache Format**: 5-10x faster than JSON for large projects. Dirty flag
   prevents unnecessary writes.

6. **Result Persistence**: Scan results survive domain reloads and window
   close/reopen, enabling instant "nothing changed" skips even after editor restart.
