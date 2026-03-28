using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ProjectCleanPro.Editor.Core
{
    public abstract class PCPDependencyResolverBase : IPCPDependencyResolver
    {
        protected readonly ConcurrentDictionary<string, HashSet<string>> m_Forward = new();
        protected readonly ConcurrentDictionary<string, HashSet<string>> m_Reverse = new();
        protected HashSet<string> m_Reachable = new(StringComparer.Ordinal);
        protected readonly ConcurrentDictionary<string, byte> m_AllAssets = new();
        protected bool m_IsBuilt;

        private const int GraphFormatVersion = 3;
        private static readonly string s_GraphPath =
            Path.Combine(PCPScanCache.CacheDirectory, "DepGraph.bin");

        public bool IsBuilt => m_IsBuilt;
        public int AssetCount => m_AllAssets.Count;
        public int ReachableCount => m_Reachable.Count;

        public abstract Task BuildGraphAsync(PCPScanContext context, CancellationToken ct);

        protected void UpdateEdges(string asset, IEnumerable<string> dependencies)
        {
            m_AllAssets[asset] = 0;

            if (m_Forward.TryGetValue(asset, out var oldDeps))
            {
                foreach (var dep in oldDeps)
                {
                    if (m_Reverse.TryGetValue(dep, out var rev))
                        lock (rev) { rev.Remove(asset); }
                }
            }

            var newDeps = new HashSet<string>(dependencies, StringComparer.Ordinal);
            m_Forward[asset] = newDeps;

            foreach (var dep in newDeps)
            {
                m_AllAssets[dep] = 0;
                var rev = m_Reverse.GetOrAdd(dep, _ => new HashSet<string>(StringComparer.Ordinal));
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
                        lock (rev) { rev.Remove(asset); }
                }
            }
            if (m_Reverse.TryRemove(asset, out var dependents))
            {
                foreach (var dependent in dependents)
                {
                    if (m_Forward.TryGetValue(dependent, out var fwd))
                        lock (fwd) { fwd.Remove(asset); }
                }
            }
            m_AllAssets.TryRemove(asset, out _);
        }

        protected void ComputeReachability(IEnumerable<string> roots)
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();

            foreach (var root in roots)
            {
                if (!string.IsNullOrEmpty(root) && reachable.Add(root))
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

        // Queries
        public IReadOnlyCollection<string> GetReachableAssets() => m_Reachable;
        public bool IsReachable(string path) => m_Reachable.Contains(path);
        public IEnumerable<string> GetAllUnreachable() => m_AllAssets.Keys.Where(a => !m_Reachable.Contains(a));
        public IReadOnlyCollection<string> GetAllAssets() => (IReadOnlyCollection<string>)m_AllAssets.Keys;
        public int GetDependentCount(string path) => m_Reverse.TryGetValue(path, out var set) ? set.Count : 0;
        public IReadOnlyCollection<string> GetDependencies(string path) =>
            m_Forward.TryGetValue(path, out var set) ? set : (IReadOnlyCollection<string>)Array.Empty<string>();
        public IReadOnlyCollection<string> GetDependents(string path) =>
            m_Reverse.TryGetValue(path, out var set) ? set : (IReadOnlyCollection<string>)Array.Empty<string>();

        // Persistence
        public void SaveToDisk()
        {
            try
            {
                PCPCacheIO.AtomicWrite(s_GraphPath, GraphFormatVersion, writer =>
                {
                    var snapshot = m_Forward.ToArray();
                    writer.Write(snapshot.Length);
                    foreach (var (asset, deps) in snapshot)
                    {
                        writer.Write(asset);
                        writer.Write(deps.Count);
                        foreach (var dep in deps)
                            writer.Write(dep);
                    }

                    writer.Write(m_Reachable.Count);
                    foreach (var r in m_Reachable)
                        writer.Write(r);
                });
                PCPSettings.Log($"[ProjectCleanPro] Dependency graph saved: {m_Forward.Count} assets, " +
                          $"{m_Reachable.Count} reachable.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to save dependency graph: {ex.Message}");
            }
        }

        public bool LoadFromDisk()
        {
            bool loaded = PCPCacheIO.SafeRead(s_GraphPath, GraphFormatVersion, reader =>
            {
                Clear();

                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var asset = reader.ReadString();
                    int depCount = reader.ReadInt32();
                    var deps = new HashSet<string>(depCount, StringComparer.Ordinal);
                    for (int j = 0; j < depCount; j++)
                        deps.Add(reader.ReadString());

                    m_Forward[asset] = deps;
                    m_AllAssets[asset] = 0;

                    foreach (var dep in deps)
                    {
                        m_AllAssets[dep] = 0;
                        var rev = m_Reverse.GetOrAdd(dep, _ => new HashSet<string>(StringComparer.Ordinal));
                        lock (rev) { rev.Add(asset); }
                    }
                }

                int reachCount = reader.ReadInt32();
                var reachable = new HashSet<string>(reachCount, StringComparer.Ordinal);
                for (int i = 0; i < reachCount; i++)
                    reachable.Add(reader.ReadString());
                m_Reachable = reachable;

                return true;
            }, out bool success);

            if (loaded && success)
            {
                m_IsBuilt = true;
                PCPSettings.Log($"[ProjectCleanPro] Dependency graph loaded from cache: " +
                          $"{m_Forward.Count} assets, {m_Reachable.Count} reachable.");
                return true;
            }

            PCPSettings.Log("[ProjectCleanPro] Dependency graph: no valid cache found, will rebuild.");
            Clear();
            return false;
        }

        public void Clear()
        {
            m_Forward.Clear();
            m_Reverse.Clear();
            m_Reachable = new HashSet<string>(StringComparer.Ordinal);
            m_AllAssets.Clear();
            m_IsBuilt = false;
        }

        /// <summary>
        /// Deletes the persisted dependency graph file from disk.
        /// </summary>
        public static void DeleteGraphFile()
        {
            try
            {
                if (File.Exists(s_GraphPath))
                    File.Delete(s_GraphPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to delete dependency graph cache: {ex.Message}");
            }
        }

        protected static HashSet<string> CollectRoots(PCPScanContext context)
        {
            return PCPScanOrchestrator.CollectRoots(context);
        }
    }
}
