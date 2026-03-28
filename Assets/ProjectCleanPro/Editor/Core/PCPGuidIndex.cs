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
    public sealed class PCPGuidIndex
    {
        private readonly ConcurrentDictionary<string, string> m_GuidToPath = new();
        private readonly ConcurrentDictionary<string, string> m_PathToGuid = new();

        /// <summary>
        /// Builds or incrementally updates the GUID index.
        /// If changedFiles is null, all metaFiles are read (full build).
        /// If changedFiles is non-null, only those are re-read, and entries for
        /// .meta files no longer in metaFiles are pruned.
        /// </summary>
        public async Task BuildAsync(
            IReadOnlyList<string> metaFiles,
            ICollection<string> changedFiles,
            CancellationToken ct)
        {
            if (changedFiles != null)
            {
                // Prune deleted entries
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
                : metaFiles.Where(f =>
                {
                    // changedFiles contains asset paths (e.g. "Assets/foo.png")
                    // but metaFiles contains .meta paths (e.g. "Assets/foo.png.meta")
                    // Strip the .meta suffix before checking membership.
                    var assetPath = f.EndsWith(".meta") ? f.Substring(0, f.Length - 5) : f;
                    return changedFiles.Contains(assetPath);
                }).ToList();

            bool isIncremental = changedFiles != null;
            PCPSettings.Log($"[ProjectCleanPro] GUID index: {(isIncremental ? "incremental" : "full")} " +
                      $"build — processing {toProcess.Count} .meta file(s)...");

            await PCPThreading.ParallelForEachAsync(toProcess, (metaPath, token) =>
            {
                var guid = ReadGuidFromMeta(metaPath);
                if (guid != null)
                {
                    var assetPath = metaPath.Substring(0, metaPath.Length - 5);
                    m_GuidToPath[guid] = assetPath;
                    m_PathToGuid[assetPath] = guid;
                }
                return Task.CompletedTask;
            }, PCPThreading.DefaultConcurrency, ct);

            PCPSettings.Log($"[ProjectCleanPro] GUID index ready: {m_GuidToPath.Count} entries.");
        }

        public string Resolve(string guid) =>
            m_GuidToPath.TryGetValue(guid, out var path) ? path : null;

        public HashSet<string> ResolveAll(IEnumerable<string> guids) =>
            new HashSet<string>(guids.Select(Resolve).Where(p => p != null));

        public int Count => m_GuidToPath.Count;

        /// <summary>
        /// Reads a .meta file to extract the guid. Sync I/O — .meta files are
        /// tiny (&lt; 500 bytes) so ReadAllText is optimal. Called from threadpool.
        /// </summary>
        private static string ReadGuidFromMeta(string metaPath)
        {
            try
            {
                var text = File.ReadAllText(metaPath);
                const string prefix = "guid: ";
                int idx = text.IndexOf(prefix, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int guidStart = idx + prefix.Length;
                    if (guidStart + 32 <= text.Length)
                    {
                        var candidate = text.Substring(guidStart, 32);
                        // GUIDs end at newline or comma; trim whitespace
                        if (candidate.Length >= 32)
                        {
                            candidate = candidate.Substring(0, 32).Trim();
                            if (candidate.Length == 32)
                                return candidate;
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return null;
        }
    }
}
