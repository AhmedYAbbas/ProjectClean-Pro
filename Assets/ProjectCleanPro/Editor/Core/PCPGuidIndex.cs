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
            new HashSet<string>(guids.Select(Resolve).Where(p => p != null));

        public int Count => m_GuidToPath.Count;

        /// <summary>
        /// Reads the first few lines of a .meta file to extract the guid.
        /// </summary>
        private static async Task<string> ReadGuidFromMetaAsync(string metaPath, CancellationToken ct)
        {
            try
            {
                using var stream = new FileStream(metaPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: 512, useAsync: true);
                using var reader = new StreamReader(stream);

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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return null;
        }
    }
}
