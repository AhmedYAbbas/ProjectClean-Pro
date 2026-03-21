using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    // ----------------------------------------------------------------
    // Options
    // ----------------------------------------------------------------

    /// <summary>
    /// Controls which modules run and how <see cref="PCPAPI.RunScan"/> behaves.
    /// </summary>
    public sealed class PCPScanOptions
    {
        /// <summary>
        /// Modules to run. Pass null or an empty array to run all modules.
        /// Valid ids: "unused", "missing", "duplicates", "dependencies",
        ///            "packages", "shaders", "size".
        /// </summary>
        public string[] Modules;

        /// <summary>
        /// When true, scan all scenes in the project rather than only those
        /// listed in Build Settings.
        /// </summary>
        public bool IncludeAllScenes;

        /// <summary>
        /// When true, include assets inside Packages/ in the unused-asset scan.
        /// </summary>
        public bool IncludePackageAssets;

        /// <summary>
        /// Extra folder paths to treat as scan roots in addition to the defaults.
        /// </summary>
        public List<string> AdditionalScanRoots;

        /// <summary>
        /// When true, write verbose progress messages to the Unity console.
        /// Useful for batch-mode CI runs where you want a detailed log.
        /// </summary>
        public bool VerboseLogging;

        /// <summary>
        /// Optional cancellation support. When set, the API will check the
        /// token after each module completes.
        /// </summary>
        public System.Threading.CancellationToken CancellationToken;
    }

    /// <summary>
    /// Output format for <see cref="PCPAPI.ExportReport"/>.
    /// </summary>
    public enum PCPReportFormat
    {
        JSON,
        CSV,
        HTML,
    }

    // ----------------------------------------------------------------
    // Main API surface
    // ----------------------------------------------------------------

    /// <summary>
    /// Public scripting API for ProjectCleanPro.
    /// <para>
    /// Allows other editor scripts, CI pipelines, and custom tools to drive
    /// scans and export reports without opening the editor window.
    /// </para>
    /// <example>
    /// <code>
    /// // Run all modules and export a JSON report:
    /// var result = PCPAPI.RunScan();
    /// PCPAPI.ExportReport(result, PCPReportFormat.JSON, "C:/Reports/scan.json");
    ///
    /// // Run only the unused-asset and duplicate modules:
    /// var result = PCPAPI.RunScan(new PCPScanOptions
    /// {
    ///     Modules = new[] { "unused", "duplicates" },
    ///     VerboseLogging = true,
    /// });
    /// </code>
    /// </example>
    /// </summary>
    public static class PCPAPI
    {
        // ----------------------------------------------------------------
        // Scan
        // ----------------------------------------------------------------

        /// <summary>
        /// Runs one or more PCP scan modules synchronously and returns the aggregated result.
        /// Safe to call from editor scripts and batch-mode entry points.
        /// </summary>
        /// <param name="options">
        /// Optional configuration. Pass null to use defaults (all modules, project settings).
        /// </param>
        /// <returns>Aggregated scan result.</returns>
        public static PCPScanResult RunScan(PCPScanOptions options = null)
        {
            options = options ?? new PCPScanOptions();

            var startTime = DateTime.UtcNow;
            var result = new PCPScanResult
            {
                scanTimestampUtc = startTime.ToString("o"),
                projectName      = Application.productName,
                unityVersion     = Application.unityVersion,
            };

            // ---- Build module list ----
            var modules = BuildModuleList(options.Modules);

            if (options.VerboseLogging)
                Debug.Log($"[PCPAPI] Starting scan — {modules.Count} module(s): " +
                          string.Join(", ", modules.ConvertAll(m => m.ModuleId)));

            // ---- Build scan context ----
            PCPContext.Initialize();

            // Apply transient options on top of persisted settings.
            var settings = PCPSettings.instance;
            bool prevIncludeAll = settings.includeAllScenes;
            if (options.IncludeAllScenes)
                settings.includeAllScenes = true;

            List<string> customRoots = options.AdditionalScanRoots;
            var context = PCPScanContext.FromGlobalContext(customRoots);
            context.OnProgress = options.VerboseLogging
                ? (float p, string label) =>
                    Debug.Log($"[PCPAPI]   {label} ({p * 100f:F0}%)")
                : (Action<float, string>)null;

            // ---- Run modules ----
            int totalAssets = AssetDatabase.GetAllAssetPaths().Length;
            result.totalAssetsScanned = totalAssets;

            foreach (var module in modules)
            {
                if (options.CancellationToken.IsCancellationRequested)
                {
                    Debug.LogWarning("[PCPAPI] Scan cancelled by caller.");
                    break;
                }

                if (options.VerboseLogging)
                    Debug.Log($"[PCPAPI] Running module: {module.DisplayName}");

                try
                {
                    module.Scan(context);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PCPAPI] Module '{module.ModuleId}' threw: {ex.Message}");
                }

                CollectModuleResults(module, result);
            }

            // ---- Restore mutated settings ----
            if (options.IncludeAllScenes)
                settings.includeAllScenes = prevIncludeAll;

            result.scanDurationSeconds = (float)(DateTime.UtcNow - startTime).TotalSeconds;

            if (options.VerboseLogging)
            {
                Debug.Log(
                    $"[PCPAPI] Scan complete in {result.scanDurationSeconds:F2}s — " +
                    $"Unused: {result.UnusedAssetCount}, " +
                    $"Missing: {result.MissingReferenceCount}, " +
                    $"Duplicate groups: {result.DuplicateGroupCount}");
            }

            return result;
        }

        // ----------------------------------------------------------------
        // Export
        // ----------------------------------------------------------------

        /// <summary>
        /// Exports a scan result to a file in the specified format.
        /// Unlike <see cref="PCPReportExporter"/> methods, this overload
        /// never shows a save-file dialog — it writes directly to
        /// <paramref name="outputPath"/>.
        /// </summary>
        /// <param name="result">The scan result to export.</param>
        /// <param name="format">Output format (JSON, CSV, or HTML).</param>
        /// <param name="outputPath">Absolute path to the output file.</param>
        public static void ExportReport(PCPScanResult result, PCPReportFormat format, string outputPath)
        {
            if (result == null)     throw new ArgumentNullException(nameof(result));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            switch (format)
            {
                case PCPReportFormat.JSON:
                    PCPReportExporter.ExportJSON(result, outputPath);
                    break;
                case PCPReportFormat.CSV:
                    PCPReportExporter.ExportCSV(result, outputPath);
                    break;
                case PCPReportFormat.HTML:
                    PCPReportExporter.ExportHTML(result, outputPath);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }
        }

        // ----------------------------------------------------------------
        // Module-result accessors (convenience helpers)
        // ----------------------------------------------------------------

        /// <summary>
        /// Runs only the unused-asset module and returns its findings.
        /// </summary>
        public static IReadOnlyList<PCPUnusedAsset> GetUnusedAssets(PCPScanOptions options = null)
        {
            options = MergeModules(options, "unused");
            var result = RunScan(options);
            return result.unusedAssets ?? new List<PCPUnusedAsset>();
        }

        /// <summary>
        /// Runs only the missing-reference module and returns its findings.
        /// </summary>
        public static IReadOnlyList<PCPMissingReference> GetMissingReferences(PCPScanOptions options = null)
        {
            options = MergeModules(options, "missing");
            var result = RunScan(options);
            return result.missingReferences ?? new List<PCPMissingReference>();
        }

        /// <summary>
        /// Runs only the duplicate-detector module and returns its findings.
        /// </summary>
        public static IReadOnlyList<PCPDuplicateGroup> GetDuplicateGroups(PCPScanOptions options = null)
        {
            options = MergeModules(options, "duplicates");
            var result = RunScan(options);
            return result.duplicateGroups ?? new List<PCPDuplicateGroup>();
        }

        /// <summary>
        /// Runs only the package-auditor module and returns its findings.
        /// </summary>
        public static IReadOnlyList<PCPPackageAuditEntry> GetPackageAudit(PCPScanOptions options = null)
        {
            options = MergeModules(options, "packages");
            var result = RunScan(options);
            return result.packageAudit ?? new List<PCPPackageAuditEntry>();
        }

        // ----------------------------------------------------------------
        // Internal helpers
        // ----------------------------------------------------------------

        private static List<IPCPModule> BuildModuleList(string[] moduleFilter)
        {
            // All available modules in run order.
            var all = new List<IPCPModule>
            {
                new PCPUnusedScanner(),
                new PCPMissingRefScanner(),
                new PCPDuplicateDetector(),
                new PCPDependencyModule(),
                new PCPPackageAuditor(),
                new PCPShaderAnalyzer(),
                new PCPSizeProfiler(),
            };

            if (moduleFilter == null || moduleFilter.Length == 0)
                return all;

            var filter = new HashSet<string>(moduleFilter, StringComparer.OrdinalIgnoreCase);
            return all.FindAll(m => filter.Contains(m.ModuleId));
        }

        private static void CollectModuleResults(IPCPModule module, PCPScanResult result)
        {
            switch (module.ModuleId)
            {
                case "unused" when module is PCPUnusedScanner u:
                    result.unusedAssets.AddRange(u.Results);
                    break;

                case "missing" when module is PCPMissingRefScanner m:
                    result.missingReferences.AddRange(m.Results);
                    break;

                case "duplicates" when module is PCPDuplicateDetector d:
                    result.duplicateGroups.AddRange(d.Results);
                    break;

                case "packages" when module is PCPPackageAuditor p:
                    result.packageAudit.AddRange(p.Results);
                    break;

                case "shaders" when module is PCPShaderAnalyzer s:
                    result.shaderEntries.AddRange(s.Results);
                    break;

                case "size" when module is PCPSizeProfiler sp:
                    result.sizeEntries.AddRange(sp.Results);
                    break;

                case "dependencies" when module is PCPDependencyModule dep:
                    result.circularDependencies.AddRange(dep.CircularDependencies);
                    result.orphanAssets.AddRange(dep.OrphanAssets);
                    break;
            }
        }

        private static PCPScanOptions MergeModules(PCPScanOptions options, string moduleId)
        {
            var merged = options != null
                ? new PCPScanOptions
                {
                    IncludeAllScenes    = options.IncludeAllScenes,
                    IncludePackageAssets = options.IncludePackageAssets,
                    AdditionalScanRoots = options.AdditionalScanRoots,
                    VerboseLogging      = options.VerboseLogging,
                    CancellationToken   = options.CancellationToken,
                }
                : new PCPScanOptions();

            merged.Modules = new[] { moduleId };
            return merged;
        }
    }
}
