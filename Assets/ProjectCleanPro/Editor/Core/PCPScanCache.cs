using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Incremental scan cache persisted to Library/ProjectCleanPro/.
    /// Tracks per-asset modification timestamps, content hashes, and dependency lists
    /// so that subsequent scans only re-process changed assets.
    /// </summary>
    public sealed class PCPScanCache
    {
        // ----------------------------------------------------------------
        // Serializable data model (JsonUtility requires wrapper classes)
        // ----------------------------------------------------------------

        [Serializable]
        private sealed class CacheEntry
        {
            public string assetPath;
            public long lastModifiedTicks;
            public string sha256Hash;
            public string[] dependencies;
        }

        [Serializable]
        private sealed class CacheData
        {
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

            string fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath))
                entry.lastModifiedTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
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

            string fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath))
                entry.lastModifiedTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
        }

        // ----------------------------------------------------------------
        // Persistence
        // ----------------------------------------------------------------

        /// <summary>
        /// Loads the cache from disk. Safe to call if the file does not exist.
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
            catch (Exception)
            {
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
    }
}
