using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Incremental scan cache persisted to Library/ProjectCleanPro/ScanCache.bin.
    /// Tracks per-asset modification timestamps, content hashes, dependency lists,
    /// file sizes, and generic per-module metadata so that subsequent scans only
    /// re-process changed assets.
    /// <para>
    /// v3 changes: Dictionary metadata for O(1) lookup, atomic writes via
    /// <see cref="PCPCacheIO"/>, module dirtiness driven by module contracts
    /// instead of hardcoded extension mappings.
    /// </para>
    /// <para>
    /// v4 changes: ConcurrentDictionary for all internal state, async I/O via
    /// <see cref="RefreshStalenessAsync"/>, <see cref="StampProcessedAssetsAsync"/>,
    /// <see cref="SaveAsync"/>, and <see cref="LoadAsync"/>.
    /// </para>
    /// </summary>
    public sealed class PCPScanCache
    {
        // ----------------------------------------------------------------
        // Versioning
        // ----------------------------------------------------------------

        public const int CurrentVersion = 3;

        // ----------------------------------------------------------------
        // Data model
        // ----------------------------------------------------------------

        private sealed class CacheEntry
        {
            public string assetPath;
            public long lastModifiedTicks;
            public string sha256Hash;
            public string[] dependencies;
            public long fileSizeBytes;
            /// <summary>
            /// Module-specific key-value pairs. O(1) lookup (was List in v2).
            /// </summary>
            public ConcurrentDictionary<string, string> metadata;
        }

        // ----------------------------------------------------------------
        // State
        // ----------------------------------------------------------------

        private readonly ConcurrentDictionary<string, CacheEntry> m_Entries =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);

        // Pre-computed staleness — populated by RefreshStaleness() / RefreshStalenessAsync(),
        // consumed by IsStale() / IsStaleOrMetaStale() for O(1) lookups.
        private readonly ConcurrentDictionary<string, byte> m_StaleAssets = new ConcurrentDictionary<string, byte>();
        private readonly ConcurrentDictionary<string, byte> m_NewAssets = new ConcurrentDictionary<string, byte>();
        private volatile bool m_StalenessComputed;

        // Dirty flag — Save() is a no-op when false.
        // Volatile because background threads may set it via GetOrCreateEntry/StampProcessedAssetsAsync.
        private volatile bool m_Dirty;

        // Module-level dirty tracking — computed alongside staleness so
        // the orchestrator can skip unaffected modules.
        private readonly ConcurrentDictionary<PCPModuleId, bool> m_DirtyModules = new();

        private static readonly string s_CacheDirectory =
            Path.Combine(Application.dataPath, "..", "Library", "ProjectCleanPro");

        private static readonly string s_CacheFilePath =
            Path.Combine(s_CacheDirectory, "ScanCache.bin");

        // Legacy paths — cleaned up on first save.
        private static readonly string s_LegacyBinPath =
            Path.Combine(s_CacheDirectory, "ScanCache.v2.bin");
        private static readonly string s_LegacyJsonPath =
            Path.Combine(s_CacheDirectory, "ScanCache.json");

        // ----------------------------------------------------------------
        // Properties
        // ----------------------------------------------------------------

        public static string CacheDirectory => s_CacheDirectory;
        public int Count => m_Entries.Count;
        public bool HasAnyChanges => m_StaleAssets.Count > 0 || m_NewAssets.Count > 0;
        public int StaleCount => m_StaleAssets.Count + m_NewAssets.Count;

        /// <summary>
        /// Returns true if the given module needs re-running based on what
        /// file types changed since the last scan. Conservative: returns true
        /// if staleness has not been computed.
        /// </summary>
        public bool IsModuleDirty(PCPModuleId id)
        {
            return !m_StalenessComputed || m_DirtyModules.ContainsKey(id);
        }

        // ----------------------------------------------------------------
        // Batch staleness computation
        // ----------------------------------------------------------------

        /// <summary>
        /// Pre-computes staleness using <see cref="PCPAssetChangeTracker"/>.
        /// <list type="bullet">
        /// <item>No changes tracked -> instant O(1).</item>
        /// <item>Specific changes tracked -> O(changed).</item>
        /// <item>Domain reload / empty cache -> full O(N) timestamp check.</item>
        /// </list>
        /// After calling this, <see cref="IsStale"/> and <see cref="IsStaleOrMetaStale"/>
        /// use O(1) ConcurrentDictionary lookups.
        /// </summary>
        public void RefreshStaleness(string[] currentAssetPaths)
        {
            m_StaleAssets.Clear();
            m_NewAssets.Clear();

            // Fast path: nothing changed since last scan.
            if (!PCPAssetChangeTracker.HasChanges && m_Entries.Count > 0)
            {
                m_DirtyModules.Clear();
                m_StalenessComputed = true;
                PCPSettings.Log("[ProjectCleanPro] Staleness check: no changes since last scan (fast path).");
                return;
            }

            // Domain reload or first scan -> full timestamp check.
            if (PCPAssetChangeTracker.FullCheckNeeded || m_Entries.Count == 0)
            {
                PCPSettings.Log($"[ProjectCleanPro] Staleness check: full timestamp comparison for {currentAssetPaths.Length} assets...");
                RefreshStalenessFull(currentAssetPaths);
                PCPSettings.Log($"[ProjectCleanPro] Staleness result: {m_StaleAssets.Count} stale, {m_NewAssets.Count} new.");
                m_StalenessComputed = true;
                return;
            }

            // Incremental: only check tracked changes.
            var changedPaths = PCPAssetChangeTracker.ChangedAssets;
            if (changedPaths != null && changedPaths.Count > 0)
            {
                var currentSet = new HashSet<string>(currentAssetPaths, StringComparer.Ordinal);
                foreach (string path in changedPaths)
                {
                    if (currentSet.Contains(path))
                    {
                        m_StaleAssets[path] = 0;
                    }
                    else if (m_Entries.TryRemove(path, out _))
                    {
                        m_Dirty = true;
                    }
                }
                PCPSettings.Log($"[ProjectCleanPro] Staleness check (incremental): " +
                          $"{m_StaleAssets.Count} stale, {m_NewAssets.Count} new " +
                          $"out of {changedPaths.Count} tracked change(s).");
            }

            m_StalenessComputed = true;
        }

        /// <summary>
        /// Async staleness computation. Runs timestamp checks on background threads.
        /// </summary>
        public async Task RefreshStalenessAsync(PCPScanContext context, CancellationToken ct)
        {
            m_StaleAssets.Clear();
            m_NewAssets.Clear();

            bool needsFullCheck = PCPAssetChangeTracker.FullCheckNeeded || m_Entries.Count == 0;
            List<string> allAssets = null;

            if (!needsFullCheck && PCPAssetChangeTracker.HasChanges)
            {
                // Incremental: only check tracked changes.
                foreach (var path in PCPAssetChangeTracker.ChangedAssets)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!m_Entries.ContainsKey(path))
                        m_NewAssets[path] = 0;
                    else
                        m_StaleAssets[path] = 0;
                }
                PCPSettings.Log($"[ProjectCleanPro] Staleness check (incremental): " +
                          $"{m_StaleAssets.Count} stale, {m_NewAssets.Count} new.");
            }
            else if (!needsFullCheck)
            {
                // Tracker reports no changes — verify with a quick file count.
                // OnPostprocessAllAssets can miss events (e.g. external file ops
                // before Unity refreshes), so compare actual file count against
                // the cache to catch untracked additions or deletions.
                allAssets = await CollectAllAssetPathsAsync(ct);
                if (allAssets.Count != m_Entries.Count)
                {
                    needsFullCheck = true;
                    PCPSettings.Log($"[ProjectCleanPro] Staleness check: file count mismatch " +
                              $"(disk={allAssets.Count}, cache={m_Entries.Count}), forcing full check...");
                }
                else
                {
                    PCPSettings.Log("[ProjectCleanPro] Staleness check: no changes since last scan (fast path).");
                }
            }

            if (needsFullCheck)
            {
                // Full check: compare timestamps for all assets on background threads.
                // Reuse allAssets if already collected by the count check above.
                if (allAssets == null)
                    allAssets = await CollectAllAssetPathsAsync(ct);

                PCPSettings.Log($"[ProjectCleanPro] Staleness check: full timestamp comparison for {allAssets.Count} assets...");

                await Core.PCPThreading.ParallelForEachAsync(allAssets, (path, token) =>
                {
                    if (!m_Entries.TryGetValue(path, out var entry))
                    {
                        m_NewAssets[path] = 0;
                        return Task.CompletedTask;
                    }

                    try
                    {
                        var currentTicks = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                        if (currentTicks != entry.lastModifiedTicks)
                            m_StaleAssets[path] = 0;
                    }
                    catch (System.IO.IOException) { m_StaleAssets[path] = 0; }

                    return Task.CompletedTask;
                }, Core.PCPThreading.DefaultConcurrency, ct);

                // Prune deleted entries
                var currentPathSet = new HashSet<string>(allAssets);
                foreach (var key in m_Entries.Keys)
                {
                    if (!currentPathSet.Contains(key))
                        m_Entries.TryRemove(key, out _);
                }

                PCPSettings.Log($"[ProjectCleanPro] Staleness result: {m_StaleAssets.Count} stale, {m_NewAssets.Count} new.");
            }

            m_StalenessComputed = true;
        }

        /// <summary>
        /// Returns all stale + new asset paths.
        /// </summary>
        public IReadOnlyList<string> GetStaleAssets()
        {
            return m_StaleAssets.Keys.Concat(m_NewAssets.Keys).ToList();
        }

        private static Task<List<string>> CollectAllAssetPathsAsync(CancellationToken ct)
        {
            // Capture the Assets path on the calling thread (Application.dataPath is main-thread-safe)
            var assetsDir = Path.GetFullPath("Assets");
            var projectRoot = Path.GetDirectoryName(assetsDir);

            return Task.Run(() =>
            {
                return Directory.EnumerateFiles(assetsDir, "*.*",
                        SearchOption.AllDirectories)
                    .Where(p => !p.EndsWith(".meta"))
                    .Select(p =>
                    {
                        // Convert to project-relative path (Assets/...)
                        var relativePath = p.Substring(projectRoot.Length + 1);
                        return relativePath.Replace('\\', '/');
                    })
                    .ToList();
            }, ct);
        }

        /// <summary>
        /// Determines which modules need re-running based on the file extensions
        /// of stale assets. Each module declares its own
        /// <see cref="IPCPModule.RelevantExtensions"/>; null means "all changes".
        /// <para>
        /// Must be called AFTER <see cref="RefreshStaleness"/> and BEFORE modules run.
        /// </para>
        /// </summary>
        public void ComputeModuleDirtiness(IReadOnlyList<IPCPModule> modules)
        {
            m_DirtyModules.Clear();

            if (m_StaleAssets.Count == 0 && m_NewAssets.Count == 0)
                return;

            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                var exts = module.RelevantExtensions;

                // null = affected by any file change.
                if (exts == null)
                {
                    m_DirtyModules[module.Id] = true;
                    continue;
                }

                // Empty set = never dirty from file changes (e.g. packages).
                if (exts.Count == 0)
                    continue;

                foreach (string path in m_StaleAssets.Keys.Concat(m_NewAssets.Keys))
                {
                    string ext = Path.GetExtension(path);
                    if (!string.IsNullOrEmpty(ext) && exts.Contains(ext.ToLowerInvariant()))
                    {
                        m_DirtyModules[module.Id] = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Directly marks a module as dirty. Used by <see cref="PCPSettingsTracker"/>
        /// and <see cref="PCPAssetChangeTracker.PackagesChanged"/>.
        /// </summary>
        public void MarkModuleDirty(PCPModuleId id)
        {
            m_DirtyModules[id] = true;
        }

        // ----------------------------------------------------------------
        // Full staleness check
        // ----------------------------------------------------------------

        private void RefreshStalenessFull(string[] currentAssetPaths)
        {
            var currentSet = new HashSet<string>(currentAssetPaths, StringComparer.Ordinal);
            var deletedEntries = new List<string>();

            foreach (var kvp in m_Entries)
            {
                string path = kvp.Key;
                if (!currentSet.Contains(path))
                {
                    deletedEntries.Add(path);
                    continue;
                }

                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    deletedEntries.Add(path);
                    continue;
                }

                long currentTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
                bool assetStale = currentTicks != kvp.Value.lastModifiedTicks;

                if (assetStale)
                {
                    m_StaleAssets[path] = 0;
                }
                else
                {
                    string fullMetaPath = fullPath + ".meta";
                    if (File.Exists(fullMetaPath))
                    {
                        string storedMetaTicks = GetMetadataFromEntry(kvp.Value, "cache.metaTicks");
                        if (storedMetaTicks == null)
                        {
                            // Meta exists but not stamped — treat as stale for meta purposes only
                            // (handled inline in IsStaleOrMetaStale when not pre-computed)
                        }
                        else
                        {
                            long currentMetaTicks = File.GetLastWriteTimeUtc(fullMetaPath).Ticks;
                            if (!string.Equals(storedMetaTicks, currentMetaTicks.ToString(),
                                    StringComparison.Ordinal))
                            {
                                // Meta-only stale: tracked in m_StaleAssets for IsStaleOrMetaStale
                                // but NOT in regular m_StaleAssets — we use a special key approach
                                // by storing in m_StaleAssets with a meta sentinel not needed;
                                // IsStaleOrMetaStale will do a direct check when not in m_StaleAssets.
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < deletedEntries.Count; i++)
            {
                m_Entries.TryRemove(deletedEntries[i], out _);
                m_Dirty = true;
            }

            // New assets (not in cache) are new.
            for (int i = 0; i < currentAssetPaths.Length; i++)
            {
                string path = currentAssetPaths[i];
                if (!m_Entries.ContainsKey(path))
                {
                    m_NewAssets[path] = 0;
                }
            }
        }

        // ----------------------------------------------------------------
        // Stamping
        // ----------------------------------------------------------------

        /// <summary>
        /// Async version of stamping: stamps all stale + new assets on background threads.
        /// </summary>
        public async Task StampProcessedAssetsAsync(CancellationToken ct)
        {
            var toStamp = m_StaleAssets.Keys.Concat(m_NewAssets.Keys).ToList();

            await Core.PCPThreading.ParallelForEachAsync(toStamp, (path, token) =>
            {
                try
                {
                    if (!System.IO.File.Exists(path)) return Task.CompletedTask;

                    var ticks = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                    var size = new System.IO.FileInfo(path).Length;

                    m_Entries.AddOrUpdate(path,
                        _ => new CacheEntry
                        {
                            assetPath = path,
                            lastModifiedTicks = ticks,
                            fileSizeBytes = size
                        },
                        (_, existing) =>
                        {
                            existing.lastModifiedTicks = ticks;
                            existing.fileSizeBytes = size;
                            return existing;
                        });
                }
                catch (System.IO.IOException) { }

                return Task.CompletedTask;
            }, Core.PCPThreading.DefaultConcurrency, ct);

            m_StaleAssets.Clear();
            m_NewAssets.Clear();
        }

        public void ResetStaleness()
        {
            m_StaleAssets.Clear();
            m_NewAssets.Clear();
            m_StalenessComputed = false;
        }

        // ----------------------------------------------------------------
        // Staleness checks
        // ----------------------------------------------------------------

        public bool IsStale(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return true;

            if (m_StalenessComputed)
                return m_StaleAssets.ContainsKey(assetPath) || m_NewAssets.ContainsKey(assetPath);

            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return true;

            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return true;

            return File.GetLastWriteTimeUtc(fullPath).Ticks != entry.lastModifiedTicks;
        }

        public bool IsStaleOrMetaStale(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return true;

            if (m_StalenessComputed)
            {
                // Check if directly stale or new
                if (m_StaleAssets.ContainsKey(assetPath) || m_NewAssets.ContainsKey(assetPath))
                    return true;

                // Asset itself is not stale — check meta timestamp inline
                string fullMetaPathFast = Path.GetFullPath(assetPath) + ".meta";
                if (!File.Exists(fullMetaPathFast))
                    return false;

                if (!m_Entries.TryGetValue(assetPath, out CacheEntry cachedEntry))
                    return true;

                string storedMetaTicksFast = GetMetadataFromEntry(cachedEntry, "cache.metaTicks");
                if (storedMetaTicksFast == null)
                    return true;

                long currentMetaTicksFast = File.GetLastWriteTimeUtc(fullMetaPathFast).Ticks;
                return !string.Equals(storedMetaTicksFast, currentMetaTicksFast.ToString(),
                    StringComparison.Ordinal);
            }

            if (IsStale(assetPath))
                return true;

            string fullMetaPath = Path.GetFullPath(assetPath) + ".meta";
            if (!File.Exists(fullMetaPath))
                return false;

            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return true;

            string storedMetaTicks = GetMetadataFromEntry(entry, "cache.metaTicks");
            if (storedMetaTicks == null)
                return true;

            long currentMetaTicks = File.GetLastWriteTimeUtc(fullMetaPath).Ticks;
            return !string.Equals(storedMetaTicks, currentMetaTicks.ToString(),
                StringComparison.Ordinal);
        }

        public void StampMeta(string assetPath)
        {
            string fullMetaPath = Path.GetFullPath(assetPath) + ".meta";
            if (!File.Exists(fullMetaPath))
                return;
            long ticks = File.GetLastWriteTimeUtc(fullMetaPath).Ticks;
            SetMetadata(assetPath, "cache.metaTicks", ticks.ToString());
        }

        // ----------------------------------------------------------------
        // Hash
        // ----------------------------------------------------------------

        public string GetHash(string assetPath)
        {
            if (m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return entry.sha256Hash;
            return null;
        }

        public void SetHash(string assetPath, string hash)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);
            entry.sha256Hash = hash;
            StampLastModified(entry, assetPath);
            m_Dirty = true;
        }

        // ----------------------------------------------------------------
        // File size
        // ----------------------------------------------------------------

        public long GetFileSize(string assetPath)
        {
            if (m_Entries.TryGetValue(assetPath, out CacheEntry entry) && entry.fileSizeBytes > 0)
                return entry.fileSizeBytes;
            return -1;
        }

        public void SetFileSize(string assetPath, long size)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);
            entry.fileSizeBytes = size;
            StampLastModified(entry, assetPath);
            m_Dirty = true;
        }

        // ----------------------------------------------------------------
        // Dependencies
        // ----------------------------------------------------------------

        public string[] GetDependencies(string assetPath)
        {
            if (m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return entry.dependencies;
            return null;
        }

        public void SetDependencies(string assetPath, string[] deps)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);
            entry.dependencies = deps;
            StampLastModified(entry, assetPath);
            m_Dirty = true;
        }

        // ----------------------------------------------------------------
        // Generic module metadata (O(1) ConcurrentDictionary lookup)
        // ----------------------------------------------------------------

        public string GetMetadata(string assetPath, string key)
        {
            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return null;
            return GetMetadataFromEntry(entry, key);
        }

        public void SetMetadata(string assetPath, string key, string value)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);

            if (entry.lastModifiedTicks == 0)
                StampLastModified(entry, assetPath);

            if (entry.metadata == null)
                entry.metadata = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

            entry.metadata[key] = value;
            m_Dirty = true;
        }

        public void RemoveMetadata(string assetPath, string key)
        {
            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return;
            if (entry.metadata != null && entry.metadata.TryRemove(key, out _))
                m_Dirty = true;
        }

        // ----------------------------------------------------------------
        // Bulk operations
        // ----------------------------------------------------------------

        public void RemoveEntry(string assetPath)
        {
            if (m_Entries.TryRemove(assetPath, out _))
                m_Dirty = true;
        }

        public IEnumerable<string> GetAllCachedPaths()
        {
            return m_Entries.Keys;
        }

        // ----------------------------------------------------------------
        // Persistence (v3 binary format with atomic writes)
        // ----------------------------------------------------------------

        /// <summary>
        /// Loads the cache from disk. Safe to call if the file does not exist.
        /// Falls back gracefully on any error.
        /// </summary>
        public void Load()
        {
            m_Entries.Clear();
            m_Dirty = false;

            bool loaded = PCPCacheIO.SafeRead(s_CacheFilePath, CurrentVersion,
                reader =>
                {
                    int entryCount = reader.ReadInt32();
                    for (int i = 0; i < entryCount; i++)
                    {
                        var entry = new CacheEntry
                        {
                            assetPath = reader.ReadString(),
                            lastModifiedTicks = reader.ReadInt64(),
                            fileSizeBytes = reader.ReadInt64()
                        };

                        bool hasHash = reader.ReadBoolean();
                        entry.sha256Hash = hasHash ? reader.ReadString() : null;

                        int depCount = reader.ReadInt32();
                        if (depCount > 0)
                        {
                            entry.dependencies = new string[depCount];
                            for (int d = 0; d < depCount; d++)
                                entry.dependencies[d] = reader.ReadString();
                        }

                        int metaCount = reader.ReadInt32();
                        if (metaCount > 0)
                        {
                            entry.metadata = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
                            for (int m = 0; m < metaCount; m++)
                            {
                                string key = reader.ReadString();
                                string val = reader.ReadString();
                                entry.metadata[key] = val;
                            }
                        }

                        if (!string.IsNullOrEmpty(entry.assetPath))
                            m_Entries[entry.assetPath] = entry;
                    }

                    return true;
                }, out _);

            if (!loaded)
            {
                m_Entries.Clear();
                PCPSettings.Log("[ProjectCleanPro] Scan cache: no existing cache found, starting fresh.");
            }
            else
            {
                PCPSettings.Log($"[ProjectCleanPro] Scan cache loaded: {m_Entries.Count} cached asset(s).");
            }
        }

        /// <summary>
        /// Persists the cache to disk using atomic writes via <see cref="PCPCacheIO"/>.
        /// No-op if nothing has changed.
        /// </summary>
        public void Save()
        {
            if (!m_Dirty)
                return;

            try
            {
                PCPCacheIO.AtomicWrite(s_CacheFilePath, CurrentVersion, writer =>
                {
                    writer.Write(m_Entries.Count);

                    foreach (var kvp in m_Entries)
                    {
                        CacheEntry entry = kvp.Value;
                        writer.Write(entry.assetPath ?? string.Empty);
                        writer.Write(entry.lastModifiedTicks);
                        writer.Write(entry.fileSizeBytes);

                        bool hasHash = !string.IsNullOrEmpty(entry.sha256Hash);
                        writer.Write(hasHash);
                        if (hasHash)
                            writer.Write(entry.sha256Hash);

                        int depCount = entry.dependencies?.Length ?? 0;
                        writer.Write(depCount);
                        for (int d = 0; d < depCount; d++)
                            writer.Write(entry.dependencies[d] ?? string.Empty);

                        int metaCount = entry.metadata?.Count ?? 0;
                        writer.Write(metaCount);
                        if (entry.metadata != null)
                        {
                            foreach (var meta in entry.metadata)
                            {
                                writer.Write(meta.Key ?? string.Empty);
                                writer.Write(meta.Value ?? string.Empty);
                            }
                        }
                    }
                });

                m_Dirty = false;
                PCPSettings.Log($"[ProjectCleanPro] Scan cache saved: {m_Entries.Count} asset(s).");

                // Clean up legacy files.
                CleanupLegacy(s_LegacyBinPath);
                CleanupLegacy(s_LegacyJsonPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to save scan cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Async wrapper: runs <see cref="Save"/> on a background thread.
        /// </summary>
        public async Task SaveAsync(CancellationToken ct)
        {
            await Task.Run(() => Save(), ct);
        }

        /// <summary>
        /// Async wrapper: runs <see cref="Load"/> on a background thread.
        /// </summary>
        public async Task LoadAsync(CancellationToken ct)
        {
            await Task.Run(() => Load(), ct);
        }

        /// <summary>
        /// Removes all cached entries and deletes the cache file from disk.
        /// </summary>
        public void Clear()
        {
            m_Entries.Clear();
            m_Dirty = false;
            ResetStaleness();
            CleanupLegacy(s_CacheFilePath);
            CleanupLegacy(s_LegacyBinPath);
            CleanupLegacy(s_LegacyJsonPath);
            Core.PCPDependencyResolverBase.DeleteGraphFile();
        }

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        private CacheEntry GetOrCreateEntry(string assetPath)
        {
            return m_Entries.GetOrAdd(assetPath, key =>
            {
                m_Dirty = true;
                return new CacheEntry { assetPath = key };
            });
        }

        private static void StampLastModified(CacheEntry entry, string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath))
                entry.lastModifiedTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
        }

        private static string GetMetadataFromEntry(CacheEntry entry, string key)
        {
            if (entry.metadata == null)
                return null;
            entry.metadata.TryGetValue(key, out string value);
            return value;
        }

        private static void CleanupLegacy(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
