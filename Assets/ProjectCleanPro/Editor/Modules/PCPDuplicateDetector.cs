using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 3 - Duplicate Detector.
    /// Identifies groups of assets that share identical file content by comparing
    /// file sizes (pre-filter) and SHA-256 hashes.
    /// </summary>
    public sealed class PCPDuplicateDetector : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override string ModuleId => "duplicates";
        public override string DisplayName => "Duplicates";
        public override string Icon => "\u2687"; // ⚇
        public override Color AccentColor => new Color(0.557f, 0.267f, 0.678f, 1f); // #8E44AD

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        private readonly List<PCPDuplicateGroup> _results = new List<PCPDuplicateGroup>();

        /// <summary>Read-only access to the scan results.</summary>
        public IReadOnlyList<PCPDuplicateGroup> Results => _results;

        public override int FindingCount => _results.Count;

        public override long TotalSizeBytes
        {
            get
            {
                long total = 0L;
                for (int i = 0; i < _results.Count; i++)
                    total += _results[i].WastedBytes;
                return total;
            }
        }

        // ----------------------------------------------------------------
        // Extensions to skip (code files, meta files, etc.)
        // ----------------------------------------------------------------

        private static readonly HashSet<string> s_SkippedExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".meta", ".asmdef", ".asmref", ".dll", ".so", ".dylib",
            ".a", ".rsp", ".cginc", ".hlsl", ".glslinc"
        };

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override void DoScan(PCPScanContext context)
        {
            _results.Clear();

            // ----------------------------------------------------------
            // Phase 1: Collect all eligible asset paths with file sizes
            // ----------------------------------------------------------
            ReportProgress(0f, "Collecting asset paths...");

            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            var pathsWithSizes = new List<PathSizePair>();

            for (int i = 0; i < allPaths.Length; i++)
            {
                string path = allPaths[i];

                if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (AssetDatabase.IsValidFolder(path))
                    continue;

                // Skip packages.
                if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    continue;

                string ext = Path.GetExtension(path);
                if (s_SkippedExtensions.Contains(ext))
                    continue;

                if (IsIgnored(path, context))
                    continue;

                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                    continue;

                long size = new FileInfo(fullPath).Length;
                if (size == 0)
                    continue;

                pathsWithSizes.Add(new PathSizePair { path = path, fullPath = fullPath, size = size });
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 2: Group by file size (pre-filter)
            // ----------------------------------------------------------
            ReportProgress(0.1f, "Grouping by file size...");

            var sizeGroups = new Dictionary<long, List<PathSizePair>>();

            for (int i = 0; i < pathsWithSizes.Count; i++)
            {
                var entry = pathsWithSizes[i];
                if (!sizeGroups.TryGetValue(entry.size, out List<PathSizePair> group))
                {
                    group = new List<PathSizePair>();
                    sizeGroups[entry.size] = group;
                }
                group.Add(entry);
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 3: Hash files within same-size groups
            // ----------------------------------------------------------
            // Count how many files need hashing for progress reporting.
            int filesToHash = 0;
            foreach (var kvp in sizeGroups)
            {
                if (kvp.Value.Count > 1)
                    filesToHash += kvp.Value.Count;
            }

            int hashed = 0;
            var hashGroups = new Dictionary<string, List<PathSizePair>>(StringComparer.Ordinal);

            foreach (var kvp in sizeGroups)
            {
                if (ShouldCancel()) return;

                List<PathSizePair> group = kvp.Value;
                if (group.Count < 2)
                    continue;

                for (int i = 0; i < group.Count; i++)
                {
                    if (ShouldCancel()) return;

                    var entry = group[i];
                    hashed++;

                    if ((hashed & 31) == 0)
                    {
                        float pct = 0.2f + 0.6f * ((float)hashed / Math.Max(filesToHash, 1));
                        ReportProgress(pct, $"Hashing file {hashed}/{filesToHash}...");
                    }

                    // Check the cache first.
                    string hash = null;
                    if (context.Cache != null && !context.Cache.IsStale(entry.path))
                    {
                        hash = context.Cache.GetHash(entry.path);
                    }

                    if (string.IsNullOrEmpty(hash))
                    {
                        try
                        {
                            hash = PCPFileUtils.ComputeSHA256(entry.fullPath);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        // Store in cache.
                        if (context.Cache != null && !string.IsNullOrEmpty(hash))
                        {
                            context.Cache.SetHash(entry.path, hash);
                        }
                    }

                    if (string.IsNullOrEmpty(hash))
                        continue;

                    entry.hash = hash;

                    if (!hashGroups.TryGetValue(hash, out List<PathSizePair> hashGroup))
                    {
                        hashGroup = new List<PathSizePair>();
                        hashGroups[hash] = hashGroup;
                    }
                    hashGroup.Add(entry);
                }
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 4: Build duplicate groups from hash matches
            // ----------------------------------------------------------
            ReportProgress(0.85f, "Building duplicate groups...");

            foreach (var kvp in hashGroups)
            {
                if (kvp.Value.Count < 2)
                    continue;

                var dupGroup = new PCPDuplicateGroup
                {
                    hash = kvp.Key
                };

                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    var item = kvp.Value[i];
                    string guid = AssetDatabase.AssetPathToGUID(item.path);

                    // Count references to this asset to help determine the canonical copy.
                    var dependents = context.DependencyResolver.IsBuilt
                        ? context.DependencyResolver.GetDependents(item.path)
                        : null;

                    int refCount = dependents != null ? ((System.Collections.ICollection)dependents).Count : 0;

                    dupGroup.entries.Add(new PCPDuplicateEntry
                    {
                        path = item.path,
                        guid = guid,
                        sizeBytes = item.size,
                        referenceCount = refCount,
                        isCanonical = false
                    });
                }

                dupGroup.ElectCanonical();
                _results.Add(dupGroup);
            }

            // ----------------------------------------------------------
            // Phase 5: Sort by wasted bytes descending
            // ----------------------------------------------------------
            ReportProgress(0.95f, "Sorting results...");
            _results.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));

            ReportProgress(1f, $"Found {_results.Count} duplicate groups.");
        }

        public override void Clear()
        {
            base.Clear();
            _results.Clear();
        }

        // ----------------------------------------------------------------
        // Internal data
        // ----------------------------------------------------------------

        private class PathSizePair
        {
            public string path;
            public string fullPath;
            public long size;
            public string hash;
        }
    }
}
