using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            public Dictionary<string, string> metadata;
        }

        // ----------------------------------------------------------------
        // State
        // ----------------------------------------------------------------

        private readonly Dictionary<string, CacheEntry> m_Entries =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        // Pre-computed staleness — populated by RefreshStaleness(),
        // consumed by IsStale() / IsStaleOrMetaStale() for O(1) lookups.
        private HashSet<string> m_StaleAssets;
        private HashSet<string> m_StaleOrMetaStaleAssets;
        private bool m_StalenessComputed;

        // Dirty flag — Save() is a no-op when false.
        private bool m_Dirty;

        // Module-level dirty tracking — computed alongside staleness so
        // the orchestrator can skip unaffected modules.
        private HashSet<PCPModuleId> m_DirtyModules;

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
        public bool HasAnyChanges => m_StaleAssets != null && m_StaleAssets.Count > 0;
        public int StaleCount => m_StaleAssets?.Count ?? 0;

        /// <summary>
        /// Returns true if the given module needs re-running based on what
        /// file types changed since the last scan. Conservative: returns true
        /// if staleness has not been computed.
        /// </summary>
        public bool IsModuleDirty(PCPModuleId id)
        {
            return m_DirtyModules == null || m_DirtyModules.Contains(id);
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
        /// use O(1) HashSet lookups.
        /// </summary>
        public void RefreshStaleness(string[] currentAssetPaths)
        {
            m_StaleAssets = new HashSet<string>(StringComparer.Ordinal);
            m_StaleOrMetaStaleAssets = new HashSet<string>(StringComparer.Ordinal);

            // Fast path: nothing changed since last scan.
            if (!PCPAssetChangeTracker.HasChanges && m_Entries.Count > 0)
            {
                m_DirtyModules = new HashSet<PCPModuleId>();
                m_StalenessComputed = true;
                return;
            }

            // Domain reload or first scan -> full timestamp check.
            if (PCPAssetChangeTracker.FullCheckNeeded || m_Entries.Count == 0)
            {
                RefreshStalenessFull(currentAssetPaths);
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
                        m_StaleAssets.Add(path);
                        m_StaleOrMetaStaleAssets.Add(path);
                    }
                    else if (m_Entries.Remove(path))
                    {
                        m_Dirty = true;
                    }
                }
            }

            m_StalenessComputed = true;
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
            m_DirtyModules = new HashSet<PCPModuleId>();

            if (m_StaleAssets == null || m_StaleAssets.Count == 0)
                return;

            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                var exts = module.RelevantExtensions;

                // null = affected by any file change.
                if (exts == null)
                {
                    m_DirtyModules.Add(module.Id);
                    continue;
                }

                // Empty set = never dirty from file changes (e.g. packages).
                if (exts.Count == 0)
                    continue;

                foreach (string path in m_StaleAssets)
                {
                    string ext = Path.GetExtension(path);
                    if (!string.IsNullOrEmpty(ext) && exts.Contains(ext.ToLowerInvariant()))
                    {
                        m_DirtyModules.Add(module.Id);
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
            if (m_DirtyModules == null)
                m_DirtyModules = new HashSet<PCPModuleId>();
            m_DirtyModules.Add(id);
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
                    m_StaleAssets.Add(path);
                    m_StaleOrMetaStaleAssets.Add(path);
                }
                else
                {
                    string fullMetaPath = fullPath + ".meta";
                    if (File.Exists(fullMetaPath))
                    {
                        string storedMetaTicks = GetMetadataFromEntry(kvp.Value, "cache.metaTicks");
                        if (storedMetaTicks == null)
                        {
                            m_StaleOrMetaStaleAssets.Add(path);
                        }
                        else
                        {
                            long currentMetaTicks = File.GetLastWriteTimeUtc(fullMetaPath).Ticks;
                            if (!string.Equals(storedMetaTicks, currentMetaTicks.ToString(),
                                    StringComparison.Ordinal))
                            {
                                m_StaleOrMetaStaleAssets.Add(path);
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < deletedEntries.Count; i++)
            {
                m_Entries.Remove(deletedEntries[i]);
                m_Dirty = true;
            }

            // New assets (not in cache) are stale.
            for (int i = 0; i < currentAssetPaths.Length; i++)
            {
                string path = currentAssetPaths[i];
                if (!m_Entries.ContainsKey(path))
                {
                    m_StaleAssets.Add(path);
                    m_StaleOrMetaStaleAssets.Add(path);
                }
            }
        }

        // ----------------------------------------------------------------
        // Stamping
        // ----------------------------------------------------------------

        /// <summary>
        /// Stamps only assets marked stale plus genuinely new assets.
        /// O(stale + new) instead of O(N).
        /// </summary>
        public void StampStaleAssets(string[] allAssetPaths)
        {
            if (m_StaleAssets != null)
            {
                foreach (string path in m_StaleAssets)
                {
                    CacheEntry entry = GetOrCreateEntry(path);
                    StampLastModified(entry, path);
                }
            }

            for (int i = 0; i < allAssetPaths.Length; i++)
            {
                string path = allAssetPaths[i];
                if (!m_Entries.TryGetValue(path, out CacheEntry entry))
                {
                    entry = new CacheEntry { assetPath = path };
                    m_Entries[path] = entry;
                    StampLastModified(entry, path);
                }
                else if (entry.lastModifiedTicks == 0)
                {
                    StampLastModified(entry, path);
                }
            }
        }

        public void ResetStaleness()
        {
            m_StaleAssets = null;
            m_StaleOrMetaStaleAssets = null;
            m_StalenessComputed = false;
        }

        // ----------------------------------------------------------------
        // Staleness checks
        // ----------------------------------------------------------------

        public bool IsStale(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return true;

            if (m_StalenessComputed && m_StaleAssets != null)
                return m_StaleAssets.Contains(assetPath);

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

            if (m_StalenessComputed && m_StaleOrMetaStaleAssets != null)
                return m_StaleOrMetaStaleAssets.Contains(assetPath);

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
        // Generic module metadata (O(1) Dictionary lookup)
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
                entry.metadata = new Dictionary<string, string>(StringComparer.Ordinal);

            entry.metadata[key] = value;
            m_Dirty = true;
        }

        public void RemoveMetadata(string assetPath, string key)
        {
            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return;
            if (entry.metadata != null && entry.metadata.Remove(key))
                m_Dirty = true;
        }

        // ----------------------------------------------------------------
        // Bulk operations
        // ----------------------------------------------------------------

        public void RemoveEntry(string assetPath)
        {
            if (m_Entries.Remove(assetPath))
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
                            entry.metadata = new Dictionary<string, string>(metaCount, StringComparer.Ordinal);
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
                m_Entries.Clear();
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
        }

        // ----------------------------------------------------------------
        // Utility
        // ----------------------------------------------------------------

        public static string ComputeFileHash(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return null;

            try
            {
                using (FileStream stream = File.OpenRead(fullPath))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hashBytes = sha.ComputeHash(stream);
                    var sb = new System.Text.StringBuilder(hashBytes.Length * 2);
                    foreach (byte b in hashBytes)
                        sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to compute hash for '{assetPath}': {ex.Message}");
                return null;
            }
        }

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        private CacheEntry GetOrCreateEntry(string assetPath)
        {
            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
            {
                entry = new CacheEntry { assetPath = assetPath };
                m_Entries[assetPath] = entry;
                m_Dirty = true;
            }
            return entry;
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
