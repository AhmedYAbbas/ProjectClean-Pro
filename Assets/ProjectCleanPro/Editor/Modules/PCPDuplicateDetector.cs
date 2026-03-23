using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        public override PCPModuleId Id => PCPModuleId.Duplicates;
        public override string DisplayName => "Duplicates";
        public override string Icon => "\u2687"; // ⚇
        public override Color AccentColor => new Color(0.557f, 0.267f, 0.678f, 1f); // #8E44AD

        public override IReadOnlyCollection<string> RelevantExtensions => null;
        public override bool RequiresDependencyGraph => true;

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

        protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
        {
            _results.Clear();

            // ----------------------------------------------------------
            // Phase 1: Collect all eligible asset paths with file sizes
            // ----------------------------------------------------------
            ReportProgress(0f, "Collecting asset paths...");

            string[] allPaths = context.AllProjectAssets;
            var pathsWithSizes = new List<PathSizePair>();

            for (int i = 0; i < allPaths.Length; i++)
            {
                string path = allPaths[i];

                string ext = Path.GetExtension(path);
                if (s_SkippedExtensions.Contains(ext))
                    continue;

                if (IsIgnored(path, context))
                    continue;

                // Skip editor-only paths unless settings opt-in.
                if (!context.Settings.scanEditorAssets && PCPAssetUtils.IsEditorOnlyPath(path))
                    continue;

                // Try cached file size first; fall back to disk.
                long size = context.Cache.GetFileSize(path);
                string fullPath = Path.GetFullPath(path);

                if (size <= 0)
                {
                    if (!File.Exists(fullPath))
                        continue;

                    size = new FileInfo(fullPath).Length;
                    if (size == 0)
                        continue;

                    context.Cache.SetFileSize(path, size);
                }
                else if (!File.Exists(fullPath))
                {
                    continue;
                }

                bool isYaml = PCPFileUtils.IsUnityYamlAsset(fullPath);
                pathsWithSizes.Add(new PathSizePair
                {
                    path = path,
                    fullPath = fullPath,
                    size = size,
                    isUnityYaml = isYaml
                });
            }

            ct.ThrowIfCancellationRequested();

            // ----------------------------------------------------------
            // Phase 2: Group by file size (pre-filter)
            // ----------------------------------------------------------
            // Unity YAML assets embed m_Name in the file, so copies of the
            // same asset have slightly different sizes.  We skip the size
            // pre-filter for them and hash every YAML asset with
            // name-normalised hashing instead.
            // ----------------------------------------------------------
            ReportProgress(0.1f, "Grouping by file size...");

            var sizeGroups = new Dictionary<long, List<PathSizePair>>();
            var yamlAssets = new List<PathSizePair>();

            for (int i = 0; i < pathsWithSizes.Count; i++)
            {
                var entry = pathsWithSizes[i];
                if (entry.isUnityYaml)
                {
                    yamlAssets.Add(entry);
                    continue;
                }

                if (!sizeGroups.TryGetValue(entry.size, out List<PathSizePair> group))
                {
                    group = new List<PathSizePair>();
                    sizeGroups[entry.size] = group;
                }
                group.Add(entry);
            }

            ct.ThrowIfCancellationRequested();

            // ----------------------------------------------------------
            // Phase 3: Hash files within same-size groups + all YAML assets
            // ----------------------------------------------------------
            // Count how many files need hashing for progress reporting.
            int filesToHash = yamlAssets.Count;
            foreach (var kvp in sizeGroups)
            {
                if (kvp.Value.Count > 1)
                    filesToHash += kvp.Value.Count;
            }

            int hashed = 0;
            var hashGroups = new Dictionary<string, List<PathSizePair>>(StringComparer.Ordinal);

            // Helper: hash a single entry and add it to hashGroups.
            void HashEntry(PathSizePair entry, bool useNormalized)
            {
                hashed++;

                if ((hashed & 31) == 0)
                {
                    float pct = 0.2f + 0.6f * ((float)hashed / Math.Max(filesToHash, 1));
                    ReportProgress(pct, $"Hashing file {hashed}/{filesToHash}...");
                }

                // Check the cache first.
                string hash = null;
                if (useNormalized && !context.Cache.IsStale(entry.path))
                {
                    hash = context.Cache.GetMetadata(entry.path, "dup.normalizedHash");
                }
                else if (!useNormalized && !context.Cache.IsStale(entry.path))
                {
                    hash = context.Cache.GetHash(entry.path);
                }

                if (string.IsNullOrEmpty(hash))
                {
                    try
                    {
                        hash = useNormalized
                            ? PCPFileUtils.ComputeNormalizedSHA256(entry.fullPath)
                            : PCPFileUtils.ComputeSHA256(entry.fullPath);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    // Store in cache.
                    if (!string.IsNullOrEmpty(hash))
                    {
                        if (useNormalized)
                            context.Cache.SetMetadata(entry.path, "dup.normalizedHash", hash);
                        else
                            context.Cache.SetHash(entry.path, hash);
                    }
                }

                if (string.IsNullOrEmpty(hash))
                    return;

                entry.hash = hash;

                if (!hashGroups.TryGetValue(hash, out List<PathSizePair> hashGroup))
                {
                    hashGroup = new List<PathSizePair>();
                    hashGroups[hash] = hashGroup;
                }
                hashGroup.Add(entry);
            }

            // 3a: Hash all Unity YAML assets (no size pre-filter).
            for (int i = 0; i < yamlAssets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                HashEntry(yamlAssets[i], useNormalized: true);
            }

            // 3b: Hash non-YAML assets within same-size groups.
            foreach (var kvp in sizeGroups)
            {
                ct.ThrowIfCancellationRequested();

                List<PathSizePair> group = kvp.Value;
                if (group.Count < 2)
                    continue;

                for (int i = 0; i < group.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    HashEntry(group[i], useNormalized: false);
                }
            }

            ct.ThrowIfCancellationRequested();

            // ----------------------------------------------------------
            // Phase 4: Build duplicate groups from hash matches
            // ----------------------------------------------------------
            ReportProgress(0.85f, "Building duplicate groups...");

            bool compareImportSettings = context.Settings.duplicateCompareImportSettings;

            foreach (var kvp in hashGroups)
            {
                if (kvp.Value.Count < 2)
                    continue;

                // When importer-settings comparison is enabled, further
                // subdivide the hash group so that assets with identical
                // content but different import settings are NOT treated
                // as duplicates.
                IEnumerable<List<PathSizePair>> subGroups;
                if (compareImportSettings)
                    subGroups = SubdivideByImporterSettings(kvp.Value);
                else
                    subGroups = new List<List<PathSizePair>> { kvp.Value };

                foreach (var subGroup in subGroups)
                {
                    if (subGroup.Count < 2)
                        continue;

                    var dupGroup = new PCPDuplicateGroup
                    {
                        hash = kvp.Key
                    };

                    for (int i = 0; i < subGroup.Count; i++)
                    {
                        var item = subGroup[i];
                        string guid = AssetDatabase.AssetPathToGUID(item.path);

                        // Count references to this asset to help determine the canonical copy.
                        var dependents = context.DependencyResolver.IsBuilt
                            ? context.DependencyResolver.GetDependents(item.path)
                            : null;

                        int refCount = dependents != null ? dependents.Count : 0;

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
        // Binary serialization
        // ----------------------------------------------------------------

        public override void WriteResults(BinaryWriter writer)
        {
            writer.Write(_results.Count);
            for (int i = 0; i < _results.Count; i++)
            {
                var g = _results[i];
                writer.Write(g.hash ?? string.Empty);
                writer.Write(g.entries.Count);
                for (int j = 0; j < g.entries.Count; j++)
                {
                    var e = g.entries[j];
                    writer.Write(e.path ?? string.Empty);
                    writer.Write(e.guid ?? string.Empty);
                    writer.Write(e.sizeBytes);
                    writer.Write(e.referenceCount);
                    writer.Write(e.isCanonical);
                }
            }
        }

        public override void ReadResults(BinaryReader reader)
        {
            _results.Clear();
            int groupCount = reader.ReadInt32();
            for (int i = 0; i < groupCount; i++)
            {
                var g = new PCPDuplicateGroup { hash = reader.ReadString() };
                int entryCount = reader.ReadInt32();
                for (int j = 0; j < entryCount; j++)
                {
                    g.entries.Add(new PCPDuplicateEntry
                    {
                        path = reader.ReadString(),
                        guid = reader.ReadString(),
                        sizeBytes = reader.ReadInt64(),
                        referenceCount = reader.ReadInt32(),
                        isCanonical = reader.ReadBoolean()
                    });
                }
                _results.Add(g);
            }
        }

        // ----------------------------------------------------------------
        // Importer-settings comparison
        // ----------------------------------------------------------------

        /// <summary>
        /// Splits a list of content-identical assets into sub-groups where
        /// each sub-group also shares identical importer settings.
        /// Assets with no importer are grouped together.
        /// </summary>
        private static IEnumerable<List<PathSizePair>> SubdivideByImporterSettings(
            List<PathSizePair> group)
        {
            var subGroups = new Dictionary<string, List<PathSizePair>>(StringComparer.Ordinal);

            for (int i = 0; i < group.Count; i++)
            {
                string key = GetImporterSettingsKey(group[i].path);

                if (!subGroups.TryGetValue(key, out List<PathSizePair> list))
                {
                    list = new List<PathSizePair>();
                    subGroups[key] = list;
                }
                list.Add(group[i]);
            }

            return subGroups.Values;
        }

        /// <summary>
        /// Returns a string key representing the importer settings for a given
        /// asset path. Assets with identical keys have identical import settings.
        /// </summary>
        private static string GetImporterSettingsKey(string assetPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                return string.Empty;

            // EditorJsonUtility serializes all SerializedObject fields,
            // giving us a complete, comparable snapshot of the importer state.
            return EditorJsonUtility.ToJson(importer);
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
            public bool isUnityYaml;
        }
    }
}
