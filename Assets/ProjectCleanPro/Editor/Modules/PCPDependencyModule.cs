using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// A node in the dependency sub-graph, representing a single asset.
    /// </summary>
    [Serializable]
    public class PCPGraphNode
    {
        /// <summary>Project-relative asset path.</summary>
        public string assetPath;

        /// <summary>Human-readable asset type name (e.g. "Texture2D", "Material").</summary>
        public string assetType;

        /// <summary>Display name derived from the file name.</summary>
        public string displayName;

        /// <summary>Depth from the center node in the BFS traversal.</summary>
        public int depth;

        public override string ToString()
        {
            return $"{displayName} ({assetType}) depth={depth}";
        }
    }

    /// <summary>
    /// A directed edge in the dependency sub-graph.
    /// </summary>
    [Serializable]
    public class PCPGraphEdge
    {
        /// <summary>Source asset path (the dependant).</summary>
        public string from;

        /// <summary>Target asset path (the dependency).</summary>
        public string to;

        public override string ToString()
        {
            return $"{from} -> {to}";
        }
    }

    /// <summary>
    /// Represents a circular dependency chain detected in the asset graph.
    /// </summary>
    [Serializable]
    public class PCPCircularDependency
    {
        /// <summary>
        /// The ordered list of asset paths forming the cycle.
        /// The last element depends on the first, closing the loop.
        /// </summary>
        public List<string> chain = new List<string>();

        public override string ToString()
        {
            return string.Join(" -> ", chain) + " -> " +
                   (chain.Count > 0 ? chain[0] : "?");
        }
    }

    /// <summary>
    /// Module 4 - Dependency Graph Analysis.
    /// Analyses the project's asset dependency graph to detect circular dependencies,
    /// orphan assets, and provides sub-graph extraction for visualization.
    /// </summary>
    public sealed class PCPDependencyModule : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override string ModuleId => "dependencies";
        public override string DisplayName => "Dependencies";
        public override string Icon => "\u2B83"; // ⮃
        public override Color AccentColor => new Color(0.161f, 0.502f, 0.725f, 1f); // #2980B9

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        private readonly List<PCPCircularDependency> _circularDeps = new List<PCPCircularDependency>();
        private readonly List<string> _orphanAssets = new List<string>();

        /// <summary>Circular dependency chains detected in the asset graph.</summary>
        public IReadOnlyList<PCPCircularDependency> CircularDependencies => _circularDeps;

        /// <summary>Assets with zero incoming edges (nothing depends on them and they are not roots).</summary>
        public IReadOnlyList<string> OrphanAssets => _orphanAssets;

        public override int FindingCount => _circularDeps.Count + _orphanAssets.Count;

        public override long TotalSizeBytes => 0L;

        // ----------------------------------------------------------------
        // Cached resolver reference
        // ----------------------------------------------------------------

        private PCPDependencyResolver _resolver;

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override void DoScan(PCPScanContext context)
        {
            _circularDeps.Clear();
            _orphanAssets.Clear();
            _resolver = context.DependencyResolver;

            // ----------------------------------------------------------
            // Phase 1: Ensure the dependency graph is built
            // ----------------------------------------------------------
            ReportProgress(0f, "Building dependency graph...");

            if (!_resolver.IsBuilt)
            {
                var roots = new HashSet<string>(StringComparer.Ordinal);

                // Include scenes as roots — all project scenes or just build scenes.
                if (context.Settings.includeAllScenes)
                {
                    string[] allScenes = PCPAssetUtils.GetAllScenePaths();
                    for (int i = 0; i < allScenes.Length; i++)
                        roots.Add(allScenes[i]);
                }
                else
                {
                    string[] buildScenePaths = PCPAssetUtils.GetBuildScenePaths();
                    for (int i = 0; i < buildScenePaths.Length; i++)
                        roots.Add(buildScenePaths[i]);
                }

                // Include Addressable entries as roots.
                if (context.Settings.includeAddressables && PCPAddressablesBridge.HasAddressables)
                {
                    var addressableRoots = PCPAddressablesBridge.GetRoots();
                    for (int i = 0; i < addressableRoots.Count; i++)
                        roots.Add(addressableRoots[i]);
                }

                _resolver.Build(roots, (p, label) =>
                {
                    ReportProgress(p * 0.4f, label);
                });
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 2: Detect circular dependencies
            // ----------------------------------------------------------
            ReportProgress(0.4f, "Detecting circular dependencies...");
            DetectCircularDependencies();

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 3: Find orphan assets
            // ----------------------------------------------------------
            ReportProgress(0.7f, "Finding orphan assets...");
            FindOrphans(context);

            ReportProgress(1f, $"Found {_circularDeps.Count} cycles, {_orphanAssets.Count} orphans.");
        }

        // ----------------------------------------------------------------
        // Circular dependency detection via DFS
        // ----------------------------------------------------------------

        /// <summary>
        /// Detects circular dependencies in the asset graph using iterative DFS
        /// with three-color marking (white/gray/black).
        /// </summary>
        private void DetectCircularDependencies()
        {
            var allAssets = _resolver.GetAllAssets();
            if (allAssets == null || allAssets.Count == 0)
                return;

            // State: 0 = white (unvisited), 1 = gray (on stack), 2 = black (done).
            var color = new Dictionary<string, int>(StringComparer.Ordinal);
            var parent = new Dictionary<string, string>(StringComparer.Ordinal);

            // Track found cycles to avoid excessive duplicates.
            var foundCycleKeys = new HashSet<string>(StringComparer.Ordinal);
            int maxCycles = 100; // Cap to avoid overwhelming results.

            foreach (string asset in allAssets)
            {
                color[asset] = 0;
            }

            foreach (string asset in allAssets)
            {
                if (ShouldCancel()) return;
                if (_circularDeps.Count >= maxCycles) return;

                if (color[asset] == 0)
                {
                    DFSVisit(asset, color, parent, foundCycleKeys, maxCycles);
                }
            }
        }

        private void DFSVisit(
            string start,
            Dictionary<string, int> color,
            Dictionary<string, string> parent,
            HashSet<string> foundCycleKeys,
            int maxCycles)
        {
            var stack = new Stack<DFSFrame>();
            stack.Push(new DFSFrame { asset = start, deps = null, index = 0 });
            color[start] = 1; // Gray.

            while (stack.Count > 0)
            {
                if (ShouldCancel()) return;
                if (_circularDeps.Count >= maxCycles) return;

                var frame = stack.Peek();

                // Lazy-load dependencies on first visit.
                if (frame.deps == null)
                {
                    var depsCollection = _resolver.GetDependencies(frame.asset);
                    frame.deps = new List<string>(depsCollection);
                }

                if (frame.index < frame.deps.Count)
                {
                    string dep = frame.deps[frame.index];
                    frame.index++;

                    if (!color.TryGetValue(dep, out int depColor))
                    {
                        // dep is not in our tracked set (e.g. outside Assets/).
                        continue;
                    }

                    if (depColor == 0)
                    {
                        // White: unvisited, recurse.
                        color[dep] = 1;
                        parent[dep] = frame.asset;
                        stack.Push(new DFSFrame { asset = dep, deps = null, index = 0 });
                    }
                    else if (depColor == 1)
                    {
                        // Gray: back edge found -> cycle!
                        ExtractCycle(frame.asset, dep, parent, foundCycleKeys);
                    }
                    // Black: already fully processed, skip.
                }
                else
                {
                    // Done with this node.
                    color[frame.asset] = 2; // Black.
                    stack.Pop();
                }
            }
        }

        /// <summary>
        /// Extracts the cycle path from parent map when a back-edge is detected.
        /// </summary>
        private void ExtractCycle(
            string from,
            string to,
            Dictionary<string, string> parent,
            HashSet<string> foundCycleKeys)
        {
            var chain = new List<string>();
            chain.Add(to);

            string current = from;
            int safetyCounter = 0;

            while (!string.Equals(current, to, StringComparison.Ordinal) && safetyCounter < 1000)
            {
                chain.Add(current);
                if (!parent.TryGetValue(current, out string p))
                    break;
                current = p;
                safetyCounter++;
            }

            chain.Reverse();

            // Build a canonical key to deduplicate cycles.
            // Rotate so that the lexicographically smallest element is first.
            int minIdx = 0;
            for (int i = 1; i < chain.Count; i++)
            {
                if (string.Compare(chain[i], chain[minIdx], StringComparison.Ordinal) < 0)
                    minIdx = i;
            }

            var rotated = new List<string>(chain.Count);
            for (int i = 0; i < chain.Count; i++)
                rotated.Add(chain[(i + minIdx) % chain.Count]);

            string cycleKey = string.Join("|", rotated);
            if (foundCycleKeys.Contains(cycleKey))
                return;

            foundCycleKeys.Add(cycleKey);

            _circularDeps.Add(new PCPCircularDependency { chain = chain });
        }

        // ----------------------------------------------------------------
        // Orphan detection
        // ----------------------------------------------------------------

        /// <summary>
        /// Finds assets that have zero incoming dependency edges (nothing references them)
        /// and that are not build-scene roots or Resources assets.
        /// </summary>
        private void FindOrphans(PCPScanContext context)
        {
            var allAssets = _resolver.GetAllAssets();
            if (allAssets == null)
                return;

            // Build the set of root assets that should not be flagged as orphans.
            var roots = new HashSet<string>(PCPAssetUtils.GetBuildScenePaths(), StringComparer.Ordinal);

            foreach (string asset in allAssets)
            {
                if (ShouldCancel()) return;

                // Skip ignored assets.
                if (IsIgnored(asset, context))
                    continue;

                // Skip known root types.
                if (roots.Contains(asset))
                    continue;

                // Skip Resources assets.
                if (PCPAssetUtils.IsResourcesPath(asset))
                    continue;

                // Skip scripts and editor-only assets.
                string ext = System.IO.Path.GetExtension(asset);
                if (string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".asmdef", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".asmref", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!context.Settings.scanEditorAssets && PCPAssetUtils.IsEditorOnlyPath(asset))
                    continue;

                // Check if anything depends on this asset.
                var dependents = _resolver.GetDependents(asset);
                if (dependents == null || dependents.Count == 0)
                {
                    _orphanAssets.Add(asset);
                }
            }

            _orphanAssets.Sort(StringComparer.Ordinal);
        }

        // ----------------------------------------------------------------
        // Sub-graph extraction for visualization
        // ----------------------------------------------------------------

        /// <summary>
        /// Extracts a sub-graph centered on <paramref name="centerAsset"/> up to
        /// <paramref name="depth"/> levels in both directions (dependencies and dependents).
        /// Returns a tuple of nodes and edges for visualization.
        /// </summary>
        /// <param name="centerAsset">The asset path to center the graph on.</param>
        /// <param name="depth">Maximum BFS depth from the center node.</param>
        /// <param name="nodes">Output list of graph nodes.</param>
        /// <param name="edges">Output list of graph edges.</param>
        public void GetSubgraph(string centerAsset, int depth,
            out List<PCPGraphNode> nodes, out List<PCPGraphEdge> edges)
        {
            nodes = new List<PCPGraphNode>();
            edges = new List<PCPGraphEdge>();

            if (_resolver == null || string.IsNullOrEmpty(centerAsset))
                return;

            var visited = new Dictionary<string, int>(StringComparer.Ordinal);
            var queue = new Queue<KeyValuePair<string, int>>();

            visited[centerAsset] = 0;
            queue.Enqueue(new KeyValuePair<string, int>(centerAsset, 0));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                string asset = current.Key;
                int currentDepth = current.Value;

                // Create node.
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(asset);
                nodes.Add(new PCPGraphNode
                {
                    assetPath = asset,
                    assetType = assetType != null ? assetType.Name : "Unknown",
                    displayName = System.IO.Path.GetFileNameWithoutExtension(asset),
                    depth = currentDepth
                });

                if (currentDepth >= depth)
                    continue;

                // Forward edges: what this asset depends on.
                var deps = _resolver.GetDependencies(asset);
                foreach (string dep in deps)
                {
                    edges.Add(new PCPGraphEdge { from = asset, to = dep });

                    if (!visited.ContainsKey(dep))
                    {
                        visited[dep] = currentDepth + 1;
                        queue.Enqueue(new KeyValuePair<string, int>(dep, currentDepth + 1));
                    }
                }

                // Reverse edges: what depends on this asset.
                var dependents = _resolver.GetDependents(asset);
                foreach (string dependent in dependents)
                {
                    edges.Add(new PCPGraphEdge { from = dependent, to = asset });

                    if (!visited.ContainsKey(dependent))
                    {
                        visited[dependent] = currentDepth + 1;
                        queue.Enqueue(new KeyValuePair<string, int>(dependent, currentDepth + 1));
                    }
                }
            }
        }

        public override void Clear()
        {
            base.Clear();
            _circularDeps.Clear();
            _orphanAssets.Clear();
            _resolver = null;
        }

        // ----------------------------------------------------------------
        // Internal types
        // ----------------------------------------------------------------

        private class DFSFrame
        {
            public string asset;
            public List<string> deps;
            public int index;
        }
    }
}
