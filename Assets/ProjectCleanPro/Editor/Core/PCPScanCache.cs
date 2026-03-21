using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Incremental scan cache persisted to Library/ProjectCleanPro/.
    /// Tracks per-asset modification timestamps, content hashes, dependency lists,
    /// file sizes, and generic per-module metadata so that subsequent scans only
    /// re-process changed assets.
    /// </summary>
    public sealed class PCPScanCache
    {
        // ----------------------------------------------------------------
        // Versioning
        // ----------------------------------------------------------------

        /// <summary>
        /// Current cache format version. Bump this when the serialized layout
        /// changes in a way that is not backward-compatible.
        /// </summary>
        public const int CurrentVersion = 1;

        // ----------------------------------------------------------------
        // Serializable data model (JsonUtility requires wrapper classes)
        // ----------------------------------------------------------------

        [Serializable]
        private sealed class MetadataPair
        {
            public string key;
            public string value;
        }

        [Serializable]
        private sealed class CacheEntry
        {
            public string assetPath;
            public long lastModifiedTicks;
            public string sha256Hash;
            public string[] dependencies;
            public long fileSizeBytes;
            public List<MetadataPair> metadata;
        }

        [Serializable]
        private sealed class CacheData
        {
            public int version;
            public List<CacheEntry> entries = new List<CacheEntry>();
        }

        // ----------------------------------------------------------------
        // State
        // ----------------------------------------------------------------

        private readonly Dictionary<string, CacheEntry> m_Entries =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        private static readonly string s_CacheDirectory =
            Path.Combine(Application.dataPath, "..", "Library", "ProjectCleanPro");

        private static readonly string s_CacheFilePath =
            Path.Combine(s_CacheDirectory, "ScanCache.json");

        /// <summary>
        /// The directory where cache files are stored.
        /// </summary>
        public static string CacheDirectory => s_CacheDirectory;

        /// <summary>
        /// Number of cached entries.
        /// </summary>
        public int Count => m_Entries.Count;

        // ----------------------------------------------------------------
        // Staleness check
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns <c>true</c> if the asset at <paramref name="assetPath"/> has been
        /// modified since it was last cached (based on file write time), or if there
        /// is no cache entry for it.
        /// </summary>
        public bool IsStale(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return true;

            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return true;

            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return true;

            long currentTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
            return currentTicks != entry.lastModifiedTicks;
        }

        /// <summary>
        /// Returns <c>true</c> if the asset or its .meta file has been modified
        /// since the asset was last cached. Use this for modules that care about
        /// import settings (e.g. SizeProfiler, DuplicateDetector).
        /// </summary>
        public bool IsStaleOrMetaStale(string assetPath)
        {
            if (IsStale(assetPath))
                return true;

            string metaPath = assetPath + ".meta";
            string fullMetaPath = Path.GetFullPath(metaPath);
            if (!File.Exists(fullMetaPath))
                return false; // No meta file — not stale on that account.

            // We store the meta timestamp in the generic metadata store.
            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return true;

            string storedMetaTicks = GetMetadataFromEntry(entry, "cache.metaTicks");
            if (storedMetaTicks == null)
                return true;

            long currentMetaTicks = File.GetLastWriteTimeUtc(fullMetaPath).Ticks;
            return !string.Equals(storedMetaTicks, currentMetaTicks.ToString(),
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Stores the current .meta file timestamp so that <see cref="IsStaleOrMetaStale"/>
        /// can detect import-setting changes on subsequent scans.
        /// </summary>
        public void StampMeta(string assetPath)
        {
            string metaPath = assetPath + ".meta";
            string fullMetaPath = Path.GetFullPath(metaPath);
            if (!File.Exists(fullMetaPath))
                return;

            long ticks = File.GetLastWriteTimeUtc(fullMetaPath).Ticks;
            SetMetadata(assetPath, "cache.metaTicks", ticks.ToString());
        }

        // ----------------------------------------------------------------
        // Hash
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the cached SHA-256 hash for <paramref name="assetPath"/>,
        /// or <c>null</c> if not cached.
        /// </summary>
        public string GetHash(string assetPath)
        {
            if (m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return entry.sha256Hash;
            return null;
        }

        /// <summary>
        /// Stores the SHA-256 hash and current modification timestamp for an asset.
        /// </summary>
        public void SetHash(string assetPath, string hash)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);
            entry.sha256Hash = hash;
            StampLastModified(entry, assetPath);
        }

        // ----------------------------------------------------------------
        // File size
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the cached file size for <paramref name="assetPath"/>,
        /// or <c>-1</c> if not cached.
        /// </summary>
        public long GetFileSize(string assetPath)
        {
            if (m_Entries.TryGetValue(assetPath, out CacheEntry entry) && entry.fileSizeBytes > 0)
                return entry.fileSizeBytes;
            return -1;
        }

        /// <summary>
        /// Stores the file size and updates the modification timestamp for an asset.
        /// </summary>
        public void SetFileSize(string assetPath, long size)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);
            entry.fileSizeBytes = size;
            StampLastModified(entry, assetPath);
        }

        // ----------------------------------------------------------------
        // Dependencies
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns cached dependencies for <paramref name="assetPath"/>,
        /// or <c>null</c> if not cached.
        /// </summary>
        public string[] GetDependencies(string assetPath)
        {
            if (m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return entry.dependencies;
            return null;
        }

        /// <summary>
        /// Stores the dependency list for an asset and updates its modification timestamp.
        /// </summary>
        public void SetDependencies(string assetPath, string[] deps)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);
            entry.dependencies = deps;
            StampLastModified(entry, assetPath);
        }

        // ----------------------------------------------------------------
        // Generic module metadata
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the cached metadata value for <paramref name="assetPath"/> and
        /// <paramref name="key"/>, or <c>null</c> if not found.
        /// </summary>
        public string GetMetadata(string assetPath, string key)
        {
            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return null;
            return GetMetadataFromEntry(entry, key);
        }

        /// <summary>
        /// Stores a metadata key-value pair for <paramref name="assetPath"/>.
        /// Updates existing entries or creates new ones as needed.
        /// </summary>
        public void SetMetadata(string assetPath, string key, string value)
        {
            CacheEntry entry = GetOrCreateEntry(assetPath);
            if (entry.metadata == null)
                entry.metadata = new List<MetadataPair>();

            for (int i = 0; i < entry.metadata.Count; i++)
            {
                if (string.Equals(entry.metadata[i].key, key, StringComparison.Ordinal))
                {
                    entry.metadata[i].value = value;
                    return;
                }
            }

            entry.metadata.Add(new MetadataPair { key = key, value = value });
        }

        /// <summary>
        /// Removes a metadata key for <paramref name="assetPath"/>.
        /// </summary>
        public void RemoveMetadata(string assetPath, string key)
        {
            if (!m_Entries.TryGetValue(assetPath, out CacheEntry entry))
                return;
            if (entry.metadata == null)
                return;

            for (int i = entry.metadata.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entry.metadata[i].key, key, StringComparison.Ordinal))
                {
                    entry.metadata.RemoveAt(i);
                    return;
                }
            }
        }

        // ----------------------------------------------------------------
        // Bulk operations
        // ----------------------------------------------------------------

        /// <summary>
        /// Removes the cache entry for <paramref name="assetPath"/>.
        /// </summary>
        public void RemoveEntry(string assetPath)
        {
            m_Entries.Remove(assetPath);
        }

        /// <summary>
        /// Returns all cached asset paths.
        /// </summary>
        public IEnumerable<string> GetAllCachedPaths()
        {
            return m_Entries.Keys;
        }

        // ----------------------------------------------------------------
        // Persistence
        // ----------------------------------------------------------------

        /// <summary>
        /// Loads the cache from disk. Safe to call if the file does not exist.
        /// Clears the cache if the stored version does not match <see cref="CurrentVersion"/>.
        /// </summary>
        public void Load()
        {
            m_Entries.Clear();

            if (!File.Exists(s_CacheFilePath))
                return;

            try
            {
                string json = File.ReadAllText(s_CacheFilePath);
                CacheData data = JsonUtility.FromJson<CacheData>(json);
                if (data?.entries == null)
                    return;

                // Version mismatch: discard stale cache.
                if (data.version != CurrentVersion)
                {
                    Debug.Log($"[ProjectCleanPro] Cache version mismatch " +
                              $"(stored={data.version}, current={CurrentVersion}). " +
                              "Clearing cache.");
                    return;
                }

                foreach (CacheEntry entry in data.entries)
                {
                    if (!string.IsNullOrEmpty(entry.assetPath))
                        m_Entries[entry.assetPath] = entry;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to load scan cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Persists the cache to disk as JSON.
        /// </summary>
        public void Save()
        {
            try
            {
                if (!Directory.Exists(s_CacheDirectory))
                    Directory.CreateDirectory(s_CacheDirectory);

                CacheData data = new CacheData();
                data.version = CurrentVersion;
                data.entries = new List<CacheEntry>(m_Entries.Values);

                string json = JsonUtility.ToJson(data, prettyPrint: false);
                File.WriteAllText(s_CacheFilePath, json);
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

            try
            {
                if (File.Exists(s_CacheFilePath))
                    File.Delete(s_CacheFilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to delete scan cache file: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Utility
        // ----------------------------------------------------------------

        /// <summary>
        /// Computes the SHA-256 hash of a file on disk.
        /// Returns the lowercase hex string, or <c>null</c> if the file does not exist.
        /// </summary>
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
                    // Convert to lowercase hex string.
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(hashBytes.Length * 2);
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

            for (int i = 0; i < entry.metadata.Count; i++)
            {
                if (string.Equals(entry.metadata[i].key, key, StringComparison.Ordinal))
                    return entry.metadata[i].value;
            }
            return null;
        }
    }
}
