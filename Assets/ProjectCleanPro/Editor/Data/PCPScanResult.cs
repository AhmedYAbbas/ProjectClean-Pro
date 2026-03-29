using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>Circular dependency chains detected by the dependency module.</summary>
        public List<PCPCircularDependency> circularDependencies = new List<PCPCircularDependency>();

        /// <summary>Orphan assets (zero incoming edges, not roots) found by the dependency module.</summary>
        public List<string> orphanAssets = new List<string>();

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

                // Only count packages that are actually problematic.
                if (packageAuditEntries != null)
                    foreach (var pkg in packageAuditEntries)
                        if (pkg.status == PCPPackageStatus.Unused)
                            count++;

                // Only count shaders with issues (mismatch, unused, high variants).
                if (shaderEntries != null)
                    foreach (var se in shaderEntries)
                        if (se.GetSeverity() != PCPSeverity.Info)
                            count++;

                // Only count size entries with optimization suggestions.
                if (sizeEntries != null)
                    foreach (var se in sizeEntries)
                        if (se.hasOptimizationSuggestion)
                            count++;

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
        /// Weighted project health score from 0 to 100. Uses severity-aware
        /// penalties, size-based impact scaling, and exponential decay so the
        /// score degrades gracefully across projects of any size.
        /// </summary>
        public int HealthScore
        {
            get
            {
                // ── Category weights (penalty points per finding) ──────
                const float kMissingRefError   = 5.0f;
                const float kMissingRefWarning = 3.0f;
                const float kMissingRefInfo    = 1.0f;
                const float kUnusedAsset       = 1.5f;
                const float kDuplicateGroup    = 2.0f;
                const float kShaderError       = 4.0f;
                const float kShaderWarning     = 2.0f;
                const float kShaderInfo        = 0.5f;
                const float kUnusedPackage     = 1.0f;
                const float kPackageOther      = 0.25f;
                const float kSizeOptimizable   = 0.5f;

                float rawPenalty = 0f;

                // Missing references – weighted by severity
                if (missingReferences != null)
                {
                    foreach (var mr in missingReferences)
                    {
                        switch (mr.severity)
                        {
                            case PCPSeverity.Error:   rawPenalty += kMissingRefError;   break;
                            case PCPSeverity.Warning: rawPenalty += kMissingRefWarning; break;
                            default:                  rawPenalty += kMissingRefInfo;    break;
                        }
                    }
                }

                // Unused assets – base penalty + size bonus
                if (unusedAssets != null)
                {
                    foreach (var ua in unusedAssets)
                    {
                        rawPenalty += kUnusedAsset;
                        // Extra penalty for large unused assets (> 1 MB)
                        if (ua.SizeBytes > 1_048_576L)
                            rawPenalty += 1.0f;
                    }
                }

                // Duplicates – base penalty + size bonus
                if (duplicateGroups != null)
                {
                    foreach (var dg in duplicateGroups)
                    {
                        rawPenalty += kDuplicateGroup;
                        // Extra penalty for significant waste (> 1 MB)
                        if (dg.WastedBytes > 1_048_576L)
                            rawPenalty += 1.5f;
                    }
                }

                // Shaders – weighted by computed severity
                if (shaderEntries != null)
                {
                    foreach (var se in shaderEntries)
                    {
                        switch (se.GetSeverity())
                        {
                            case PCPSeverity.Error:   rawPenalty += kShaderError;   break;
                            case PCPSeverity.Warning: rawPenalty += kShaderWarning; break;
                            default:                  rawPenalty += kShaderInfo;    break;
                        }
                    }
                }

                // Packages – only penalize unused and transitive-only
                if (packageAuditEntries != null)
                {
                    foreach (var pkg in packageAuditEntries)
                    {
                        if (pkg.status == PCPPackageStatus.Unused)
                            rawPenalty += kUnusedPackage;
                        else if (pkg.status == PCPPackageStatus.TransitiveOnly)
                            rawPenalty += kPackageOther;
                    }
                }

                // Size entries – only count those with optimization suggestions
                if (sizeEntries != null)
                {
                    foreach (var se in sizeEntries)
                    {
                        if (se.hasOptimizationSuggestion)
                            rawPenalty += kSizeOptimizable;
                    }
                }

                // ── Normalize relative to project size ─────────────────
                // Scale the penalty down for larger projects so a project
                // with 10 000 assets and 20 findings isn't punished as
                // harshly as a project with 100 assets and 20 findings.
                int assetCount = Math.Max(totalAssetsScanned, 1);
                float scaleFactor = 100f / (float)Math.Sqrt(assetCount);

                // ── Exponential decay: score = 100 * e^(-k * penalty) ─
                // k is tuned so that ~50 weighted penalty points on a
                // medium project (~1000 assets) yields roughly 50%.
                float k = 0.02f * scaleFactor / 100f * (float)Math.Sqrt(100);
                float score = 100f * (float)Math.Exp(-k * rawPenalty);

                return Math.Max(0, Math.Min(100, (int)Math.Round(score)));
            }
        }

        /// <summary>Number of circular dependency chains found.</summary>
        public int CircularDependencyCount => circularDependencies?.Count ?? 0;

        /// <summary>Number of orphan assets found.</summary>
        public int OrphanAssetCount => orphanAssets?.Count ?? 0;

        /// <summary>
        /// Removes all findings that reference any of the given asset paths.
        /// Used by the smart-delete path to update results without rescanning.
        /// </summary>
        public void RemovePaths(HashSet<string> deletedPaths)
        {
            if (deletedPaths == null || deletedPaths.Count == 0)
                return;

            unusedAssets?.RemoveAll(a => deletedPaths.Contains(a.Path));
            sizeEntries?.RemoveAll(e => deletedPaths.Contains(e.path));
            missingReferences?.RemoveAll(m => deletedPaths.Contains(m.sourceAssetPath));
            shaderEntries?.RemoveAll(s => deletedPaths.Contains(s.assetPath));
            orphanAssets?.RemoveAll(o => deletedPaths.Contains(o));

            if (duplicateGroups != null)
            {
                foreach (var group in duplicateGroups)
                    group.entries?.RemoveAll(e => deletedPaths.Contains(e.path));
                duplicateGroups.RemoveAll(g => g.entries == null || g.entries.Count < 2);
            }
        }

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
            circularDependencies.Clear();
            orphanAssets.Clear();
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
                   $"  Circular deps:       {circularDependencies.Count}\n" +
                   $"  Orphan assets:       {orphanAssets.Count}\n" +
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

        /// <summary>
        /// Collects results from a module into this scan result.
        /// Shared by both the orchestrator (manifest computation) and the window.
        /// </summary>
        public static void CollectModuleResults(IPCPModule module, PCPScanResult result)
        {
            switch (module.Id)
            {
                case PCPModuleId.Unused when module is PCPUnusedScanner u:
                    result.unusedAssets.AddRange(u.Results);
                    break;
                case PCPModuleId.Missing when module is PCPMissingRefScanner m:
                    result.missingReferences.AddRange(m.Results);
                    break;
                case PCPModuleId.Duplicates when module is PCPDuplicateDetector d:
                    result.duplicateGroups.AddRange(d.Results);
                    break;
                case PCPModuleId.Dependencies when module is PCPDependencyModule dep:
                    result.circularDependencies.AddRange(dep.CircularDependencies);
                    result.orphanAssets.AddRange(dep.OrphanAssets);
                    break;
                case PCPModuleId.Packages when module is PCPPackageAuditor p:
                    result.packageAuditEntries.AddRange(p.Results);
                    break;
                case PCPModuleId.Shaders when module is PCPShaderAnalyzer s:
                    result.shaderEntries.AddRange(s.Results);
                    break;
                case PCPModuleId.Size when module is PCPSizeProfiler z:
                    result.sizeEntries.AddRange(z.Results);
                    break;
            }
        }
    }
}
