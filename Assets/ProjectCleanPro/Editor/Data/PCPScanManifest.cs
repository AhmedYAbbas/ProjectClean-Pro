using System;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Lightweight summary of the last scan, stored as the header of the result
    /// cache. The dashboard reads only this — it never needs to load the full
    /// per-module result lists.
    /// <para>
    /// Aggregate values (HealthScore, TotalWastedBytes, TotalFindingCount)
    /// are pre-computed once after a scan, not recalculated on every access.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class PCPScanManifest
    {
        public const int FormatVersion = 1;

        // ----------------------------------------------------------------
        // Global metadata
        // ----------------------------------------------------------------

        public string scanTimestampUtc;
        public float scanDurationSeconds;
        public string projectName;
        public string unityVersion;
        public int totalAssetsScanned;

        // ----------------------------------------------------------------
        // Pre-computed aggregates (O(1) dashboard access)
        // ----------------------------------------------------------------

        public int healthScore;
        public long totalWastedBytes;
        public int totalFindingCount;

        // ----------------------------------------------------------------
        // Per-module summaries
        // ----------------------------------------------------------------

        public PCPModuleSummary[] moduleSummaries;

        /// <summary>
        /// Returns the summary for a specific module, or a default struct
        /// if the module is not present in the manifest.
        /// </summary>
        public PCPModuleSummary GetModuleSummary(PCPModuleId id)
        {
            if (moduleSummaries == null)
                return default;

            int ordinal = (int)id;
            if (ordinal >= 0 && ordinal < moduleSummaries.Length)
                return moduleSummaries[ordinal];

            return default;
        }

        /// <summary>
        /// Returns true if all modules have results (i.e. a full scan completed).
        /// </summary>
        public bool HasAllModuleResults()
        {
            if (moduleSummaries == null)
                return false;

            for (int i = 0; i < moduleSummaries.Length; i++)
            {
                if (!moduleSummaries[i].hasResults)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Per-module summary stored in the manifest. Indexed by <see cref="PCPModuleId"/>
    /// ordinal so the dashboard can show finding counts without loading full results.
    /// </summary>
    [Serializable]
    public struct PCPModuleSummary
    {
        public PCPModuleId id;
        public long scanTimestampTicks;
        public int findingCount;
        public long totalSizeBytes;
        public bool hasResults;
    }
}
