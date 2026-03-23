using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Builds a full asset dependency graph (forward and reverse) and computes
    /// reachability from root assets via BFS.
    /// <para>
    /// v2 changes: persists the reachable set to disk alongside forward edges,
    /// supports async Build with editor yielding, and skips BFS entirely when
    /// no edges changed and reachability was loaded from disk.
    /// </para>
    /// </summary>
    public sealed class PCPDependencyResolver
    {
        // Forward edges: asset -> set of assets it depends on.
        private readonly Dictionary<string, HashSet<string>> m_Forward =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Reverse edges: asset -> set of assets that depend on it.
        private readonly Dictionary<string, HashSet<string>> m_Reverse =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Reachable set from the last Build().
        private readonly HashSet<string> m_Reachable =
            new HashSet<string>(StringComparer.Ordinal);

        // All asset paths in the graph.
        private readonly HashSet<string> m_AllAssets =
            new HashSet<string>(StringComparer.Ordinal);

        // Whether the reachable set loaded from disk is still valid.
        private bool m_ReachabilityValid;

        private static readonly string s_GraphPath =
            Path.Combine(PCPScanCache.CacheDirectory, "DepGraph.bin");

        private const int GraphFormatVersion = 2;

        public bool IsBuilt { get; private set; }
        public int AssetCount => m_AllAssets.Count;
        public int ReachableCount => m_Reachable.Count;

        // ----------------------------------------------------------------
        // Async build
        // ----------------------------------------------------------------

        /// <summary>
        /// Builds/updates the dependency graph and computes reachability via BFS.
        /// Yields to the editor every 64 assets so the UI stays responsive.
        /// <para>
        /// When the graph was previously built and a cache is available, only
        /// stale/new assets have their dependencies re-queried (incremental).
        /// When no edges changed and the reachable set was loaded from disk,
        /// BFS is skipped entirely (O(1)).
        /// </para>
        /// </summary>
        public async Task BuildAsync(IEnumerable<string> roots,
            Action<float, string> onProgress = null,
            PCPScanCache cache = null,
            string[] allAssetPaths = null,
            CancellationToken ct = default)
        {
            bool incremental = IsBuilt && cache != null;

            if (!incremental)
            {
                ClearGraph();
            }
            else
            {
                m_Reachable.Clear();
            }

            // -- Phase 1: Gather assets --
            onProgress?.Invoke(0f, "Gathering asset paths...");

            string[] allPaths = allAssetPaths ?? AssetDatabase.GetAllAssetPaths()
                .Where(PCPAssetUtils.IsValidAssetPath)
                .ToArray();

            int total = allPaths.Length;
            if (total == 0)
            {
                IsBuilt = true;
                return;
            }

            // -- Phase 2: Build/update adjacency lists --
            bool edgesChanged = await UpdateEdgesAsync(
                allPaths, incremental, cache, onProgress, ct);

            // -- Phase 3: BFS reachability --
            if (!edgesChanged && m_ReachabilityValid && m_Reachable.Count > 0)
            {
                // Reachable set persisted from disk is still valid. O(1) skip.
                onProgress?.Invoke(1f, "Dependency graph complete (reachability cached).");
                IsBuilt = true;
                return;
            }

            m_Reachable.Clear();
            await ComputeReachabilityAsync(roots, onProgress, ct);

            m_ReachabilityValid = true;
            IsBuilt = true;
            SaveToDisk();

            onProgress?.Invoke(1f, "Dependency graph complete.");
        }

        /// <summary>
        /// Synchronous build for backward compatibility and batch mode.
        /// </summary>
        public void Build(IEnumerable<string> roots, Action<float, string> onProgress = null,
            PCPScanCache cache = null, string[] allAssetPaths = null)
        {
            PCPEditorAsync.RunSync(() => BuildAsync(roots, onProgress, cache, allAssetPaths));
        }

        // ----------------------------------------------------------------
        // Phase 2: Edge update (returns true if any edge changed)
        // ----------------------------------------------------------------

        private async Task<bool> UpdateEdgesAsync(string[] allPaths, bool incremental,
            PCPScanCache cache, Action<float, string> onProgress, CancellationToken ct)
        {
            int total = allPaths.Length;
            bool anyEdgeChanged = false;

            if (incremental)
            {
                // Remove edges for deleted assets.
                var currentSet = new HashSet<string>(allPaths, StringComparer.Ordinal);
                var toRemove = new List<string>();
                foreach (string asset in m_AllAssets)
                {
                    if (!currentSet.Contains(asset) && m_Forward.ContainsKey(asset))
                        toRemove.Add(asset);
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    RemoveAssetEdges(toRemove[i]);
                    anyEdgeChanged = true;
                }

                int processed = 0;
                for (int i = 0; i < total; i++)
                {
                    string path = allPaths[i];
                    m_AllAssets.Add(path);

                    if (!cache.IsStale(path) && m_Forward.ContainsKey(path))
                        continue;

                    processed++;
                    anyEdgeChanged = true;

                    if ((processed & 63) == 0)
                    {
                        onProgress?.Invoke((float)i / total * 0.8f,
                            $"Updating dependencies ({processed} changed)...");
                        ct.ThrowIfCancellationRequested();
                        await PCPEditorAsync.YieldToEditor();
                    }

                    RemoveAssetEdges(path);
                    m_AllAssets.Add(path);

                    string[] deps = !cache.IsStale(path) ? cache.GetDependencies(path) : null;
                    if (deps == null)
                    {
                        deps = AssetDatabase.GetDependencies(path, false);
                        cache.SetDependencies(path, deps);
                    }

                    AddEdges(path, deps);
                }
            }
            else
            {
                anyEdgeChanged = true;
                for (int i = 0; i < total; i++)
                {
                    string path = allPaths[i];
                    m_AllAssets.Add(path);

                    if ((i & 63) == 0)
                    {
                        onProgress?.Invoke((float)i / total * 0.8f,
                            $"Resolving dependencies ({i}/{total})...");
                        ct.ThrowIfCancellationRequested();
                        await PCPEditorAsync.YieldToEditor();
                    }

                    string[] deps = null;
                    if (cache != null && !cache.IsStale(path))
                        deps = cache.GetDependencies(path);
                    if (deps == null)
                    {
                        deps = AssetDatabase.GetDependencies(path, false);
                        cache?.SetDependencies(path, deps);
                    }

                    AddEdges(path, deps);
                }
            }

            return anyEdgeChanged;
        }

        private void AddEdges(string path, string[] deps)
        {
            HashSet<string> forwardSet = GetOrCreateSet(m_Forward, path);
            foreach (string dep in deps)
            {
                if (!PCPAssetUtils.IsValidAssetPath(dep) ||
                    string.Equals(dep, path, StringComparison.Ordinal))
                    continue;

                forwardSet.Add(dep);
                m_AllAssets.Add(dep);
                GetOrCreateSet(m_Reverse, dep).Add(path);
            }
        }

        // ----------------------------------------------------------------
        // Phase 3: BFS reachability (async, yields every 128 visits)
        // ----------------------------------------------------------------

        private async Task ComputeReachabilityAsync(IEnumerable<string> roots,
            Action<float, string> onProgress, CancellationToken ct)
        {
            onProgress?.Invoke(0.8f, "Computing reachable set...");

            var queue = new Queue<string>();
            foreach (string root in roots)
            {
                if (!string.IsNullOrEmpty(root) && m_Reachable.Add(root))
                    queue.Enqueue(root);
            }

            int visited = 0;
            int assetCount = m_AllAssets.Count + 1;

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                visited++;

                if ((visited & 127) == 0)
                {
                    float progress = 0.8f + 0.2f * Mathf.Min((float)visited / assetCount, 1f);
                    onProgress?.Invoke(progress, $"BFS reachability ({visited} visited)...");
                    ct.ThrowIfCancellationRequested();
                    await PCPEditorAsync.YieldToEditor();
                }

                if (m_Forward.TryGetValue(current, out HashSet<string> deps))
                {
                    foreach (string dep in deps)
                    {
                        if (m_Reachable.Add(dep))
                            queue.Enqueue(dep);
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // Edge removal
        // ----------------------------------------------------------------

        internal void RemoveAssets(IEnumerable<string> paths)
        {
            foreach (string path in paths)
                RemoveAssetEdges(path);
        }

        internal void RemoveAssetEdges(string assetPath)
        {
            if (m_Forward.TryGetValue(assetPath, out HashSet<string> forwardDeps))
            {
                foreach (string dep in forwardDeps)
                {
                    if (m_Reverse.TryGetValue(dep, out HashSet<string> revSet))
                        revSet.Remove(assetPath);
                }
                m_Forward.Remove(assetPath);
            }

            if (m_Reverse.TryGetValue(assetPath, out HashSet<string> reverseDeps))
            {
                foreach (string dep in reverseDeps)
                {
                    if (m_Forward.TryGetValue(dep, out HashSet<string> fwdSet))
                        fwdSet.Remove(assetPath);
                }
                m_Reverse.Remove(assetPath);
            }

            m_AllAssets.Remove(assetPath);
        }

        // ----------------------------------------------------------------
        // Disk persistence (v2: forward edges + reachable set)
        // ----------------------------------------------------------------

        public void SaveToDisk()
        {
            try
            {
                PCPCacheIO.AtomicWrite(s_GraphPath, GraphFormatVersion, writer =>
                {
                    // Forward adjacency list.
                    writer.Write(m_Forward.Count);
                    foreach (var kvp in m_Forward)
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Count);
                        foreach (string dep in kvp.Value)
                            writer.Write(dep);
                    }

                    // Reachable set (new in v2).
                    writer.Write(m_Reachable.Count);
                    foreach (string path in m_Reachable)
                        writer.Write(path);
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to save dependency graph: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads forward edges and reachable set from disk. Reverse edges are
        /// rebuilt from forward edges (O(E), no Unity API calls).
        /// Returns true on success.
        /// </summary>
        public bool LoadFromDisk()
        {
            bool loaded = PCPCacheIO.SafeRead(s_GraphPath, GraphFormatVersion, reader =>
            {
                ClearGraph();

                // Forward adjacency list.
                int forwardCount = reader.ReadInt32();
                for (int i = 0; i < forwardCount; i++)
                {
                    string path = reader.ReadString();
                    int depCount = reader.ReadInt32();
                    var fwdSet = GetOrCreateSet(m_Forward, path);
                    m_AllAssets.Add(path);

                    for (int j = 0; j < depCount; j++)
                    {
                        string dep = reader.ReadString();
                        fwdSet.Add(dep);
                        m_AllAssets.Add(dep);
                        GetOrCreateSet(m_Reverse, dep).Add(path);
                    }
                }

                // Reachable set (new in v2).
                int reachableCount = reader.ReadInt32();
                for (int i = 0; i < reachableCount; i++)
                    m_Reachable.Add(reader.ReadString());

                return true;
            }, out bool success);

            if (loaded && success)
            {
                IsBuilt = true;
                m_ReachabilityValid = m_Reachable.Count > 0;
                return true;
            }

            ClearGraph();
            return false;
        }

        // ----------------------------------------------------------------
        // Queries
        // ----------------------------------------------------------------

        public IReadOnlyCollection<string> GetDependencies(string assetPath)
        {
            if (m_Forward.TryGetValue(assetPath, out HashSet<string> deps))
                return deps;
            return Array.Empty<string>();
        }

        public IReadOnlyCollection<string> GetDependents(string assetPath)
        {
            if (m_Reverse.TryGetValue(assetPath, out HashSet<string> dependents))
                return dependents;
            return Array.Empty<string>();
        }

        public bool IsReachable(string assetPath) => m_Reachable.Contains(assetPath);

        public IReadOnlyCollection<string> GetAllReachable() => m_Reachable;

        public IEnumerable<string> GetAllUnreachable()
        {
            foreach (string path in m_AllAssets)
            {
                if (!m_Reachable.Contains(path))
                    yield return path;
            }
        }

        public IReadOnlyCollection<string> GetAllAssets() => m_AllAssets;

        // ----------------------------------------------------------------
        // Clear
        // ----------------------------------------------------------------

        public void Clear()
        {
            ClearGraph();
        }

        private void ClearGraph()
        {
            m_Forward.Clear();
            m_Reverse.Clear();
            m_Reachable.Clear();
            m_AllAssets.Clear();
            m_ReachabilityValid = false;
            IsBuilt = false;
        }

        private static HashSet<string> GetOrCreateSet(
            Dictionary<string, HashSet<string>> dict, string key)
        {
            if (!dict.TryGetValue(key, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                dict[key] = set;
            }
            return set;
        }
    }
}
