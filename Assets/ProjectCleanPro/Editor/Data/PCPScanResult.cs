using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Aggregates the results from all analysis modules into a single snapshot.
    /// Produced by a full or incremental scan and consumed by the UI layer.
    /// </summary>
    [Serializable]
    public class PCPScanResult
    {
        // ── Module results ──────────────────────────────────────────────

        /// <summary>Assets determined to be unreferenced by the unused-asset scanner.</summary>
        public List<PCPUnusedAsset> unusedAssets = new List<PCPUnusedAsset>();

        /// <summary>Broken serialized references found by the missing-reference scanner.</summary>
        public List<PCPMissingReference> missingReferences = new List<PCPMissingReference>();

        /// <summary>Groups of content-identical assets found by the duplicate scanner.</summary>
        public List<PCPDuplicateGroup> duplicateGroups = new List<PCPDuplicateGroup>();

        /// <summary>Per-package audit results from the package auditor.</summary>
        public List<PCPPackageAuditEntry> packageAuditEntries = new List<PCPPackageAuditEntry>();

        /// <summary>Per-shader analysis results from the shader analyzer.</summary>
        public List<PCPShaderEntry> shaderEntries = new List<PCPShaderEntry>();

        /// <summary>Per-asset size profile entries from the size profiler.</summary>
        public List<PCPSizeEntry> sizeEntries = new List<PCPSizeEntry>();

        // ── Metadata ────────────────────────────────────────────────────

        /// <summary>UTC timestamp when the scan started (ISO 8601).</summary>
        public string scanTimestampUtc;

        /// <summary>Wall-clock duration of the scan in seconds.</summary>
        public float scanDurationSeconds;

        /// <summary>Name of the Unity project.</summary>
        public string projectName;

        /// <summary>Unity Editor version used during the scan.</summary>
        public string unityVersion;

        /// <summary>Total number of assets evaluated during the scan.</summary>
        public int totalAssetsScanned;

        // ── Property alias ──────────────────────────────────────────────

        /// <summary>
        /// Alias for <see cref="packageAuditEntries"/>. Used by API and report layers.
        /// Not serialized by JsonUtility (property, not field).
        /// </summary>
        public List<PCPPackageAuditEntry> packageAudit => packageAuditEntries;

        // ── Computed properties ─────────────────────────────────────────

        /// <summary>
        /// Total number of individual findings across all modules.
        /// </summary>
        public int TotalFindingCount
        {
            get
            {
                int count = 0;
                count += unusedAssets.Count;
                count += missingReferences.Count;
                count += duplicateGroups.Count;
                count += packageAuditEntries.Count;
                count += shaderEntries.Count;
                count += sizeEntries.Count;
                return count;
            }
        }

        /// <summary>Number of unused assets found.</summary>
        public int UnusedAssetCount => unusedAssets?.Count ?? 0;

        /// <summary>Number of missing references found.</summary>
        public int MissingReferenceCount => missingReferences?.Count ?? 0;

        /// <summary>Number of duplicate groups found.</summary>
        public int DuplicateGroupCount => duplicateGroups?.Count ?? 0;

        /// <summary>Total size in bytes of all unused assets.</summary>
        public long UnusedAssetsTotalSize
        {
            get
            {
                long total = 0L;
                if (unusedAssets != null)
                    foreach (var unused in unusedAssets)
                        total += unused.SizeBytes;
                return total;
            }
        }

        /// <summary>Total wasted bytes across all duplicate groups.</summary>
        public long DuplicateWastedSize
        {
            get
            {
                long total = 0L;
                if (duplicateGroups != null)
                    foreach (var group in duplicateGroups)
                        total += group.WastedBytes;
                return total;
            }
        }

        /// <summary>
        /// Estimated total bytes that could be reclaimed by acting on all findings.
        /// Includes unused asset sizes and duplicate wasted bytes.
        /// </summary>
        public long TotalWastedBytes => UnusedAssetsTotalSize + DuplicateWastedSize;

        /// <summary>Total wasted bytes formatted as a human-readable string.</summary>
        public string FormattedWastedBytes => PCPAssetInfo.FormatBytes(TotalWastedBytes);

        /// <summary>
        /// Returns true if the scan produced no findings at all.
        /// </summary>
        public bool IsClean => TotalFindingCount == 0;

        /// <summary>
        /// Clears all result lists, resetting the scan result to an empty state.
        /// Does not reset the timestamp or duration.
        /// </summary>
        public void Clear()
        {
            unusedAssets.Clear();
            missingReferences.Clear();
            duplicateGroups.Clear();
            packageAuditEntries.Clear();
            shaderEntries.Clear();
            sizeEntries.Clear();
        }

        /// <summary>
        /// Returns a multi-line summary of the scan results.
        /// </summary>
        public string GetSummary()
        {
            return $"Scan completed at {scanTimestampUtc ?? "N/A"} " +
                   $"({scanDurationSeconds:F1}s)\n" +
                   $"  Unused assets:       {unusedAssets.Count}\n" +
                   $"  Missing references:  {missingReferences.Count}\n" +
                   $"  Duplicate groups:    {duplicateGroups.Count}\n" +
                   $"  Package audit:       {packageAuditEntries.Count}\n" +
                   $"  Shader entries:      {shaderEntries.Count}\n" +
                   $"  Size entries:        {sizeEntries.Count}\n" +
                   $"  Total findings:      {TotalFindingCount}\n" +
                   $"  Estimated savings:   {FormattedWastedBytes}";
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }
}
