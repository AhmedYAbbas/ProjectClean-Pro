using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Batch-mode CLI entry points for ProjectCleanPro.
    /// <para>
    /// Invoke from the command line with Unity's <c>-batchmode -executeMethod</c> flag.
    /// All entry points exit Unity with code 0 on success or 1 on failure.
    /// </para>
    ///
    /// <para><b>Supported command-line arguments:</b></para>
    /// <list type="bullet">
    ///   <item><term>-pcpOutput &lt;path&gt;</term>
    ///         <description>Absolute path for the exported report file.
    ///         Defaults to &lt;ProjectRoot&gt;/Library/ProjectCleanPro/report.&lt;ext&gt;</description></item>
    ///   <item><term>-pcpModules &lt;id1,id2,...&gt;</term>
    ///         <description>Comma-separated list of module ids to run.
    ///         Omit to run all modules.
    ///         Valid ids: unused, missing, duplicates, dependencies, packages, shaders, size</description></item>
    ///   <item><term>-pcpFormat json|csv|html</term>
    ///         <description>Overrides the output format for <see cref="ScanAndExport"/>.
    ///         Defaults to JSON.</description></item>
    ///   <item><term>-pcpAllScenes</term>
    ///         <description>When present, includes all scenes, not just Build Settings scenes.</description></item>
    ///   <item><term>-pcpFailOnFindings</term>
    ///         <description>Exit with code 1 when any findings are produced.
    ///         Useful for enforcing hygiene gates in CI pipelines.</description></item>
    ///   <item><term>-pcpVerbose</term>
    ///         <description>Write detailed per-asset progress to the log.</description></item>
    /// </list>
    ///
    /// <para><b>Example invocations:</b></para>
    /// <code>
    /// # Scan all, export JSON to default path
    /// Unity -batchmode -projectPath /path/to/project \
    ///   -executeMethod ProjectCleanPro.Editor.PCPCli.ScanAll -quit
    ///
    /// # Scan unused + duplicates, export HTML, fail pipeline on findings
    /// Unity -batchmode -projectPath /path/to/project \
    ///   -executeMethod ProjectCleanPro.Editor.PCPCli.ScanAndExport \
    ///   -pcpModules unused,duplicates \
    ///   -pcpFormat html \
    ///   -pcpOutput /tmp/report.html \
    ///   -pcpFailOnFindings \
    ///   -quit
    ///
    /// # Export CSV of missing references only
    /// Unity -batchmode -projectPath /path/to/project \
    ///   -executeMethod ProjectCleanPro.Editor.PCPCli.ScanAndExportCSV \
    ///   -pcpOutput /tmp/missing.csv \
    ///   -quit
    /// </code>
    /// </summary>
    public static class PCPCli
    {
        // ----------------------------------------------------------------
        // CLI entry points
        // ----------------------------------------------------------------

        /// <summary>
        /// Scans all modules and exports a JSON report to
        /// Library/ProjectCleanPro/report.json (or the path given by -pcpOutput).
        /// </summary>
        public static void ScanAll()
        {
            RunAndExit(PCPReportFormat.JSON, defaultExt: "json");
        }

        /// <summary>
        /// Flexible entry point. Reads format from <c>-pcpFormat</c> (default: JSON).
        /// </summary>
        public static void ScanAndExport()
        {
            string formatArg = GetArg("-pcpFormat") ?? "json";

            PCPReportFormat format;
            if (!Enum.TryParse(formatArg, ignoreCase: true, out format))
            {
                Debug.LogError($"[PCPCli] Unknown format '{formatArg}'. Expected: json, csv, html.");
                EditorApplication.Exit(1);
                return;
            }

            string ext = format.ToString().ToLowerInvariant();
            RunAndExit(format, defaultExt: ext);
        }

        /// <summary>
        /// Scans all modules and exports a JSON report.
        /// Equivalent to <c>-pcpFormat json</c> passed to <see cref="ScanAndExport"/>.
        /// </summary>
        public static void ScanAndExportJSON()
        {
            RunAndExit(PCPReportFormat.JSON, defaultExt: "json");
        }

        /// <summary>
        /// Scans all modules and exports a CSV report.
        /// </summary>
        public static void ScanAndExportCSV()
        {
            RunAndExit(PCPReportFormat.CSV, defaultExt: "csv");
        }

        /// <summary>
        /// Scans all modules and exports a self-contained HTML report.
        /// </summary>
        public static void ScanAndExportHTML()
        {
            RunAndExit(PCPReportFormat.HTML, defaultExt: "html");
        }

        // ----------------------------------------------------------------
        // Core logic
        // ----------------------------------------------------------------

        private static void RunAndExit(PCPReportFormat format, string defaultExt)
        {
            try
            {
                var options = BuildOptions();
                if (options.VerboseLogging)
                    Debug.Log("[PCPCli] Scan started.");

                PCPScanResult result = PCPAPI.RunScan(options);

                string outputPath = ResolveOutputPath(defaultExt);
                EnsureDirectoryExists(outputPath);

                PCPAPI.ExportReport(result, format, outputPath);

                Debug.Log(
                    $"[PCPCli] Report exported to: {outputPath}\n" +
                    $"  Unused assets   : {result.UnusedAssetCount}\n" +
                    $"  Missing refs    : {result.MissingReferenceCount}\n" +
                    $"  Duplicate groups: {result.DuplicateGroupCount}\n" +
                    $"  Scan duration   : {result.scanDurationSeconds:F2}s");

                bool failOnFindings = HasFlag("-pcpFailOnFindings");
                bool hasFindings = result.UnusedAssetCount > 0 ||
                                   result.MissingReferenceCount > 0 ||
                                   result.DuplicateGroupCount > 0;

                int exitCode = (failOnFindings && hasFindings) ? 1 : 0;

                if (exitCode != 0)
                    Debug.LogWarning("[PCPCli] Findings detected — exiting with code 1 (pcpFailOnFindings).");

                EditorApplication.Exit(exitCode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PCPCli] Fatal error: {ex}");
                EditorApplication.Exit(1);
            }
        }

        // ----------------------------------------------------------------
        // Argument parsing
        // ----------------------------------------------------------------

        private static PCPScanOptions BuildOptions()
        {
            var options = new PCPScanOptions
            {
                VerboseLogging   = HasFlag("-pcpVerbose"),
                IncludeAllScenes = HasFlag("-pcpAllScenes"),
            };

            string modulesArg = GetArg("-pcpModules");
            if (!string.IsNullOrEmpty(modulesArg))
            {
                options.Modules = modulesArg
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            return options;
        }

        private static string ResolveOutputPath(string extension)
        {
            string fromArg = GetArg("-pcpOutput");
            if (!string.IsNullOrEmpty(fromArg))
                return fromArg;

            // Default: Library/ProjectCleanPro/report.<ext>
            string libraryDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Library", "ProjectCleanPro");

            return Path.Combine(libraryDir, $"report.{extension}");
        }

        /// <summary>
        /// Returns the value of a named command-line argument, e.g.
        /// <c>GetArg("-pcpOutput")</c> returns "/tmp/report.json" for
        /// <c>-pcpOutput /tmp/report.json</c>.
        /// </summary>
        private static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        /// <summary>
        /// Returns true when a boolean flag is present in the command-line arguments.
        /// </summary>
        private static bool HasFlag(string name)
        {
            return System.Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureDirectoryExists(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
