using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ProjectCleanPro.Editor.Core;
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

        public override PCPModuleId Id => PCPModuleId.Packages;
        public override string DisplayName => "Packages";
        public override string Icon => "\u2750"; // ❐
        public override Color AccentColor => new Color(0.086f, 0.627f, 0.522f, 1f); // #16A085

        private static readonly HashSet<string> s_EmptyExtensions = new HashSet<string>();
        public override IReadOnlyCollection<string> RelevantExtensions => s_EmptyExtensions;
        public override bool RequiresDependencyGraph => false;

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

        protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
        {
            _results.Clear();

            // ----------------------------------------------------------
            // Phase 1: Gather all installed packages
            // ----------------------------------------------------------
            ReportProgress(0f, "Gathering installed packages...");

            PackageInfo[] allPackages;
            try
            {
#if UNITY_2021_1_OR_NEWER
                allPackages = PackageInfo.GetAllRegisteredPackages();
#else
                // GetAllRegisteredPackages() is not available before Unity 2021.1.
                // Fall back to the synchronous Client.List() request.
                var listRequest = UnityEditor.PackageManager.Client.List(true, false);
                while (!listRequest.IsCompleted)
                    System.Threading.Thread.Sleep(10);

                if (listRequest.Status != UnityEditor.PackageManager.StatusCode.Success)
                {
                    Debug.LogWarning("[ProjectCleanPro] Failed to list packages via Client.List().");
                    return;
                }

                allPackages = listRequest.Result.ToArray();
#endif
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

            ct.ThrowIfCancellationRequested();

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

            ct.ThrowIfCancellationRequested();

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

            ct.ThrowIfCancellationRequested();

            // ----------------------------------------------------------
            // Phase 4: Scan project .asmdef files for assembly references
            // ----------------------------------------------------------
            ReportProgress(0.25f, "Scanning project assembly definitions...");

            string[] projectAsmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset",
                new[] { "Assets" });

            for (int i = 0; i < projectAsmdefGuids.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

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

            ct.ThrowIfCancellationRequested();

            // ----------------------------------------------------------
            // Phase 5: Scan project .cs files for using directives
            // ----------------------------------------------------------
            ReportProgress(0.35f, "Scanning C# source files for using directives...");

            var codeReferenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] csFileGuids = AssetDatabase.FindAssets("t:Script", new[] { "Assets" });

            // Step 1: Main thread — resolve GUID paths and check cache.
            var allCsItems = new List<(string csPath, string[] cachedUsings)>(csFileGuids.Length);

            for (int i = 0; i < csFileGuids.Length; i++)
            {
                string csPath = AssetDatabase.GUIDToAssetPath(csFileGuids[i]);
                if (!csPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] usings = null;
                if (!context.Cache.IsStale(csPath))
                {
                    string cached = context.Cache.GetMetadata(csPath, "packages.usings");
                    if (cached != null)
                    {
                        usings = cached.Length > 0
                            ? cached.Split(',')
                            : Array.Empty<string>();
                    }
                }

                allCsItems.Add((csPath, usings));
            }

            // Step 2: Background — read and parse stale files in parallel.
            var staleItems = new List<string>();
            for (int i = 0; i < allCsItems.Count; i++)
            {
                if (allCsItems[i].cachedUsings == null)
                    staleItems.Add(allCsItems[i].csPath);
            }

            ReportProgress(0.40f, $"Parsing {staleItems.Count} stale source files...");

            var parsedResults = new ConcurrentDictionary<string, string[]>(StringComparer.Ordinal);

            if (staleItems.Count > 0)
            {
                await PCPThreading.ParallelForEachAsync(staleItems, (csPath, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    string fullPath = Path.GetFullPath(csPath);
                    if (!File.Exists(fullPath))
                        return Task.CompletedTask;

                    try
                    {
                        string content = File.ReadAllText(fullPath);
                        MatchCollection matches = s_UsingRegex.Matches(content);
                        var usingArr = new string[matches.Count];
                        for (int j = 0; j < matches.Count; j++)
                            usingArr[j] = matches[j].Groups[1].Value;
                        parsedResults[csPath] = usingArr;
                    }
                    catch (Exception)
                    {
                        // Ignore read errors.
                    }

                    return Task.CompletedTask;
                }, PCPThreading.DefaultConcurrency, ct);
            }

            // Step 3: Update cache for freshly parsed files.
            foreach (var kvp in parsedResults)
                context.Cache.SetMetadata(kvp.Key, "packages.usings", string.Join(",", kvp.Value));

            // Step 4: Tally namespace references from all files.
            ReportProgress(0.65f, "Matching namespaces to packages...");

            for (int i = 0; i < allCsItems.Count; i++)
            {
                var (csPath, cachedUsings) = allCsItems[i];
                string[] usings = cachedUsings;
                if (usings == null && !parsedResults.TryGetValue(csPath, out usings))
                    continue;

                foreach (string ns in usings)
                {
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

            ct.ThrowIfCancellationRequested();

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

            ct.ThrowIfCancellationRequested();

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
        // Serialization
        // ----------------------------------------------------------------

        public override void WriteResults(BinaryWriter writer)
        {
            writer.Write(_results.Count);
            for (int i = 0; i < _results.Count; i++)
            {
                var p = _results[i];
                writer.Write(p.packageName ?? string.Empty);
                writer.Write(p.displayName ?? string.Empty);
                writer.Write(p.version ?? string.Empty);
                writer.Write(p.description ?? string.Empty);
                writer.Write((byte)p.status);
                writer.Write(p.directReferenceCount);
                writer.Write(p.codeReferenceCount);
                // dependedOnBy
                int depCount = p.dependedOnBy?.Count ?? 0;
                writer.Write(depCount);
                for (int j = 0; j < depCount; j++)
                    writer.Write(p.dependedOnBy[j] ?? string.Empty);
            }
        }

        public override void ReadResults(BinaryReader reader)
        {
            _results.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var p = new PCPPackageAuditEntry
                {
                    packageName = reader.ReadString(),
                    displayName = reader.ReadString(),
                    version = reader.ReadString(),
                    description = reader.ReadString(),
                    status = (PCPPackageStatus)reader.ReadByte(),
                    directReferenceCount = reader.ReadInt32(),
                    codeReferenceCount = reader.ReadInt32()
                };
                int depCount = reader.ReadInt32();
                p.dependedOnBy = new List<string>(depCount);
                for (int j = 0; j < depCount; j++)
                    p.dependedOnBy.Add(reader.ReadString());
                _results.Add(p);
            }
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
