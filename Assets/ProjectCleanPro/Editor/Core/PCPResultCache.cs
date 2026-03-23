using System;
using System.IO;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Persists <see cref="PCPScanResult"/> to disk so that results survive
    /// domain reloads and window close/reopen. Each module's results are stored
    /// in a separate file under <c>Library/ProjectCleanPro/Results/</c>.
    /// </summary>
    public static class PCPResultCache
    {
        private static readonly string s_ResultDirectory =
            Path.Combine(PCPScanCache.CacheDirectory, "Results");

        private static readonly string s_FullResultPath =
            Path.Combine(s_ResultDirectory, "ScanResult.json");

        /// <summary>
        /// Persists the full scan result to disk.
        /// </summary>
        public static void Save(PCPScanResult result)
        {
            if (result == null)
                return;

            try
            {
                if (!Directory.Exists(s_ResultDirectory))
                    Directory.CreateDirectory(s_ResultDirectory);

                string json = JsonUtility.ToJson(result, prettyPrint: false);
                File.WriteAllText(s_FullResultPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to save result cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the cached scan result from disk. Returns null if no cache
        /// exists or the data is invalid.
        /// </summary>
        public static PCPScanResult Load()
        {
            if (!File.Exists(s_FullResultPath))
                return null;

            try
            {
                string json = File.ReadAllText(s_FullResultPath);
                var result = JsonUtility.FromJson<PCPScanResult>(json);
                if (result == null || string.IsNullOrEmpty(result.scanTimestampUtc))
                    return null;

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to load result cache: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Deletes all cached results from disk.
        /// </summary>
        public static void InvalidateAll()
        {
            try
            {
                if (File.Exists(s_FullResultPath))
                    File.Delete(s_FullResultPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to invalidate result cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if a cached result file exists on disk.
        /// </summary>
        public static bool HasCachedResult => File.Exists(s_FullResultPath);
    }
}
