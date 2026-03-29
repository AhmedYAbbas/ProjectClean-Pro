# ProjectClean Pro - Feature Documentation

**Version 1.0**

ProjectClean Pro is a Unity editor extension that helps you find, analyze, and clean up issues in your project. It scans for unused assets, missing references, duplicate files, dependency problems, unused packages, shader issues, and oversized assets - all from a single window.

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Dashboard](#dashboard)
3. [Unused Assets Scanner](#unused-assets-scanner)
4. [Missing References Detector](#missing-references-detector)
5. [Duplicate Finder](#duplicate-finder)
6. [Dependency Graph Visualizer](#dependency-graph-visualizer)
7. [Package Auditor](#package-auditor)
8. [Shader Analyzer](#shader-analyzer)
9. [Size Profiler](#size-profiler)
10. [Archive & Restore](#archive--restore)
11. [Settings](#settings)
12. [Filtering, Sorting & Selection](#filtering-sorting--selection)
13. [Exporting Reports](#exporting-reports)
14. [Scan Performance & Caching](#scan-performance--caching)
15. [Safe Deletion System](#safe-deletion-system)
16. [Scripting API](#scripting-api)

---

## Getting Started

Open ProjectClean Pro from the Unity menu bar:

**Tools > ProjectClean Pro**

The window has a minimum size of 900x500 pixels and is divided into two areas:

- **Left sidebar** - Navigation tabs for each module
- **Right panel** - Content area showing the selected module

There are **10 navigation tabs** in the sidebar:

| Tab | Purpose |
|-----|---------|
| Dashboard | Overview of project health and scan summaries |
| Unused | Find assets not used anywhere in the project |
| Missing | Detect broken or missing references in scenes and prefabs |
| Duplicates | Find files with identical content |
| Dependencies | Visualize and inspect the asset dependency graph |
| Packages | Audit installed packages for actual usage |
| Shaders | Analyze shaders for variant counts, compatibility, and usage |
| Size | Profile asset sizes with a visual treemap |
| Archive | Browse and restore previously deleted assets |
| Settings | Configure all tool behaviors and preferences |

---

## Dashboard

The Dashboard is the home screen and gives you a bird's-eye view of your project's health.

### Project Health Score

At the top, a summary row shows three key metrics:

- **Total Findings** - The combined number of issues found across all modules
- **Wasted Space** - The total disk space consumed by unused assets and duplicate files
- **Project Health** - A percentage score from 0% to 100%
  - 100% is displayed in green (no issues)
  - 50% is displayed in yellow (some issues)
  - Below 50% is displayed in red (many issues)

### Module Summary Cards

Below the summary, there are **seven cards** - one for each scan module. Each card shows:

- The module name and a colored accent matching its theme
- The number of findings (displayed as a large number)
- The amount of wasted space (where applicable)
- A status label: either **"Clean"** (green) or **"X issue(s) found"** (red)
- A **"View Results"** button that takes you directly to that module's tab

### Action Buttons

Three buttons appear below the summary cards:

- **Scan All Modules** - Runs a full scan across every module in one go
- **Export Report** - Exports all findings in your chosen format (CSV, JSON, HTML, or Markdown)
- **Reset Results** - Clears all scan data (asks for confirmation first)

### Scan Warnings

If any files could not be scanned, a collapsible **Warnings** section appears listing each problematic file and the reason it was skipped.

---

## Unused Assets Scanner

The Unused Assets module identifies files in your project that are not referenced by any scene, prefab, scriptable object, or other asset.

### How It Works

The scanner builds a full dependency graph starting from your project's **root assets**:

- All scenes listed in Build Settings
- Everything inside `Resources/` folders
- Assets assigned to AssetBundles (if enabled in settings)
- Assets registered with the Addressables system (if enabled in settings)
- Any paths you've manually added as "Always-Used Roots" in settings

Any asset that cannot be reached from these roots is flagged as **unused**.

### What Gets Excluded

The following are automatically excluded from unused detection:

- Script files (`.cs`), assembly definitions (`.asmdef`, `.asmref`), and native libraries (`.dll`, `.so`, `.dylib`, `.a`)
- Meta files (`.meta`)
- Shader include files (`.cginc`, `.hlsl`, `.glslinc`)
- Any extensions you've added to the excluded extensions list in settings
- Any paths or patterns matching your custom ignore rules
- Editor-only assets (unless "Scan Editor/ folder assets" is enabled in settings)

### Status Labels

Each unused asset is tagged with one of two statuses:

- **UNUSED** - Not reachable from any root asset
- **IN RESOURCES** - Located in a `Resources/` folder but not loaded by any script (shown with a yellow badge as a softer warning since Resources assets can be loaded dynamically at runtime)

### Available Actions

- **Delete Selected** - Opens a delete preview dialog before removing the selected assets
- **Ignore Selected** - Adds ignore rules so the selected assets won't appear in future scans
- **Export** - Exports the unused assets list to a file

---

## Missing References Detector

The Missing References module scans your scenes, prefabs, and scriptable objects for broken or null references.

### What It Detects

- **Broken object references** - A serialized field that points to an asset that no longer exists
- **Missing script references** - A MonoBehaviour component whose C# script has been deleted or renamed
- **Null property bindings** - Serialized fields that reference nothing

### How Results Are Displayed

Results are **grouped by source asset** - the scene, prefab, or asset that contains the broken references. Each group appears as a collapsible section showing:

- The source asset's name
- A count badge (e.g., "3 missing refs")
- A severity indicator

When expanded, each broken reference within that asset is listed with:

- The property name and type
- The serialization path
- A severity level

### Severity Levels

- **Error** (red) - Critical issues that will cause runtime errors
- **Warning** (yellow) - Issues that may cause unexpected behavior
- **Info** (blue) - Minor informational notices

### Filtering

You can filter missing references by:

- **Search** - Type to filter by asset name
- **Severity** - Show only errors, warnings, or info items
- **Type** - Filter by the type of asset containing the broken reference
- **Sort order** - Sort by severity, count, or name

---

## Duplicate Finder

The Duplicate Finder identifies files with identical content, even if they have different names or locations.

### How It Works

Every asset file in your project is hashed (SHA256). Files with matching hashes are grouped together. Within each group:

- One file is designated as the **canonical** copy (the one to keep)
- All other copies are marked as **duplicates** (wasted space)

Optionally, the scanner can also compare **import settings** (texture compression, mesh optimization settings, etc.) so that files with the same source but different import configurations are not incorrectly flagged.

### How Results Are Displayed

Results appear as **collapsible groups**, one per set of duplicates. Each group header shows:

- The first 16 characters of the content hash
- The number of copies found
- The total wasted size

When expanded, each file in the group is listed with:

- Its filename and path
- File size
- Reference count (how many other assets depend on it)
- A **"Keep"** radio button to designate which copy should be the canonical one

The pre-selected canonical copy is shown with a **green badge**, while duplicates show a **yellow badge**.

### Merging Duplicates

The **Merge All** button replaces all duplicate copies with references to the canonical copy. This operation:

- Updates all materials, prefabs, and scenes to point to the kept copy
- Removes the duplicate files
- Triggers an automatic rescan after completion
- Asks for confirmation before proceeding

### Filtering

You can filter duplicate groups by:

- **Search** - Type to filter by filename
- **Type** - Show only specific asset types (Textures, Materials, Meshes, etc.)
- **Sort order** - Sort by wasted size or count

---

## Dependency Graph Visualizer

The Dependency Graph module provides an interactive visual map of how your assets reference each other.

### The Graph View

An interactive node-and-edge canvas displays your asset relationships:

- **Nodes** represent individual assets, color-coded by type (Texture, Material, Mesh, Prefab, Scene, Script, Audio, Shader)
- **Edges** (arrows) show dependency direction - from the asset that references to the asset being referenced
- **Red edges** highlight circular dependencies (asset A depends on B, which depends back on A)

### Navigation Controls

- **Scroll wheel** to zoom in and out
- **Click and drag** the canvas to pan
- **Click a node** to re-center the graph around that asset
- **Selection rectangle** to multi-select nodes
- **Breadcrumb trail** at the top shows your navigation path from the original root

### Toolbar

- **Asset field** - Select any asset to use as the center of the visualization
- **Depth slider** (1-10) - Controls how many levels of dependencies are shown. A depth of 1 shows only direct dependencies; higher values show deeper chains.

The maximum depth can also be configured globally in Settings under "Dependency graph max depth."

### What It Detects

- **Circular dependencies** - Chains of assets that reference each other in a loop
- **Orphan assets** - Assets with no incoming or outgoing references

---

## Package Auditor

The Package Auditor checks every installed Unity Package Manager (UPM) package to determine whether it's actually being used in your code.

### How It Works

The tool scans all C# source files in your project for `using` statements and matches them against the namespaces provided by each installed package. Based on this analysis, each package is classified as:

- **Used** (green border) - Your code directly references this package's namespaces
- **Unused** (red border) - Installed but not referenced anywhere in your code
- **Transitive** (yellow border) - Only installed because another package depends on it
- **Unknown** (gray border) - Usage could not be determined

### How Results Are Displayed

Packages are shown as **cards** with colored borders matching their classification. Each card shows:

- Package name and version number
- Status badge (Used, Unused, Transitive, or Unknown)
- The number of direct references found
- A list of dependent packages

### Filtering

Toggle filter buttons at the top let you show:

- **All** packages
- Only **Used** packages
- Only **Unused** packages
- Only **Transitive** packages

### Selection

Click any card to select it. A selection bar appears showing the count of selected packages and available actions.

---

## Shader Analyzer

The Shader Analyzer inspects all shader-related files in your project for performance and compatibility issues.

### What It Analyzes

The module scans `.shader`, `.shadergraph`, `.shadersubgraph`, `.compute`, and `.mat` files to determine:

- **Keyword count** - The number of `multi_compile` and `shader_feature` directives
- **Variant estimation** - The calculated number of shader variants that will be compiled
- **Pipeline compatibility** - Whether the shader targets Built-in, URP, or HDRP rendering pipeline
- **Material assignments** - Which materials in your project use each shader

### Status Labels

Each shader receives one of four statuses:

- **OK** (green) - No issues detected
- **HIGH VARIANTS** (red) - More than 256 estimated variants, which can significantly impact build times and runtime memory
- **MISMATCH** (yellow) - The shader targets a different rendering pipeline than what your project uses (e.g., a URP shader in a Built-in project)
- **UNUSED** (red) - No materials in the project reference this shader

### Detail Panel

When you select a shader from the list, a detail panel appears below showing:

- The shader name
- Total keyword count, variant estimate, and pass count
- An expandable list of all pragma directives with their individual keyword names and per-keyword variant contributions

### Available Actions

- **Delete Selected** - Remove selected unused shaders (with delete preview)
- **Ignore Selected** - Add selected shaders to the ignore list
- **Export** - Export shader analysis to a file

Pipeline compatibility checking can be toggled in Settings under "Check shaders for pipeline compatibility."

---

## Size Profiler

The Size Profiler gives you a complete breakdown of where disk space is being consumed in your project, with visual tools to identify the largest assets.

### Summary Bar

At the top, a large label shows the **total project size**. Below it, a **stacked percentage bar** visualizes how space is distributed across asset types:

- **Textures** (light blue)
- **Meshes** (brown)
- **Audio** (pink)
- **Animations** (green)
- **Other** (gray)

Each segment is proportional to its share of total size.

### Type Filter Tabs

Click any type label to filter the view to only that category: All, Textures, Meshes, Audio, Animations, or Other.

### Treemap Visualization

The upper portion of the view shows an interactive **squarified treemap** where:

- Each rectangle represents one asset
- Rectangle **size** is proportional to the asset's disk size
- Rectangle **color** indicates the asset type
- **Hovering** shows a tooltip with the asset name and exact size
- **Clicking** a rectangle drills down into that folder for a closer look
- A **breadcrumb trail** at the top lets you navigate back up (e.g., "All / Assets / Textures")

### Result List

Below the treemap, a standard result list shows all assets with columns for name, path, type, size, and optimization status.

### Optimization Suggestions

The profiler flags assets that could benefit from compression or optimization:

- **OPTIMIZE** (yellow badge) - The asset is over 1 MB and not using compression (applies to textures and audio files)
- **OK** (green badge) - No optimization needed

---

## Archive & Restore

When you delete assets through ProjectClean Pro, they can optionally be archived for later recovery.

### How Archiving Works

- Deleted assets are saved to a `.pcp_archive/` folder at your project root
- Each deletion batch creates a **session folder** named with a timestamp (e.g., `20260328_185138_259/`)
- A `manifest.json` file inside each session records metadata about what was deleted

Archiving is enabled by default and can be toggled in Settings under "Archive assets before deleting."

### Archive Browser

The Archive tab shows all saved sessions as expandable cards. Each card displays:

- **When** the deletion happened (timestamp)
- **How many** assets were archived
- **Total size** of the archived files

Expanding a card reveals the full list of archived files with their original paths and sizes.

### Restoring Assets

- **Restore All** - Restores every file in a session back to its original location
- **Delete Session** - Permanently removes an archive session (cannot be undone)

### Automatic Cleanup

- **Purge Old Archives** button in the header automatically removes sessions older than the retention period
- The retention period is configurable in Settings (default: 30 days, minimum: 1 day)

### Empty State

If no archives exist, a message explains that archives are created automatically when assets are deleted with the archive setting enabled.

---

## Settings

The Settings tab provides full control over how ProjectClean Pro operates.

### Scan Configuration

| Setting | Description |
|---------|-------------|
| Include all scenes | Scan every `.unity` scene file, not just those listed in Build Settings |
| Include Addressable entries | Treat Addressable system entries as root assets |
| Include AssetBundle entries | Treat AssetBundle assignments as root assets |
| Scan Editor/ folder assets | Include assets inside `Assets/Editor/` in scans |

### Excluded Extensions

A list of file extensions that should be skipped entirely during scans. Pre-configured defaults include `.cs`, `.meta`, `.asmdef`, `.asmref`, `.dll`, `.so`, `.dylib`, `.a`, `.rsp`, `.cginc`, `.hlsl`, and `.glslinc`.

You can add or remove extensions at any time.

### Always-Used Roots

A list of folder or asset paths that should always be treated as "used," regardless of whether they're reachable from Build Settings scenes. All dependencies of these paths are also protected.

Each entry has:

- A text field for the path
- A **Browse** button that opens a folder picker and converts the selection to a project-relative path
- A **Remove** button

### Ignore Rules

Custom rules that exclude specific assets from scan results. Each rule consists of:

- An **enable/disable toggle** to temporarily turn the rule on or off without deleting it
- A **type** dropdown (PathPrefix, FileExtension, and more)
- A **pattern** field for the matching expression
- A **comment** field for your own notes about why this rule exists

### Safe Delete

| Setting | Description |
|---------|-------------|
| Archive assets before deleting | Save a backup copy before removal (default: on) |
| Use 'git rm' in repositories | When inside a Git repository, use `git rm` instead of filesystem delete |
| Null out references on delete | Automatically clear broken references in scenes and prefabs after deletion |
| Archive retention (days) | How many days to keep archived sessions (default: 30, minimum: 1) |

### Module Settings

| Setting | Description |
|---------|-------------|
| Dependency graph max depth | Maximum depth (1-10) shown in the dependency visualizer |
| Check shaders for pipeline compatibility | Enable URP/HDRP mismatch detection in shader analysis |
| Compare import settings for duplicates | When enabled, files with identical content but different import settings are not flagged as duplicates |

### Performance

| Setting | Description |
|---------|-------------|
| Enable console logging | Print scan progress and cache activity to the Unity Console |
| Main Thread Budget (ms) | How much time per frame the scanner is allowed to use (4-16 ms) |

The thread budget slider balances editor responsiveness against scan speed:

- **4 ms** - Editor stays smooth, scans take longer
- **8 ms** - Balanced (default)
- **16 ms** - Fastest scans, but the editor may stutter

### Module Accent Colors

Customize the accent color for each of the 8 modules (Unused, Missing, Duplicates, Dependencies, Packages, Shaders, Size, Archive). These colors appear in tab borders, card accents, dashboard cards, and graph nodes.

Each color has a swatch preview and a color picker field.

### Actions

- **Reset to Defaults** - Restores all settings to their original values (asks for confirmation)
- **Clear Cache** - Removes the incremental scan cache, forcing the next scan to be a full rescan

All settings are **saved automatically** as you change them - there is no Apply button.

---

## Filtering, Sorting & Selection

Most module views share a common set of interaction features.

### Search

A search field at the top of each result view lets you filter by asset name or path. Filtering happens in real time as you type.

### Type Filter Chips

Clickable chips let you narrow results to specific asset types: Texture, Material, Mesh, Audio, Script, Prefab, Scene, or Other. You can select multiple types at once. Clicking **All** resets the filter.

Active chips are highlighted in blue. Inactive chips appear in dark gray.

### Status / Severity Dropdown

A dropdown lets you filter by status (e.g., UNUSED, IN RESOURCES) or severity (ERROR, WARNING, INFO) depending on the module.

### Clear Filters

A single button resets all active filters at once.

### Active Filters Label

When any filter is active, a small italic label shows exactly which filters are applied (e.g., "Active: Type: Texture | Status: UNUSED").

### Column Sorting

Click any sortable column header to sort the results in ascending order. Click again to reverse to descending. Sortable columns include Name, Path, Type, Size, and Status.

### Column Resizing

Drag the border between column headers to resize columns. Each column has a minimum width of 30 pixels.

### Multi-Selection

- Click a row's checkbox to select or deselect it
- Use **Shift+Click** to select a range of rows
- Selected items are available for batch actions (Delete, Ignore, Export)

---

## Exporting Reports

You can export scan results in four formats:

| Format | Description |
|--------|-------------|
| **JSON** | Structured data with complete finding details, suitable for automation and CI pipelines |
| **CSV** | Flat table format with one row per finding, compatible with spreadsheets |
| **HTML** | Self-contained interactive report that can be opened in any browser |
| **Markdown** | Human-readable text format suitable for documentation or issue trackers |

### Export Scope

- **Full report** (from Dashboard) - Includes findings from all modules
- **Module-specific** (from individual tabs) - Includes only that module's findings

When no file path is provided, a save-file dialog appears. If a path is given (e.g., via the scripting API), the report is written directly.

---

## Scan Performance & Caching

ProjectClean Pro is designed to handle large projects efficiently.

### Incremental Scanning

After the first full scan, subsequent scans only process assets that have changed. The tool tracks file modifications and only re-runs modules affected by those changes.

If nothing has changed since the last scan, the scan completes instantly with cached results.

### What Gets Cached

- **Scan cache** - Binary file tracking which assets have been processed and their timestamps
- **Dependency graph** - The full asset dependency graph in binary format
- **Scan results** - The complete findings from the last scan in JSON format

All caches are stored in the `Library/ProjectCleanPro/` folder and persist across editor sessions and domain reloads.

### Clearing the Cache

Use the **Clear Cache** button in Settings to force a full rescan on the next run. This is useful if results seem stale or after significant project restructuring.

### Module Execution Order

When running a full scan, modules execute in an optimized order:

1. **Unused Assets** runs first (builds the dependency graph used by other modules)
2. **Dependencies** reuses the graph from step 1
3. **Duplicates** uses the graph for reference counting
4. **Missing References**, **Shaders**, **Size Profiler**, and **Packages** run independently

---

## Safe Deletion System

ProjectClean Pro includes several safety mechanisms to prevent accidental data loss.

### Delete Preview Dialog

Before any deletion, a preview dialog shows:

- The complete list of files that will be deleted
- Each file's name, type, size, and reference count
- A warning banner if any selected assets are still referenced by other files
- The total size that will be reclaimed
- An option to override the archive setting for this specific deletion

### Reference Warnings

If a selected asset is referenced by scenes, prefabs, or other assets, the dialog shows:

- A red warning banner
- The list of referencing assets (up to 10 shown)
- The total number of references that will be broken

### Archive Backup

When archiving is enabled, deleted files are copied to `.pcp_archive/` before removal. You can restore them at any time from the Archive tab.

### Git Integration

When "Use 'git rm' in repositories" is enabled and your project is inside a Git repository, deletions go through `git rm` instead of direct filesystem deletion. This ensures the removal is properly tracked in version control.

### Reference Cleanup

When "Null out references on delete" is enabled, ProjectClean Pro automatically clears broken references in scenes and prefabs after deletion, preventing null reference errors at runtime.

---

## Scripting API

ProjectClean Pro provides a public API for running scans and exporting reports from custom editor scripts or CI pipelines.

### Running Scans

You can trigger scans programmatically with optional configuration:

- Run a full scan across all modules
- Run scans for specific modules only
- Provide additional scan roots beyond what's configured in settings
- Control verbose logging
- Support for cancellation tokens for async workflows

### Retrieving Results

Convenience methods let you retrieve results for individual modules:

- Get all unused assets
- Get all missing references
- Get all duplicate groups
- Get the package audit results

### Exporting

The same export formats available in the UI (JSON, CSV, HTML, Markdown) can be triggered from code by providing the scan result, desired format, and output path.

### Use Cases

- **CI/CD integration** - Run scans as part of your build pipeline and fail builds if issues exceed a threshold
- **Automated cleanup** - Script periodic cleanup routines
- **Custom reporting** - Generate reports in formats tailored to your team's workflow

---

## Status Bar

At the bottom of the ProjectClean Pro window, a persistent status bar shows:

- **Last scan time** - The time (HH:MM:SS) when the most recent scan completed
- **Findings summary** - A compact "X findings | Y reclaimable" label
- **Current status** - Real-time progress text during active scans

### Scan Progress Overlay

While a scan is running, a semi-transparent overlay covers the window with:

- A "Scanning Project..." title
- The name of the module currently being processed
- A progress bar showing completion percentage (0-100%)
- A **Cancel** button to stop the scan at any time

---

*ProjectClean Pro - Keep your Unity project lean, clean, and optimized.*
