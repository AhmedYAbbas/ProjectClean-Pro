# ProjectClean Pro v1.0 — Final Validation & Sanitation Report

**Date:** 2026-03-21
**Auditor:** Claude Opus 4.6
**Scope:** Full documentation-vs-implementation comparison, Unity version compatibility audit, code quality review

---

## 1. CRITICAL ISSUES (Must Fix Before Production)

### 1.1 No IMGUI Fallback — Unity 2020 and Earlier NOT Supported

**Doc claims:** Unity 5.6–2020.3 support via IMGUI fallback.
**Reality:** The entire UI is built exclusively with `VisualElement` / UI Toolkit. There is **zero** IMGUI code (`OnGUI`, `GUILayout`, `EditorGUILayout`) anywhere in the codebase. The `ListView` API used requires Unity 2021.2+.

**Preprocessor directives found (oldest first):**

| Directive | File | Purpose |
|---|---|---|
| `UNITY_2021_1_OR_NEWER` | PCPPackageAuditor.cs | Package API fallback |
| `UNITY_2021_2_OR_NEWER` | PCPResultListView.cs | ListView virtualization + Refresh API |
| `UNITY_2022_1_OR_NEWER` | PCPRenderPipelineDetector, FilterBar, Toolbar | GraphicsSettings API, placeholder text |
| `UNITY_2022_2_OR_NEWER` | PCPResultListView.cs | selectionChanged event |
| `UNITY_2023_2_OR_NEWER` | Badge, FilterBar, Header, Overlay, ResultList, Toolbar, Treemap | UxmlElement attribute |
| `UNITY_6000_3_OR_NEWER` | PCPSafeDelete.cs | EntityIdToObject API |

**Verdict:** The tool will **not compile** on Unity 2020.3 or earlier. The minimum supported version is effectively **Unity 2021.2**. Either:
- **(a)** Build an IMGUI fallback for all views (significant effort), or
- **(b)** Update the documentation to reflect minimum Unity 2021.2+ support

### 1.2 Scripting API Names Don't Match Documentation

| Doc Says | Actual Code | Status |
|---|---|---|
| `PCPScanner.RunFullScan()` | `PCPAPI.RunScan()` | **Mismatch** |
| `PCPScanner.RunFullScanAsync()` | Not implemented | **Missing** |
| `PCPSafeDelete.Execute()` | `PCPSafeDelete.ArchiveAndDelete()` | **Mismatch** |
| `PCPDeleteResult` (return type) | Not defined | **Missing class** |
| `PCPDeleteOptions` (parameter) | Uses `PCPSettings` directly | **Missing class** |
| `PCPSettings.Load()` | `ScriptableSingleton` via `PCPContext.Settings` | **Mismatch** |
| `PCPAssetType` enum | Not defined (uses string `assetTypeName`) | **Missing enum** |
| `PCPReportScope` flags enum | Not defined | **Missing enum** |

### 1.3 CLI Flags Don't Match Documentation

| Doc Flag | Actual Flag | Status |
|---|---|---|
| `-pcp-output` | `-pcpOutput` | **Mismatch** (camelCase) |
| `-pcp-format` | `-pcpFormat` | **Mismatch** |
| `-pcp-modules` | `-pcpModules` | **Mismatch** |
| `-pcp-fail-on` | `-pcpFailOnFindings` | **Mismatch** (different name & semantics) |
| `-pcp-config` | Not implemented | **Missing** |
| `-pcp-no-archive` | Not implemented | **Missing** |
| `-pcp-verbose` | `-pcpVerbose` | **Mismatch** |
| `-executeMethod ...PCPCommandLine.RunScan` | `...PCPCli.ScanAll` / `ScanAndExport` | **Mismatch** (class & method) |

### 1.4 Markdown Report Export Missing

Doc claims 4 export formats: JSON, CSV, HTML, **Markdown**. Only JSON, CSV, HTML are implemented.

---

## 2. SIGNIFICANT GAPS (Doc Features Not Implemented)

### Missing Features by Module

**Unused Asset Scanner:**
- "MAYBE USED" status badge (only UNUSED exists)
- Scene usage mapping (hover to see which scenes reference an asset)

**Missing Reference Finder:**
- One-click null-out from the Missing Refs view
- Binary asset support / Force Text serialization switching
- FileID + GUID cross-check (implementation checks object references, not raw YAML parsing)

**Duplicate Detector:**
- Material duplicate detection by shader+property values (only file hash)
- Texture atlas cross-check
- Merge assistant partially implemented (merge exists in UI but doesn't reroute references before deleting)

**Dependency Graph:**
- Export graph as SVG/PNG
- Node size by file size
- Filter by folder in graph view

**Package Auditor:**
- Package health check (version compatibility, available updates, deprecated detection)
- Built-in package detection (only UPM packages scanned)

**Shader Analyzer:**
- Shader Graph node count analysis
- Shader compilation error detection
- Variant collection audit (ShaderVariantCollection)
- Keyword redundancy check
- URP Shader Graph upgrader integration
- HDRP material validator integration

**Size Profiler:**
- Mesh complexity report (vertex/poly count — only checks Read/Write and compression)
- Platform-specific build size estimates
- Historical size snapshots/comparison
- Oversized texture detection (source vs MaxTextureSize)

**Safe Delete:**
- Dry-run mode
- Delete log (`Library/ProjectCleanPro/deletions.log`)
- Archive size management by MB (only by days)

**Addressables:**
- Group cleanliness audit
- Missing AssetReference detection
- Stale group entry detection

**Keyboard Shortcuts:**
- No keyboard shortcut implementation found (doc lists 10 shortcuts)

---

## 3. SIMILARITIES / CORRECTLY IMPLEMENTED

### Core Architecture (Matches Doc Design)
- 7 scan modules with unified `IPCPModule` interface
- `PCPScanResult` with all expected result lists (unused, missing, duplicates, packages, shaders, size, circular deps, orphans)
- Safety-first delete workflow: Preview -> Archive -> Delete
- Archive system with session management and restore
- Ignore rules system with PathPrefix, PathExact, Regex, AssetType, Label, Folder types
- `pcp-keep` label support
- Render pipeline detection (Built-in, URP, HDRP, Custom)
- Addressables bridge with conditional compilation (`PCP_ADDRESSABLES` define)
- Git-aware deletion (`git rm --cached`)
- Report export (JSON, CSV, HTML)
- Incremental scan cache with SHA-256 hashing and timestamp-based staleness
- Settings stored in `ProjectSettings/PCPSettings.asset`
- Pure editor scripts, no runtime components
- Assembly definition with `versionDefines` for optional integrations

### Data Models (Complete)
- `PCPUnusedAsset`, `PCPMissingReference`, `PCPDuplicateGroup`
- `PCPPackageAuditEntry`, `PCPShaderEntry`, `PCPSizeEntry`
- `PCPDeletePreview`, `PCPArchiveSession`, `PCPAssetInfo`

### Module Logic (Substantial)
- Unused scanner: build scenes + Resources + Addressables + AssetBundles + custom roots + always-included shaders as dependency roots, BFS reachability
- Missing ref scanner: prefabs, scenes, ScriptableObjects, missing scripts + broken object references
- Duplicate detector: SHA-256 content hashing, YAML normalization, import settings comparison
- Dependency module: circular dependency detection via DFS, orphan asset detection
- Package auditor: manifest.json analysis, using-directive scanning, transitive dependency awareness
- Shader analyzer: keyword/variant estimation, pipeline mismatch detection, material-to-shader mapping
- Size profiler: per-asset profiling, texture/audio/mesh optimization suggestions

### UI Quality (High)
- Professional dark theme with consistent color palette
- Reusable components (Badge, ModuleHeader, FilterBar, ProgressOverlay, Toolbar, TreemapView)
- Virtual list rendering with sorting and multi-select
- Delete preview dialog with warnings
- Dashboard with health score and module cards
- Settings view with full configuration
- Archive view with session restore

---

## 4. UNITY VERSION COMPATIBILITY MATRIX (Actual)

| Unity Version | Compiles? | UI System | Notes |
|---|---|---|---|
| **Unity 6.x (6000.0+)** | Yes | UI Toolkit | Full support including `EntityIdToObject` |
| **Unity 2023.2+** | Yes | UI Toolkit | Uses `[UxmlElement]` attribute |
| **Unity 2022.3 LTS** | Yes | UI Toolkit | All features |
| **Unity 2022.1-2022.2** | Yes | UI Toolkit | Fallback selection event API |
| **Unity 2021.2-2021.3** | Yes | UI Toolkit | Oldest working version. Fallback ListView API |
| **Unity 2021.1** | Likely | UI Toolkit | Package API fallback present but ListView might have issues |
| **Unity 2020.3 LTS** | **NO** | N/A | ListView API missing, no IMGUI fallback |
| **Unity 2019.4 LTS** | **NO** | N/A | UI Toolkit immature, no fallback |
| **Unity 2018.4 / 5.6** | **NO** | N/A | No support whatsoever |

---

## 5. CODE QUALITY ISSUES

### Safety/Correctness
1. **PCPGitUtils.RunGitCommand** — Path arguments are quoted but not shell-escaped (potential injection if asset paths contain special chars)
2. **PCPPackageAuditor** — Pre-2021.1 fallback uses `Thread.Sleep(10)` busy-wait with no timeout
3. **PCPFileUtils.ComputeNormalizedSHA256** — Reads entire file into memory (defeats streaming)
4. **PCPDuplicateGroup.ElectCanonical** — No null check on entries list
5. **PCPSafeDelete.NullOutReferences** — Loads ALL project assets to find references; O(n) on project size
6. **PCPArchiveManager** — No checksum verification of archived files

### Thread Safety
- `PCPContext` (static service locator) has no synchronization
- `PCPAPI.RunScan` mutates shared `settings.includeAllScenes` during scan
- `PCPScanCache` has no file locking for concurrent read/write

### Folder Structure Mismatch with Doc
Doc shows `CLI/PCPCommandLine.cs` but actual is `API/PCPCli.cs`. Doc shows flat `Core/` with scanner/shader/safedelete but actual uses separate `Modules/` and `Services/` directories. The actual architecture is cleaner than the doc suggests.

---

## 6. DETAILED FILE INVENTORY

### Core (8 files)
| File | Class | Purpose | Preprocessor |
|---|---|---|---|
| PCPContext.cs | PCPContext (static) | Service registry / locator | None |
| PCPDefineManager.cs | PCPDefineManager (static) | Integration define flags | PCP_ADDRESSABLES, PCP_URP, PCP_HDRP, PCP_SHADERGRAPH |
| PCPDependencyResolver.cs | PCPDependencyResolver (sealed) | Bidirectional dependency graph + BFS reachability | None |
| PCPIgnoreRules.cs | PCPIgnoreRules (sealed) | Rule evaluator with regex cache | None |
| PCPRenderPipelineDetector.cs | PCPRenderPipelineDetector (sealed) | RP detection via GraphicsSettings | UNITY_2022_1_OR_NEWER |
| PCPScanCache.cs | PCPScanCache (sealed) | JSON-based scan cache in Library/ | None |
| PCPScanContext.cs | PCPScanContext (sealed) | Dependency injection container for scans | None |
| PCPSettings.cs | PCPSettings : ScriptableSingleton | Persisted settings in ProjectSettings/ | None |

### Data (10 files)
| File | Class | Purpose |
|---|---|---|
| PCPArchiveSession.cs | PCPArchivedFile | Archived file entry |
| PCPAssetInfo.cs | PCPAssetInfo | Asset metadata container |
| PCPDeletePreview.cs | PCPDeleteWarning, PCPDeleteItem, PCPDeletePreview | Delete preview data |
| PCPDuplicateGroup.cs | PCPDuplicateEntry, PCPDuplicateGroup | Duplicate grouping |
| PCPMissingReference.cs | PCPSeverity (enum), PCPMissingReference | Missing ref entry |
| PCPPackageAuditEntry.cs | PCPPackageStatus (enum), PCPPackageAuditEntry | Package audit entry |
| PCPScanResult.cs | PCPScanResult | Aggregate scan results |
| PCPShaderEntry.cs | PCPShaderEntry | Shader analysis entry |
| PCPSizeEntry.cs | PCPSizeEntry | Size profiling entry |
| PCPUnusedAsset.cs | PCPUnusedAsset | Unused asset entry |

### Modules (9 files)
| File | Module ID | Preprocessor |
|---|---|---|
| IPCPModule.cs | (interface) | None |
| PCPModuleBase.cs | (abstract base) | None |
| PCPUnusedScanner.cs | "unused" | None |
| PCPMissingRefScanner.cs | "missing" | None |
| PCPDuplicateDetector.cs | "duplicates" | None |
| PCPDependencyModule.cs | "dependencies" | None |
| PCPPackageAuditor.cs | "packages" | UNITY_2021_1_OR_NEWER |
| PCPShaderAnalyzer.cs | "shaders" | None |
| PCPSizeProfiler.cs | "size" | None |

### Services (6 files)
| File | Class | Purpose |
|---|---|---|
| PCPArchiveManager.cs | PCPArchiveManager (static) | Archive creation/restore |
| PCPAssetUtils.cs | PCPAssetUtils (static) | Asset path utilities |
| PCPFileUtils.cs | PCPFileUtils (static) | File hashing/IO |
| PCPGitUtils.cs | PCPGitUtils (static) | Git integration |
| PCPReportExporter.cs | PCPReportExporter (static) | JSON/CSV/HTML export |
| PCPSafeDelete.cs | PCPSafeDelete (static) | Safe delete workflow |

### API (2 files)
| File | Class | Purpose |
|---|---|---|
| PCPAPI.cs | PCPScanOptions, PCPAPI (static) | Public scripting API |
| PCPCli.cs | PCPCli (static) | CLI entry points |

### Integrations (2 files)
| File | Class | Preprocessor |
|---|---|---|
| PCPAddressablesBridge.cs | PCPAddressablesBridge (static) | PCP_ADDRESSABLES |
| PCPAddressablesSupport.cs | PCPAddressablesSupport (static) | PCP_ADDRESSABLES (entire file) |

### UI — Controls (7 files)
| File | Class | Preprocessor |
|---|---|---|
| PCPBadge.cs | PCPBadge : Label | UNITY_2023_2_OR_NEWER |
| PCPDeletePreviewDialog.cs | PCPDeletePreviewDialog : EditorWindow | None |
| PCPFilterBar.cs | PCPFilterBar : VisualElement | UNITY_2023_2_OR_NEWER, UNITY_2022_1_OR_NEWER |
| PCPModuleHeader.cs | PCPModuleHeader : VisualElement | UNITY_2023_2_OR_NEWER |
| PCPProgressOverlay.cs | PCPProgressOverlay : VisualElement | UNITY_2023_2_OR_NEWER |
| PCPResultListView.cs | PCPResultListView : VisualElement | UNITY_2023_2_OR_NEWER, UNITY_2021_2_OR_NEWER, UNITY_2022_2_OR_NEWER |
| PCPToolbar.cs | PCPToolbar : VisualElement | UNITY_2023_2_OR_NEWER, UNITY_2022_1_OR_NEWER |
| PCPTreemapView.cs | PCPTreemapView : VisualElement | UNITY_2023_2_OR_NEWER |

### UI — Views (8 files)
| File | Class | Base |
|---|---|---|
| PCPDependencyGraphView.cs | PCPDependencyGraphView : VisualElement | Direct |
| PCPDuplicatesView.cs | PCPDuplicatesView : VisualElement | Direct |
| PCPMissingRefsView.cs | PCPMissingRefsView : VisualElement | Direct |
| PCPModuleView.cs | PCPModuleView : VisualElement (abstract) | Direct |
| PCPPackagesView.cs | PCPPackagesView : VisualElement | Direct |
| PCPShadersView.cs | PCPShadersView : PCPModuleView | Inherited |
| PCPSizeView.cs | PCPSizeView : VisualElement | Direct |
| PCPUnusedView.cs | PCPUnusedView : PCPModuleView | Inherited |

### UI — Other (3 files)
| File | Class | Purpose |
|---|---|---|
| IPCPRefreshable.cs | IPCPRefreshable (interface) | View refresh contract |
| PCPWindow.cs | PCPWindow : EditorWindow | Main editor window |
| PCPDashboardView.cs | PCPDashboardView : VisualElement | Dashboard overview |
| PCPSettingsView.cs | PCPSettingsView : VisualElement | Settings configuration |
| PCPArchiveView.cs | PCPArchiveView : VisualElement | Archive session browser |

---

## 7. RECOMMENDATIONS

### Before Shipping (Critical)
1. **Choose compatibility scope**: Either implement IMGUI fallback for 2020+, or update docs to state minimum Unity 2021.2
2. **Align API naming**: Either rename classes to match docs or update docs to match code
3. **Align CLI flags**: Either adopt dash-separated flags or update docs
4. **Add Markdown export** or remove it from docs
5. **Add missing API classes** (`PCPDeleteResult`, `PCPDeleteOptions`) or update docs

### Before Shipping (Important)
6. Remove Unity 5.6, 2018.4, 2019.4, 2020.3 from compatibility table in docs
7. Remove IMGUI fallback claims from docs
8. Document actual keyboard shortcuts (if any exist) or remove from docs
9. Add timeout to PCPPackageAuditor's pre-2021.1 busy-wait

### Post-Ship Backlog
10. Implement missing doc features or create a roadmap document
11. Add file locking to PCPScanCache
12. Add shell escaping to PCPGitUtils
13. Implement delete log
14. Implement dry-run mode

---

## 8. PENDING DECISIONS

| # | Decision | Options |
|---|---|---|
| 1 | Unity version scope | (A) Update docs to 2021.2+ / (B) Build IMGUI fallback / (C) Partial fallback for 2020 |
| 2 | Scripting API naming | (A) Rename code to match docs / (B) Update docs / (C) Create wrapper class |
| 3 | CLI flags | (A) Dash-separated in code / (B) Update docs / (C) Support both |
| 4 | Missing features | (A) Remove from docs / (B) Implement / (C) Mark as "v1.1" |
| 5 | Markdown export | (A) Implement / (B) Remove from docs |
| 6 | Keyboard shortcuts | (A) Implement / (B) Remove from docs / (C) Mark as planned |

---

*Report generated by Claude Opus 4.6 on 2026-03-21*
