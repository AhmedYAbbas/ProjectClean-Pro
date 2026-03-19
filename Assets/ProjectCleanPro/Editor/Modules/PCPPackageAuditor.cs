using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 5 - Package Auditor.
    /// Analyses installed UPM packages to determine which are actively used,
    /// unused, or only present as transitive dependencies.
    /// </summary>
    public sealed class PCPPackageAuditor : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override string ModuleId => "packages";
        public override string DisplayName => "Packages";
        public override string Icon => "\u2750"; // ❐
        public override Color AccentColor => new Color(0.086f, 0.627f, 0.522f, 1f); // #16A085

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        private readonly List<PCPPackageAuditEntry> _results = new List<PCPPackageAuditEntry>();

        /// <summary>Read-only access to the scan results.</summary>
        public IReadOnlyList<PCPPackageAuditEntry> Results => _results;

        /// <summary>Returns the number of packages classified as unused.</summary>
        public override int FindingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _results.Count; i++)
                {
                    if (_results[i].status == PCPPackageStatus.Unused)
                        count++;
                }
                return count;
            }
        }

        public override long TotalSizeBytes => 0L;

        // ----------------------------------------------------------------
        // Regex for detecting using directives
        // ----------------------------------------------------------------

        private static readonly Regex s_UsingRegex =
            new Regex(@"^\s*using\s+([\w\.]+)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override void DoScan(PCPScanContext context)
        {
            _results.Clear();

            // ----------------------------------------------------------
            // Phase 1: Gather all installed packages
            // ----------------------------------------------------------
            ReportProgress(0f, "Gathering installed packages...");

            PackageInfo[] allPackages;
            try
            {
                allPackages = PackageInfo.GetAllRegisteredPackages();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to get packages: {ex.Message}");
                return;
            }

            if (allPackages == null || allPackages.Length == 0)
            {
                ReportProgress(1f, "No packages found.");
                return;
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 2: Build a map of package assembly names and namespaces
            // ----------------------------------------------------------
            ReportProgress(0.05f, "Analysing package assemblies...");

            // Map: assembly name -> package name.
            var assemblyToPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Map: namespace prefix -> package name (approximate; uses assembly root namespace or name).
            var namespaceToPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Map: package name -> PackageInfo.
            var packageMap = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < allPackages.Length; i++)
            {
                PackageInfo pkg = allPackages[i];
                if (string.IsNullOrEmpty(pkg.name))
                    continue;

                packageMap[pkg.name] = pkg;

                // Find .asmdef files inside the package to discover assembly names.
                string packagePath = pkg.assetPath;
                if (!string.IsNullOrEmpty(packagePath))
                {
                    string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset",
                        new[] { packagePath });

                    for (int j = 0; j < asmdefGuids.Length; j++)
                    {
                        string asmdefPath = AssetDatabase.GUIDToAssetPath(asmdefGuids[j]);
                        string asmdefName = Path.GetFileNameWithoutExtension(asmdefPath);

                        if (!string.IsNullOrEmpty(asmdefName))
                        {
                            assemblyToPackage[asmdefName] = pkg.name;

                            // Use the asmdef name as a namespace approximation.
                            // Many Unity packages use their assembly name as the root namespace.
                            namespaceToPackage[asmdefName] = pkg.name;
                        }

                        // Try to read the asmdef to extract the rootNamespace if specified.
                        try
                        {
                            string asmdefFullPath = Path.GetFullPath(asmdefPath);
                            if (File.Exists(asmdefFullPath))
                            {
                                string asmdefContent = File.ReadAllText(asmdefFullPath);
                                string rootNs = ExtractJsonField(asmdefContent, "rootNamespace");
                                if (!string.IsNullOrEmpty(rootNs))
                                {
                                    namespaceToPackage[rootNs] = pkg.name;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore read errors.
                        }
                    }
                }
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 3: Gather project assembly references
            // ----------------------------------------------------------
            ReportProgress(0.15f, "Checking project assembly references...");

            // Set of package names that are referenced by project assemblies.
            var referencedByAssembly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var projectAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
                if (projectAssemblies != null)
                {
                    for (int i = 0; i < projectAssemblies.Length; i++)
                    {
                        var assembly = projectAssemblies[i];
                        if (assembly.assemblyReferences == null)
                            continue;

                        for (int j = 0; j < assembly.assemblyReferences.Length; j++)
                        {
                            string refName = assembly.assemblyReferences[j].name;
                            if (assemblyToPackage.TryGetValue(refName, out string pkgName))
                            {
                                referencedByAssembly.Add(pkgName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to get compilation assemblies: {ex.Message}");
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 4: Scan project .asmdef files for assembly references
            // ----------------------------------------------------------
            ReportProgress(0.25f, "Scanning project assembly definitions...");

            string[] projectAsmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset",
                new[] { "Assets" });

            for (int i = 0; i < projectAsmdefGuids.Length; i++)
            {
                if (ShouldCancel()) return;

                string asmdefPath = AssetDatabase.GUIDToAssetPath(projectAsmdefGuids[i]);
                string fullPath = Path.GetFullPath(asmdefPath);

                if (!File.Exists(fullPath))
                    continue;

                try
                {
                    string content = File.ReadAllText(fullPath);

                    // Look for assembly references in the JSON.
                    foreach (var kvp in assemblyToPackage)
                    {
                        if (content.Contains(kvp.Key))
                        {
                            referencedByAssembly.Add(kvp.Value);
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore read errors.
                }
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 5: Scan project .cs files for using directives
            // ----------------------------------------------------------
            ReportProgress(0.35f, "Scanning C# source files for using directives...");

            var codeReferenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] csFileGuids = AssetDatabase.FindAssets("t:Script", new[] { "Assets" });

            int totalCsFiles = csFileGuids.Length;
            for (int i = 0; i < totalCsFiles; i++)
            {
                if (ShouldCancel()) return;

                if ((i & 63) == 0)
                {
                    float pct = 0.35f + 0.35f * ((float)i / Math.Max(totalCsFiles, 1));
                    ReportProgress(pct, $"Scanning source file {i}/{totalCsFiles}...");
                }

                string csPath = AssetDatabase.GUIDToAssetPath(csFileGuids[i]);
                if (!csPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                string csFullPath = Path.GetFullPath(csPath);
                if (!File.Exists(csFullPath))
                    continue;

                string sourceContent;
                try
                {
                    sourceContent = File.ReadAllText(csFullPath);
                }
                catch (Exception)
                {
                    continue;
                }

                MatchCollection matches = s_UsingRegex.Matches(sourceContent);
                foreach (Match match in matches)
                {
                    string ns = match.Groups[1].Value;

                    // Check if this namespace matches any package namespace.
                    foreach (var kvp in namespaceToPackage)
                    {
                        if (ns.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ns, kvp.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            string pkgName = kvp.Value;
                            if (!codeReferenceCounts.ContainsKey(pkgName))
                                codeReferenceCounts[pkgName] = 0;
                            codeReferenceCounts[pkgName]++;
                            break;
                        }
                    }
                }
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 6: Build package dependency graph
            // ----------------------------------------------------------
            ReportProgress(0.75f, "Building package dependency graph...");

            // Map: package name -> list of packages that depend on it.
            var dependedOnBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in packageMap)
            {
                PackageInfo pkg = kvp.Value;
                if (pkg.dependencies == null)
                    continue;

                for (int i = 0; i < pkg.dependencies.Length; i++)
                {
                    string depName = pkg.dependencies[i].name;

                    if (!dependedOnBy.TryGetValue(depName, out List<string> depByList))
                    {
                        depByList = new List<string>();
                        dependedOnBy[depName] = depByList;
                    }
                    depByList.Add(pkg.name);
                }
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 7: Classify each package
            // ----------------------------------------------------------
            ReportProgress(0.85f, "Classifying packages...");

            for (int i = 0; i < allPackages.Length; i++)
            {
                PackageInfo pkg = allPackages[i];
                if (string.IsNullOrEmpty(pkg.name))
                    continue;

                bool hasAssemblyRef = referencedByAssembly.Contains(pkg.name);
                int codeRefCount = 0;
                codeReferenceCounts.TryGetValue(pkg.name, out codeRefCount);

                var entry = new PCPPackageAuditEntry
                {
                    packageName = pkg.name,
                    version = pkg.version ?? string.Empty,
                    displayName = !string.IsNullOrEmpty(pkg.displayName) ? pkg.displayName : pkg.name,
                    description = pkg.description ?? string.Empty,
                    directReferenceCount = hasAssemblyRef ? 1 : 0,
                    codeReferenceCount = codeRefCount
                };

                // Add dependedOnBy info.
                if (dependedOnBy.TryGetValue(pkg.name, out List<string> depBy))
                {
                    entry.dependedOnBy = new List<string>(depBy);
                }

                // Classify status.
                bool hasDirectUsage = hasAssemblyRef || codeRefCount > 0;
                bool hasDependants = entry.dependedOnBy.Count > 0;

                if (hasDirectUsage)
                {
                    entry.status = PCPPackageStatus.Used;
                }
                else if (hasDependants)
                {
                    // Check if any of the packages that depend on this one are Used.
                    bool anyUsedDependant = false;
                    for (int j = 0; j < entry.dependedOnBy.Count; j++)
                    {
                        string depPkgName = entry.dependedOnBy[j];
                        if (referencedByAssembly.Contains(depPkgName) ||
                            (codeReferenceCounts.ContainsKey(depPkgName) && codeReferenceCounts[depPkgName] > 0))
                        {
                            anyUsedDependant = true;
                            break;
                        }
                    }

                    entry.status = anyUsedDependant
                        ? PCPPackageStatus.TransitiveOnly
                        : PCPPackageStatus.Unused;
                }
                else
                {
                    entry.status = PCPPackageStatus.Unused;
                }

                _results.Add(entry);
            }

            // Sort: unused first, then transitive, then used.
            _results.Sort((a, b) =>
            {
                int statusCmp = GetStatusPriority(a.status).CompareTo(GetStatusPriority(b.status));
                if (statusCmp != 0)
                    return statusCmp;
                return string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
            });

            ReportProgress(1f, $"Audited {_results.Count} packages, {FindingCount} unused.");
        }

        public override void Clear()
        {
            base.Clear();
            _results.Clear();
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Sort priority: unused = 0 (first), transitive = 1, unknown = 2, used = 3 (last).
        /// </summary>
        private static int GetStatusPriority(PCPPackageStatus status)
        {
            switch (status)
            {
                case PCPPackageStatus.Unused: return 0;
                case PCPPackageStatus.TransitiveOnly: return 1;
                case PCPPackageStatus.Unknown: return 2;
                case PCPPackageStatus.Used: return 3;
                default: return 4;
            }
        }

        /// <summary>
        /// Naive extraction of a JSON string field value.
        /// Handles simple cases like: "rootNamespace": "MyNamespace"
        /// </summary>
        private static string ExtractJsonField(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\"\\s*:\\s*\"([^\"]*)\"";
            Match match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
