using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Builds a full asset dependency graph (forward and reverse) and computes
    /// reachability from a set of root assets via BFS.
    /// </summary>
    public sealed class PCPDependencyResolver
    {
        // Forward edges: asset -> set of assets it depends on.
        private readonly Dictionary<string, HashSet<string>> m_Forward =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Reverse edges: asset -> set of assets that depend on it.
        private readonly Dictionary<string, HashSet<string>> m_Reverse =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // The set of assets reachable from the roots determined during the last Build().
        private readonly HashSet<string> m_Reachable =
            new HashSet<string>(StringComparer.Ordinal);

        // All asset paths that were part of the graph.
        private readonly HashSet<string> m_AllAssets =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the graph has been built at least once.
        /// </summary>
        public bool IsBuilt { get; private set; }

        /// <summary>
        /// Total number of assets in the graph.
        /// </summary>
        public int AssetCount => m_AllAssets.Count;

        /// <summary>
        /// Total number of reachable assets after the last Build().
        /// </summary>
        public int ReachableCount => m_Reachable.Count;

        // ----------------------------------------------------------------
        // Building
        // ----------------------------------------------------------------

        /// <summary>
        /// Builds the dependency graph for every asset under Assets/ and then
        /// runs BFS from <paramref name="roots"/> to compute the reachable set.
        /// </summary>
        /// <param name="roots">
        /// Root asset paths (e.g. scenes in build settings, addressable entries).
        /// </param>
        /// <param name="onProgress">
        /// Optional progress callback: (progress 0-1, description).
        /// </param>
        public void Build(IEnumerable<string> roots, Action<float, string> onProgress = null)
        {
            Clear();

            // -- Phase 1: Gather all asset paths under Assets/ --
            onProgress?.Invoke(0f, "Gathering asset paths...");

            string[] allPaths = AssetDatabase.GetAllAssetPaths()
                .Where(IsValidAssetPath)
                .ToArray();

            int total = allPaths.Length;
            if (total == 0)
            {
                IsBuilt = true;
                return;
            }

            // -- Phase 2: Build forward and reverse adjacency lists --
            for (int i = 0; i < total; i++)
            {
                string path = allPaths[i];
                m_AllAssets.Add(path);

                // Report progress every 64 assets to avoid overhead.
                if ((i & 63) == 0)
                {
                    float progress = (float)i / total * 0.8f; // 0 – 0.8
                    onProgress?.Invoke(progress, $"Resolving dependencies ({i}/{total})...");
                }

                // Direct (non-recursive) dependencies.
                string[] deps = AssetDatabase.GetDependencies(path, false);

                HashSet<string> forwardSet = GetOrCreateSet(m_Forward, path);

                foreach (string dep in deps)
                {
                    if (!IsValidAssetPath(dep) || string.Equals(dep, path, StringComparison.Ordinal))
                        continue;

                    forwardSet.Add(dep);
                    m_AllAssets.Add(dep);

                    // Reverse edge.
                    GetOrCreateSet(m_Reverse, dep).Add(path);
                }
            }

            // -- Phase 3: BFS from roots --
            onProgress?.Invoke(0.8f, "Computing reachable set...");

            Queue<string> queue = new Queue<string>();

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root))
                    continue;

                if (m_Reachable.Add(root))
                    queue.Enqueue(root);
            }

            int visited = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                visited++;

                if ((visited & 127) == 0)
                {
                    float progress = 0.8f + 0.2f * Mathf.Min((float)visited / (m_AllAssets.Count + 1), 1f);
                    onProgress?.Invoke(progress, $"BFS reachability ({visited} visited)...");
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

            onProgress?.Invoke(1f, "Dependency graph complete.");
            IsBuilt = true;
        }

        // ----------------------------------------------------------------
        // Queries
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the direct dependencies of <paramref name="assetPath"/>.
        /// </summary>
        public IReadOnlyCollection<string> GetDependencies(string assetPath)
        {
            if (m_Forward.TryGetValue(assetPath, out HashSet<string> deps))
                return deps;
            return Array.Empty<string>();
        }

        /// <summary>
        /// Returns the assets that directly depend on <paramref name="assetPath"/>.
        /// </summary>
        public IReadOnlyCollection<string> GetDependents(string assetPath)
        {
            if (m_Reverse.TryGetValue(assetPath, out HashSet<string> dependents))
                return dependents;
            return Array.Empty<string>();
        }

        /// <summary>
        /// Whether <paramref name="assetPath"/> is reachable from the roots.
        /// </summary>
        public bool IsReachable(string assetPath)
        {
            return m_Reachable.Contains(assetPath);
        }

        /// <summary>
        /// Returns every asset path reachable from the roots.
        /// </summary>
        public IReadOnlyCollection<string> GetAllReachable()
        {
            return m_Reachable;
        }

        /// <summary>
        /// Returns every asset path that is NOT reachable from the roots.
        /// </summary>
        public IEnumerable<string> GetAllUnreachable()
        {
            foreach (string path in m_AllAssets)
            {
                if (!m_Reachable.Contains(path))
                    yield return path;
            }
        }

        /// <summary>
        /// Returns all known asset paths in the graph.
        /// </summary>
        public IReadOnlyCollection<string> GetAllAssets()
        {
            return m_AllAssets;
        }

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        /// <summary>
        /// Clears all graph data.
        /// </summary>
        public void Clear()
        {
            m_Forward.Clear();
            m_Reverse.Clear();
            m_Reachable.Clear();
            m_AllAssets.Clear();
            IsBuilt = false;
        }

        private static bool IsValidAssetPath(string path)
        {
            // Only include assets under the Assets/ folder; skip Packages/ and built-in resources.
            return !string.IsNullOrEmpty(path)
                && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !AssetDatabase.IsValidFolder(path);
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

        // Mathf may not be available in all contexts; provide a minimal helper.
        private static class Mathf
        {
            public static float Min(float a, float b) => a < b ? a : b;
        }
    }
}
