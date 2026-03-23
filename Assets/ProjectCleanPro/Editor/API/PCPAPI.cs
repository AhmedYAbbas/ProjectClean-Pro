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
        // String → PCPModuleId mapping
        // ----------------------------------------------------------------

        private static readonly Dictionary<string, PCPModuleId> s_ModuleIdMap =
            new Dictionary<string, PCPModuleId>(StringComparer.OrdinalIgnoreCase)
            {
                { "unused",       PCPModuleId.Unused },
                { "missing",      PCPModuleId.Missing },
                { "duplicates",   PCPModuleId.Duplicates },
                { "dependencies", PCPModuleId.Dependencies },
                { "packages",     PCPModuleId.Packages },
                { "shaders",      PCPModuleId.Shaders },
                { "size",         PCPModuleId.Size },
            };

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

            // ---- Initialize global context ----
            PCPContext.Initialize();

            // ---- Apply transient options on top of persisted settings ----
            var settings = PCPSettings.instance;
            bool prevIncludeAll = settings.includeAllScenes;
            if (options.IncludeAllScenes)
                settings.includeAllScenes = true;

            try
            {
                // ---- Build scan context ----
                List<string> customRoots = options.AdditionalScanRoots;
                var context = PCPScanContext.FromGlobalContext(customRoots);
                context.OnProgress = options.VerboseLogging
                    ? (float p, string label) =>
                        Debug.Log($"[PCPAPI]   {label} ({p * 100f:F0}%)")
                    : (Action<float, string>)null;

                // ---- Determine modules ----
                bool isFilteredScan = options.Modules != null && options.Modules.Length > 0;

                PCPScanOrchestrator orchestrator;
                PCPScanManifest manifest;

                if (!isFilteredScan)
                {
                    // Full scan — delegate to the global orchestrator.
                    orchestrator = PCPContext.Orchestrator;

                    if (options.VerboseLogging)
                        Debug.Log("[PCPAPI] Starting full scan (all modules).");

                    manifest = orchestrator.ScanAllSync(context, context.OnProgress);
                }
                else
                {
                    // Filtered scan — create a temporary orchestrator with only
                    // the requested modules so the orchestrator handles ordering,
                    // graph building, caching, and finalization.
                    var filteredModules = BuildFilteredModuleList(options.Modules);

                    if (options.VerboseLogging)
                        Debug.Log($"[PCPAPI] Starting filtered scan — {filteredModules.Count} module(s): " +
                                  string.Join(", ", filteredModules.ConvertAll(m => m.Id.ToString())));

                    if (filteredModules.Count == 0)
                    {
                        Debug.LogWarning("[PCPAPI] No valid modules matched the filter. Returning empty result.");
                        return new PCPScanResult
                        {
                            scanTimestampUtc = DateTime.UtcNow.ToString("o"),
                            projectName = Application.productName,
                            unityVersion = Application.unityVersion,
                        };
                    }

                    orchestrator = new PCPScanOrchestrator(filteredModules, PCPContext.ResultCacheManager);
                    manifest = orchestrator.ScanAllSync(context, context.OnProgress);
                }

                // ---- Populate backward-compatible PCPScanResult ----
                var result = PopulateResult(orchestrator, manifest);

                // Store on context for report exporter and other consumers.
                PCPContext.LastScanResult = result;
                PCPContext.LastScanManifest = manifest;

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
            finally
            {
                // ---- Restore mutated settings ----
                if (options.IncludeAllScenes)
                    settings.includeAllScenes = prevIncludeAll;
            }
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

        /// <summary>
        /// Converts string module filter into a list of concrete module instances.
        /// Used for filtered (partial) scans where a temporary orchestrator is created.
        /// </summary>
        private static List<IPCPModule> BuildFilteredModuleList(string[] moduleFilter)
        {
            var filter = new HashSet<PCPModuleId>();
            foreach (var name in moduleFilter)
            {
                if (s_ModuleIdMap.TryGetValue(name, out var id))
                    filter.Add(id);
                else
                    Debug.LogWarning($"[PCPAPI] Unknown module id '{name}' — skipping.");
            }

            // Create fresh module instances for the temporary orchestrator.
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

            return all.FindAll(m => filter.Contains(m.Id));
        }

        /// <summary>
        /// Populates a <see cref="PCPScanResult"/> from the orchestrator's module
        /// results and the scan manifest. Provides backward compatibility for callers
        /// that consume the flat result object.
        /// </summary>
        private static PCPScanResult PopulateResult(PCPScanOrchestrator orchestrator, PCPScanManifest manifest)
        {
            var result = new PCPScanResult
            {
                scanTimestampUtc = manifest.scanTimestampUtc,
                scanDurationSeconds = manifest.scanDurationSeconds,
                projectName = manifest.projectName,
                unityVersion = manifest.unityVersion,
                totalAssetsScanned = manifest.totalAssetsScanned,
            };

            var unused = orchestrator.GetModule(PCPModuleId.Unused) as PCPUnusedScanner;
            if (unused != null)
                result.unusedAssets = new List<PCPUnusedAsset>(unused.Results);

            var missing = orchestrator.GetModule(PCPModuleId.Missing) as PCPMissingRefScanner;
            if (missing != null)
                result.missingReferences = new List<PCPMissingReference>(missing.Results);

            var duplicates = orchestrator.GetModule(PCPModuleId.Duplicates) as PCPDuplicateDetector;
            if (duplicates != null)
                result.duplicateGroups = new List<PCPDuplicateGroup>(duplicates.Results);

            var deps = orchestrator.GetModule(PCPModuleId.Dependencies) as PCPDependencyModule;
            if (deps != null)
            {
                result.circularDependencies = new List<PCPCircularDependency>(deps.CircularDependencies);
                result.orphanAssets = new List<string>(deps.OrphanAssets);
            }

            var packages = orchestrator.GetModule(PCPModuleId.Packages) as PCPPackageAuditor;
            if (packages != null)
                result.packageAuditEntries = new List<PCPPackageAuditEntry>(packages.Results);

            var shaders = orchestrator.GetModule(PCPModuleId.Shaders) as PCPShaderAnalyzer;
            if (shaders != null)
                result.shaderEntries = new List<PCPShaderEntry>(shaders.Results);

            var size = orchestrator.GetModule(PCPModuleId.Size) as PCPSizeProfiler;
            if (size != null)
                result.sizeEntries = new List<PCPSizeEntry>(size.Results);

            return result;
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
