using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    // ----------------------------------------------------------------
    // Report exporter
    // ----------------------------------------------------------------

    /// <summary>
    /// Exports <see cref="PCPScanResult"/> data to JSON, CSV, or self-contained HTML reports.
    /// </summary>
    public static class PCPReportExporter
    {
        // ================================================================
        // JSON Export
        // ================================================================

        /// <summary>
        /// Exports the scan result as a structured JSON file.
        /// </summary>
        /// <param name="result">The scan result to export.</param>
        /// <param name="outputPath">
        /// Absolute file path. If null, a save-file dialog is shown.
        /// </param>
        public static void ExportJSON(PCPScanResult result, string outputPath = null)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            bool isExplicitPath = !string.IsNullOrEmpty(outputPath);

            if (!isExplicitPath)
            {
                outputPath = EditorUtility.SaveFilePanel(
                    "Export Report as JSON", "", "ProjectCleanPro_Report.json", "json");
                if (string.IsNullOrEmpty(outputPath))
                    return; // User cancelled.
            }

            string json = JsonUtility.ToJson(result, true);
            File.WriteAllText(outputPath, json, Encoding.UTF8);

            if (!isExplicitPath)
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    $"JSON report exported successfully.\n\n{outputPath}", "OK");
        }

        // ================================================================
        // CSV Export
        // ================================================================

        /// <summary>
        /// Exports the scan result as a flat CSV file with one row per finding.
        /// </summary>
        /// <param name="result">The scan result to export.</param>
        /// <param name="outputPath">
        /// Absolute file path. If null, a save-file dialog is shown.
        /// </param>
        public static void ExportCSV(PCPScanResult result, string outputPath = null)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            bool isExplicitPath = !string.IsNullOrEmpty(outputPath);

            if (!isExplicitPath)
            {
                outputPath = EditorUtility.SaveFilePanel(
                    "Export Report as CSV", "", "ProjectCleanPro_Report.csv", "csv");
                if (string.IsNullOrEmpty(outputPath))
                    return;
            }

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("Module,Path,Detail1,Detail2,Detail3,SizeBytes,Severity");

            // Unused Assets
            if (result.unusedAssets != null)
            {
                foreach (var asset in result.unusedAssets)
                {
                    sb.AppendLine(CsvRow("Unused Asset",
                        asset.Path,
                        asset.assetInfo?.assetTypeName ?? "",
                        asset.suggestedAction ?? "",
                        asset.isInResources ? "In Resources" : "",
                        asset.SizeBytes,
                        ""));
                }
            }

            // Missing References
            if (result.missingReferences != null)
            {
                foreach (var mr in result.missingReferences)
                {
                    sb.AppendLine(CsvRow("Missing Reference",
                        mr.sourceAssetPath ?? "",
                        mr.componentType ?? "",
                        mr.propertyPath ?? "",
                        mr.gameObjectPath ?? "",
                        0,
                        mr.severity.ToString()));
                }
            }

            // Duplicates
            if (result.duplicateGroups != null)
            {
                foreach (var group in result.duplicateGroups)
                {
                    foreach (var entry in group.entries)
                    {
                        sb.AppendLine(CsvRow("Duplicate",
                            entry.path ?? "",
                            $"Hash: {group.hash}",
                            $"Refs: {entry.referenceCount}",
                            entry.isCanonical ? "Canonical" : "Duplicate",
                            entry.sizeBytes,
                            ""));
                    }
                }
            }

            // Package Audit
            if (result.packageAuditEntries != null)
            {
                foreach (var pkg in result.packageAuditEntries)
                {
                    sb.AppendLine(CsvRow("Package Audit",
                        pkg.packageName ?? "",
                        pkg.displayName ?? "",
                        pkg.version ?? "",
                        pkg.status.ToString(),
                        0,
                        ""));
                }
            }

            // Shader Analysis
            if (result.shaderEntries != null)
            {
                foreach (var shader in result.shaderEntries)
                {
                    sb.AppendLine(CsvRow("Shader",
                        shader.assetPath ?? "",
                        shader.shaderName ?? "",
                        $"Variants: {shader.estimatedVariants}",
                        shader.pipelineMismatch ? "MISMATCH" : shader.isUnused ? "UNUSED" : "OK",
                        shader.sizeBytes,
                        shader.GetSeverity().ToString()));
                }
            }

            // Size Profiler
            if (result.sizeEntries != null)
            {
                foreach (var entry in result.sizeEntries)
                {
                    sb.AppendLine(CsvRow("Size",
                        entry.path ?? "",
                        entry.assetTypeName ?? "",
                        entry.compressionInfo ?? "",
                        entry.hasOptimizationSuggestion ? entry.optimizationSuggestion ?? "" : "",
                        entry.sizeBytes,
                        ""));
                }
            }

            // Circular Dependencies
            if (result.circularDependencies != null)
            {
                foreach (var cd in result.circularDependencies)
                {
                    string chain = string.Join(" -> ", cd.chain);
                    sb.AppendLine(CsvRow("Circular Dependency",
                        chain,
                        $"{cd.chain.Count} assets in cycle",
                        "", "",
                        0,
                        "Warning"));
                }
            }

            // Orphan Assets
            if (result.orphanAssets != null)
            {
                foreach (var orphan in result.orphanAssets)
                {
                    sb.AppendLine(CsvRow("Orphan Asset",
                        orphan,
                        "", "", "",
                        0,
                        "Info"));
                }
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

            if (!isExplicitPath)
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    $"CSV report exported successfully.\n\n{outputPath}", "OK");
        }

        // ================================================================
        // HTML Export
        // ================================================================

        /// <summary>
        /// Exports the scan result as a self-contained HTML file with inline CSS and JavaScript,
        /// featuring a dark theme, summary section, and sortable tables.
        /// </summary>
        /// <param name="result">The scan result to export.</param>
        /// <param name="outputPath">
        /// Absolute file path. If null, a save-file dialog is shown.
        /// </param>
        public static void ExportHTML(PCPScanResult result, string outputPath = null)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            bool isExplicitPath = !string.IsNullOrEmpty(outputPath);

            if (!isExplicitPath)
            {
                outputPath = EditorUtility.SaveFilePanel(
                    "Export Report as HTML", "", "ProjectCleanPro_Report.html", "html");
                if (string.IsNullOrEmpty(outputPath))
                    return;
            }

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"UTF-8\">");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("<title>ProjectCleanPro Report</title>");
            html.AppendLine("<style>");
            html.AppendLine(GetCssStyles());
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            // ---- Header ----
            html.AppendLine("<div class=\"header\">");
            html.AppendLine("<h1>ProjectCleanPro Report</h1>");
            html.AppendLine($"<p class=\"meta\">Project: <strong>{HtmlEncode(result.projectName ?? "Unknown")}</strong> " +
                            $"| Unity: <strong>{HtmlEncode(result.unityVersion ?? "Unknown")}</strong> " +
                            $"| Scan time: <strong>{HtmlEncode(result.scanTimestampUtc ?? "N/A")}</strong> " +
                            $"| Duration: <strong>{result.scanDurationSeconds:F1}s</strong></p>");
            html.AppendLine("</div>");

            // ---- Summary cards ----
            html.AppendLine("<div class=\"summary\">");
            html.AppendLine(SummaryCard("Assets Scanned", result.totalAssetsScanned.ToString(), "#45B7D1"));
            html.AppendLine(SummaryCard("Unused Assets",
                $"{result.UnusedAssetCount} ({PCPAssetUtils.FormatSize(result.UnusedAssetsTotalSize)})",
                "#FF6B6B"));
            html.AppendLine(SummaryCard("Missing References", result.MissingReferenceCount.ToString(), "#4ECDC4"));
            html.AppendLine(SummaryCard("Duplicate Groups",
                $"{result.DuplicateGroupCount} ({PCPAssetUtils.FormatSize(result.DuplicateWastedSize)} wasted)",
                "#96CEB4"));
            int packageIssues = result.packageAuditEntries != null
                ? result.packageAuditEntries.Count(p => p.status == PCPPackageStatus.Unused)
                : 0;
            html.AppendLine(SummaryCard("Unused Packages", packageIssues.ToString(), "#FFEAA7"));
            html.AppendLine("</div>");

            // ---- Unused Assets section ----
            if (result.unusedAssets != null && result.unusedAssets.Count > 0)
            {
                html.AppendLine("<div class=\"section\">");
                html.AppendLine("<h2 class=\"section-title\" style=\"border-color: #FF6B6B;\">Unused Assets</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<thead><tr>");
                html.AppendLine("<th onclick=\"sortTable(this, 0)\">Path</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 1)\">Type</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 2)\" data-type=\"size\">Size</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 3)\">Suggested Action</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 4)\">In Resources</th>");
                html.AppendLine("</tr></thead>");
                html.AppendLine("<tbody>");

                foreach (var asset in result.unusedAssets)
                {
                    string sizeFormatted = asset.assetInfo != null
                        ? PCPAssetUtils.FormatSize(asset.SizeBytes)
                        : "0 B";
                    string resTag = asset.isInResources
                        ? "<span class=\"badge badge-warn\">Yes</span>"
                        : "No";

                    html.AppendLine("<tr>");
                    html.AppendLine($"<td>{HtmlEncode(asset.Path)}</td>");
                    html.AppendLine($"<td>{HtmlEncode(asset.assetInfo?.assetTypeName ?? "Unknown")}</td>");
                    html.AppendLine($"<td data-sort=\"{asset.SizeBytes}\">{sizeFormatted}</td>");
                    html.AppendLine($"<td>{HtmlEncode(asset.suggestedAction ?? "")}</td>");
                    html.AppendLine($"<td>{resTag}</td>");
                    html.AppendLine("</tr>");
                }

                html.AppendLine("</tbody></table>");
                html.AppendLine("</div>");
            }

            // ---- Missing References section ----
            if (result.missingReferences != null && result.missingReferences.Count > 0)
            {
                html.AppendLine("<div class=\"section\">");
                html.AppendLine("<h2 class=\"section-title\" style=\"border-color: #4ECDC4;\">Missing References</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<thead><tr>");
                html.AppendLine("<th onclick=\"sortTable(this, 0)\">Source Asset</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 1)\">GameObject Path</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 2)\">Component</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 3)\">Property</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 4)\">Severity</th>");
                html.AppendLine("</tr></thead>");
                html.AppendLine("<tbody>");

                foreach (var mr in result.missingReferences)
                {
                    string severityClass = mr.severity == PCPSeverity.Error ? "badge-err" :
                        mr.severity == PCPSeverity.Warning ? "badge-warn" : "badge-info";

                    html.AppendLine("<tr>");
                    html.AppendLine($"<td>{HtmlEncode(mr.sourceAssetPath ?? "")}</td>");
                    html.AppendLine($"<td>{HtmlEncode(mr.gameObjectPath ?? "")}</td>");
                    html.AppendLine($"<td>{HtmlEncode(mr.componentType ?? "")}</td>");
                    html.AppendLine($"<td>{HtmlEncode(mr.propertyPath ?? "")}</td>");
                    html.AppendLine($"<td><span class=\"badge {severityClass}\">{mr.severity}</span></td>");
                    html.AppendLine("</tr>");
                }

                html.AppendLine("</tbody></table>");
                html.AppendLine("</div>");
            }

            // ---- Duplicate Assets section ----
            if (result.duplicateGroups != null && result.duplicateGroups.Count > 0)
            {
                html.AppendLine("<div class=\"section\">");
                html.AppendLine("<h2 class=\"section-title\" style=\"border-color: #96CEB4;\">Duplicate Assets</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<thead><tr>");
                html.AppendLine("<th onclick=\"sortTable(this, 0)\">Hash</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 1)\">Path</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 2)\" data-type=\"size\">Size</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 3)\">References</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 4)\">Status</th>");
                html.AppendLine("</tr></thead>");
                html.AppendLine("<tbody>");

                foreach (var group in result.duplicateGroups)
                {
                    foreach (var entry in group.entries)
                    {
                        string status = entry.isCanonical
                            ? "<span class=\"badge badge-ok\">Keep</span>"
                            : "<span class=\"badge badge-warn\">Duplicate</span>";

                        string shortHash = !string.IsNullOrEmpty(group.hash) && group.hash.Length >= 12
                            ? group.hash.Substring(0, 12) + "..."
                            : group.hash ?? "";

                        html.AppendLine("<tr>");
                        html.AppendLine($"<td class=\"mono\">{HtmlEncode(shortHash)}</td>");
                        html.AppendLine($"<td>{HtmlEncode(entry.path ?? "")}</td>");
                        html.AppendLine($"<td data-sort=\"{entry.sizeBytes}\">{PCPAssetUtils.FormatSize(entry.sizeBytes)}</td>");
                        html.AppendLine($"<td>{entry.referenceCount}</td>");
                        html.AppendLine($"<td>{status}</td>");
                        html.AppendLine("</tr>");
                    }
                }

                html.AppendLine("</tbody></table>");
                html.AppendLine("</div>");
            }

            // ---- Package Audit section ----
            if (result.packageAuditEntries != null && result.packageAuditEntries.Count > 0)
            {
                html.AppendLine("<div class=\"section\">");
                html.AppendLine("<h2 class=\"section-title\" style=\"border-color: #FFEAA7;\">Package Audit</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<thead><tr>");
                html.AppendLine("<th onclick=\"sortTable(this, 0)\">Package</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 1)\">Version</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 2)\">Status</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 3)\">Direct Refs</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 4)\">Code Refs</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 5)\">Depended On By</th>");
                html.AppendLine("</tr></thead>");
                html.AppendLine("<tbody>");

                foreach (var pkg in result.packageAuditEntries)
                {
                    string statusClass = pkg.status == PCPPackageStatus.Unused ? "badge-warn" :
                        pkg.status == PCPPackageStatus.Used ? "badge-ok" : "badge-info";
                    string depsBy = pkg.dependedOnBy != null && pkg.dependedOnBy.Count > 0
                        ? string.Join(", ", pkg.dependedOnBy)
                        : "-";

                    html.AppendLine("<tr>");
                    html.AppendLine($"<td>{HtmlEncode(pkg.displayName ?? pkg.packageName ?? "")}</td>");
                    html.AppendLine($"<td>{HtmlEncode(pkg.version ?? "")}</td>");
                    html.AppendLine($"<td><span class=\"badge {statusClass}\">{pkg.status}</span></td>");
                    html.AppendLine($"<td>{pkg.directReferenceCount}</td>");
                    html.AppendLine($"<td>{pkg.codeReferenceCount}</td>");
                    html.AppendLine($"<td>{HtmlEncode(depsBy)}</td>");
                    html.AppendLine("</tr>");
                }

                html.AppendLine("</tbody></table>");
                html.AppendLine("</div>");
            }

            // ---- Shader Analysis section ----
            if (result.shaderEntries != null && result.shaderEntries.Count > 0)
            {
                html.AppendLine("<div class=\"section\">");
                html.AppendLine("<h2 class=\"section-title\" style=\"border-color: #DDA0DD;\">Shader Analysis</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<thead><tr>");
                html.AppendLine("<th onclick=\"sortTable(this, 0)\">Shader</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 1)\">Pipeline</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 2)\">Variants</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 3)\">Keywords</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 4)\">Materials</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 5)\" data-type=\"size\">Size</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 6)\">Status</th>");
                html.AppendLine("</tr></thead>");
                html.AppendLine("<tbody>");

                foreach (var shader in result.shaderEntries)
                {
                    string statusText;
                    string statusClass;
                    if (shader.pipelineMismatch)
                    {
                        statusText = "MISMATCH";
                        statusClass = "badge-err";
                    }
                    else if (shader.isUnused)
                    {
                        statusText = "UNUSED";
                        statusClass = "badge-warn";
                    }
                    else if (shader.estimatedVariants > 256)
                    {
                        statusText = "HIGH VARIANTS";
                        statusClass = "badge-warn";
                    }
                    else
                    {
                        statusText = "OK";
                        statusClass = "badge-ok";
                    }

                    html.AppendLine("<tr>");
                    html.AppendLine($"<td>{HtmlEncode(shader.shaderName ?? "")}</td>");
                    html.AppendLine($"<td>{shader.targetPipeline}</td>");
                    html.AppendLine($"<td>{shader.estimatedVariants}</td>");
                    html.AppendLine($"<td>{shader.keywordCount}</td>");
                    html.AppendLine($"<td>{shader.materialCount}</td>");
                    html.AppendLine($"<td data-sort=\"{shader.sizeBytes}\">{PCPAssetUtils.FormatSize(shader.sizeBytes)}</td>");
                    html.AppendLine($"<td><span class=\"badge {statusClass}\">{statusText}</span></td>");
                    html.AppendLine("</tr>");
                }

                html.AppendLine("</tbody></table>");
                html.AppendLine("</div>");
            }

            // ---- Size Profiler section (top 100 largest) ----
            if (result.sizeEntries != null && result.sizeEntries.Count > 0)
            {
                html.AppendLine("<div class=\"section\">");
                html.AppendLine("<h2 class=\"section-title\" style=\"border-color: #45B7D1;\">Size Profiler (Top 100)</h2>");
                html.AppendLine("<table>");
                html.AppendLine("<thead><tr>");
                html.AppendLine("<th onclick=\"sortTable(this, 0)\">Asset</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 1)\">Type</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 2)\" data-type=\"size\">Size</th>");
                html.AppendLine("<th onclick=\"sortTable(this, 3)\">Optimization</th>");
                html.AppendLine("</tr></thead>");
                html.AppendLine("<tbody>");

                // Sort by size descending, limit to top 100
                var sortedSize = new List<PCPSizeEntry>(result.sizeEntries);
                sortedSize.Sort((a, b) => b.sizeBytes.CompareTo(a.sizeBytes));
                int limit = Math.Min(sortedSize.Count, 100);

                for (int i = 0; i < limit; i++)
                {
                    var entry = sortedSize[i];
                    string suggestion = entry.hasOptimizationSuggestion
                        ? $"<span class=\"badge badge-warn\">OPTIMIZE</span> {HtmlEncode(entry.optimizationSuggestion ?? "")}"
                        : "<span class=\"badge badge-ok\">OK</span>";

                    html.AppendLine("<tr>");
                    html.AppendLine($"<td>{HtmlEncode(entry.path ?? "")}</td>");
                    html.AppendLine($"<td>{HtmlEncode(entry.assetTypeName ?? "")}</td>");
                    html.AppendLine($"<td data-sort=\"{entry.sizeBytes}\">{PCPAssetUtils.FormatSize(entry.sizeBytes)}</td>");
                    html.AppendLine($"<td>{suggestion}</td>");
                    html.AppendLine("</tr>");
                }

                html.AppendLine("</tbody></table>");
                html.AppendLine("</div>");
            }

            // ---- Footer ----
            html.AppendLine("<div class=\"footer\">");
            html.AppendLine($"<p>Generated by ProjectCleanPro on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
            html.AppendLine("</div>");

            // ---- Inline JavaScript for sorting ----
            html.AppendLine("<script>");
            html.AppendLine(GetJavaScript());
            html.AppendLine("</script>");

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            File.WriteAllText(outputPath, html.ToString(), Encoding.UTF8);

            if (!isExplicitPath)
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    $"HTML report exported successfully.\n\n{outputPath}", "OK");
        }

        // ================================================================
        // Markdown Export
        // ================================================================

        /// <summary>
        /// Exports the scan result as a summary-focused Markdown file.
        /// </summary>
        /// <param name="result">The scan result to export.</param>
        /// <param name="outputPath">
        /// Absolute file path. If null, a save-file dialog is shown.
        /// </param>
        public static void ExportMarkdown(PCPScanResult result, string outputPath = null)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            bool isExplicitPath = !string.IsNullOrEmpty(outputPath);

            if (!isExplicitPath)
            {
                outputPath = EditorUtility.SaveFilePanel(
                    "Export Report as Markdown", "", "ProjectCleanPro_Report.md", "md");
                if (string.IsNullOrEmpty(outputPath))
                    return;
            }

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("# ProjectCleanPro Report");
            sb.AppendLine();
            sb.AppendLine($"- **Project:** {result.projectName ?? "Unknown"}");
            sb.AppendLine($"- **Unity Version:** {result.unityVersion ?? "Unknown"}");
            sb.AppendLine($"- **Scan Date:** {result.scanTimestampUtc ?? "N/A"}");
            sb.AppendLine($"- **Scan Duration:** {result.scanDurationSeconds:F1}s");
            sb.AppendLine($"- **Assets Scanned:** {result.totalAssetsScanned}");
            sb.AppendLine();

            // Health summary
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"| Metric | Value |");
            sb.AppendLine($"|--------|-------|");
            sb.AppendLine($"| Health Score | **{result.HealthScore}/100** |");
            sb.AppendLine($"| Total Findings | {result.TotalFindingCount} |");
            sb.AppendLine($"| Estimated Savings | {result.FormattedWastedBytes} |");
            sb.AppendLine();

            // Module breakdown table
            sb.AppendLine("## Findings by Module");
            sb.AppendLine();
            sb.AppendLine("| Module | Findings | Size |");
            sb.AppendLine("|--------|----------|------|");

            sb.AppendLine($"| Unused Assets | {result.UnusedAssetCount} | {PCPAssetUtils.FormatSize(result.UnusedAssetsTotalSize)} |");
            sb.AppendLine($"| Missing References | {result.MissingReferenceCount} | - |");
            sb.AppendLine($"| Duplicate Assets | {result.DuplicateGroupCount} groups | {PCPAssetUtils.FormatSize(result.DuplicateWastedSize)} wasted |");

            int unusedPackages = result.packageAuditEntries != null
                ? result.packageAuditEntries.Count(p => p.status == PCPPackageStatus.Unused)
                : 0;
            sb.AppendLine($"| Unused Packages | {unusedPackages} | - |");

            int shaderIssues = result.shaderEntries != null
                ? result.shaderEntries.Count(s => s.GetSeverity() != PCPSeverity.Info)
                : 0;
            sb.AppendLine($"| Shader Issues | {shaderIssues} | - |");

            int sizeOptimizable = result.sizeEntries != null
                ? result.sizeEntries.Count(s => s.hasOptimizationSuggestion)
                : 0;
            sb.AppendLine($"| Size Optimizations | {sizeOptimizable} | - |");

            sb.AppendLine($"| Circular Dependencies | {result.CircularDependencyCount} | - |");
            sb.AppendLine($"| Orphan Assets | {result.OrphanAssetCount} | - |");
            sb.AppendLine();

            // Footer
            sb.AppendLine("---");
            sb.AppendLine($"*Generated by ProjectCleanPro on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

            if (!isExplicitPath)
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    $"Markdown report exported successfully.\n\n{outputPath}", "OK");
        }

        // ================================================================
        // CSS Styles (dark theme)
        // ================================================================

        private static string GetCssStyles()
        {
            return @"
:root {
    --bg-primary: #1a1a2e;
    --bg-secondary: #16213e;
    --bg-card: #0f3460;
    --bg-table-row: #1a1a3e;
    --bg-table-alt: #16213e;
    --bg-table-hover: #1f2b5e;
    --text-primary: #e0e0e0;
    --text-secondary: #a0a0b0;
    --accent: #4ECDC4;
    --border: #2a2a4a;
}

* { margin: 0; padding: 0; box-sizing: border-box; }

body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, sans-serif;
    background-color: var(--bg-primary);
    color: var(--text-primary);
    line-height: 1.6;
    padding: 20px;
}

.header {
    text-align: center;
    padding: 30px 20px;
    background: linear-gradient(135deg, var(--bg-secondary), var(--bg-card));
    border-radius: 12px;
    margin-bottom: 24px;
    border: 1px solid var(--border);
}

.header h1 {
    font-size: 28px;
    color: var(--accent);
    margin-bottom: 8px;
}

.header .meta {
    color: var(--text-secondary);
    font-size: 14px;
}

.summary {
    display: flex;
    flex-wrap: wrap;
    gap: 16px;
    margin-bottom: 30px;
    justify-content: center;
}

.summary-card {
    background: var(--bg-secondary);
    border-radius: 10px;
    padding: 20px 28px;
    min-width: 180px;
    text-align: center;
    border: 1px solid var(--border);
    border-top: 3px solid var(--accent);
    transition: transform 0.15s ease;
}

.summary-card:hover { transform: translateY(-2px); }
.summary-card .label { font-size: 13px; color: var(--text-secondary); text-transform: uppercase; letter-spacing: 0.5px; }
.summary-card .value { font-size: 22px; font-weight: 700; margin-top: 6px; }

.section {
    background: var(--bg-secondary);
    border-radius: 10px;
    padding: 24px;
    margin-bottom: 24px;
    border: 1px solid var(--border);
    overflow-x: auto;
}

.section-title {
    font-size: 20px;
    margin-bottom: 16px;
    padding-bottom: 8px;
    border-bottom: 2px solid var(--accent);
    display: inline-block;
}

table {
    width: 100%;
    border-collapse: collapse;
    font-size: 13px;
}

thead th {
    background: var(--bg-card);
    color: var(--accent);
    padding: 10px 14px;
    text-align: left;
    cursor: pointer;
    user-select: none;
    white-space: nowrap;
    position: sticky;
    top: 0;
    border-bottom: 2px solid var(--border);
}

thead th:hover { background: #1a4a7a; }
thead th::after { content: ' \2195'; font-size: 11px; opacity: 0.5; }
thead th.sort-asc::after { content: ' \2191'; opacity: 1; }
thead th.sort-desc::after { content: ' \2193'; opacity: 1; }

tbody tr { background: var(--bg-table-row); transition: background 0.12s ease; }
tbody tr:nth-child(even) { background: var(--bg-table-alt); }
tbody tr:hover { background: var(--bg-table-hover); }

td {
    padding: 8px 14px;
    border-bottom: 1px solid var(--border);
    word-break: break-all;
    max-width: 400px;
}

.mono { font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace; font-size: 12px; }

.badge {
    display: inline-block;
    padding: 2px 10px;
    border-radius: 12px;
    font-size: 11px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.3px;
}

.badge-err  { background: #5c1a1a; color: #ff6b6b; }
.badge-warn { background: #5c4a1a; color: #ffeaa7; }
.badge-info { background: #1a3a5c; color: #74b9ff; }
.badge-ok   { background: #1a5c3a; color: #55efc4; }

.footer {
    text-align: center;
    padding: 20px;
    color: var(--text-secondary);
    font-size: 12px;
    border-top: 1px solid var(--border);
    margin-top: 20px;
}
";
        }

        // ================================================================
        // Inline JavaScript (table sorting)
        // ================================================================

        private static string GetJavaScript()
        {
            return @"
function sortTable(header, colIndex) {
    var table = header.closest('table');
    var tbody = table.querySelector('tbody');
    var rows = Array.from(tbody.querySelectorAll('tr'));
    var headers = table.querySelectorAll('thead th');
    var isAsc = header.classList.contains('sort-asc');

    // Clear sort indicators from all headers in this table.
    headers.forEach(function(th) { th.classList.remove('sort-asc', 'sort-desc'); });

    var direction = isAsc ? -1 : 1;
    header.classList.add(isAsc ? 'sort-desc' : 'sort-asc');

    var isSize = header.getAttribute('data-type') === 'size';

    rows.sort(function(a, b) {
        var cellA = a.cells[colIndex];
        var cellB = b.cells[colIndex];
        var valA, valB;

        if (isSize) {
            valA = parseInt(cellA.getAttribute('data-sort') || '0', 10);
            valB = parseInt(cellB.getAttribute('data-sort') || '0', 10);
            return (valA - valB) * direction;
        }

        valA = (cellA.textContent || '').trim().toLowerCase();
        valB = (cellB.textContent || '').trim().toLowerCase();

        var numA = parseFloat(valA);
        var numB = parseFloat(valB);
        if (!isNaN(numA) && !isNaN(numB)) {
            return (numA - numB) * direction;
        }

        if (valA < valB) return -1 * direction;
        if (valA > valB) return 1 * direction;
        return 0;
    });

    rows.forEach(function(row) { tbody.appendChild(row); });
}
";
        }

        // ================================================================
        // Module subset & menu helpers
        // ================================================================

        /// <summary>
        /// Creates a copy of <paramref name="source"/> that only contains the
        /// data for a single module. Metadata (timestamps, project name, etc.)
        /// is preserved; all other module lists are left empty.
        /// </summary>
        /// <param name="source">The full scan result.</param>
        /// <param name="moduleKey">
        /// One of: "unused", "missing", "duplicates", "dependencies", "packages", "shaders", "size".
        /// </param>
        public static PCPScanResult CreateModuleSubset(PCPScanResult source, string moduleKey)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var subset = new PCPScanResult
            {
                scanTimestampUtc    = source.scanTimestampUtc,
                scanDurationSeconds = source.scanDurationSeconds,
                projectName         = source.projectName,
                unityVersion        = source.unityVersion,
                totalAssetsScanned  = source.totalAssetsScanned
            };

            switch (moduleKey)
            {
                case "unused":
                    subset.unusedAssets = new List<PCPUnusedAsset>(source.unusedAssets);
                    break;
                case "missing":
                    subset.missingReferences = new List<PCPMissingReference>(source.missingReferences);
                    break;
                case "duplicates":
                    subset.duplicateGroups = new List<PCPDuplicateGroup>(source.duplicateGroups);
                    break;
                case "dependencies":
                    subset.circularDependencies = new List<PCPCircularDependency>(source.circularDependencies);
                    subset.orphanAssets = new List<string>(source.orphanAssets);
                    break;
                case "packages":
                    subset.packageAuditEntries = new List<PCPPackageAuditEntry>(source.packageAuditEntries);
                    break;
                case "shaders":
                    subset.shaderEntries = new List<PCPShaderEntry>(source.shaderEntries);
                    break;
                case "size":
                    subset.sizeEntries = new List<PCPSizeEntry>(source.sizeEntries);
                    break;
            }

            return subset;
        }

        /// <summary>
        /// Shows a context menu with JSON / CSV / HTML export options for the
        /// given scan result.
        /// </summary>
        public static void ShowExportMenu(PCPScanResult result)
        {
            if (result == null)
                return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Export as JSON"), false,
                () => ExportJSON(result));
            menu.AddItem(new GUIContent("Export as CSV"), false,
                () => ExportCSV(result));
            menu.AddItem(new GUIContent("Export as HTML"), false,
                () => ExportHTML(result));
            menu.AddItem(new GUIContent("Export as Markdown"), false,
                () => ExportMarkdown(result));
            menu.ShowAsContext();
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static string SummaryCard(string label, string value, string color)
        {
            return $"<div class=\"summary-card\" style=\"border-top-color: {color};\">" +
                   $"<div class=\"label\">{HtmlEncode(label)}</div>" +
                   $"<div class=\"value\" style=\"color: {color};\">{HtmlEncode(value)}</div></div>";
        }

        private static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static string CsvRow(string module, string path, string detail1,
            string detail2, string detail3, long sizeBytes, string severity)
        {
            return string.Join(",",
                CsvEscape(module),
                CsvEscape(path),
                CsvEscape(detail1),
                CsvEscape(detail2),
                CsvEscape(detail3),
                sizeBytes.ToString(),
                CsvEscape(severity));
        }
    }
}
