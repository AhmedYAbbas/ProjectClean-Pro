using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 3 - Duplicate Detector.
    /// Identifies groups of assets that share identical file content by comparing
    /// file content hashes using a three-phase async pattern:
    /// Phase 1 (GATHER): Hash files on background threads (parallel I/O).
    /// Phase 2 (QUERY): Compare import settings on main thread (frame-budgeted).
    /// Phase 3 (ANALYZE): Build result objects.
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
        // Scan implementation — three-phase async pattern
        // ----------------------------------------------------------------

        protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
        {
            _results.Clear();

            var cache = context.Cache;
            var settings = context.Settings;

            // Collect candidates (filter excluded extensions, ignored paths)
            var allAssets = await context.GetAllProjectAssetsAsync(ct);
            var candidates = allAssets
                .Where(p => !IsExcludedExtension(p, settings) && !IsIgnored(p, context))
                .ToList();

            Interlocked.Exchange(ref m_TotalCount, candidates.Count);

            // === PHASE 1: GATHER — Hash on background threads ===
            var hashMap = new ConcurrentDictionary<string, string>();

            await PCPThreading.ParallelForEachAsync(candidates, async (path, token) =>
            {
                try
                {
                    // Check cache first
                    if (!cache.IsStale(path))
                    {
                        var cachedHash = cache.GetHash(path);
                        if (cachedHash != null)
                        {
                            hashMap[path] = cachedHash;
                            Interlocked.Increment(ref m_ProcessedCount);
                            return;
                        }
                    }

                    // Cache miss — hash on background thread
                    if (!System.IO.File.Exists(path))
                    {
                        Interlocked.Increment(ref m_ProcessedCount);
                        return;
                    }

                    var bytes = await System.IO.File.ReadAllBytesAsync(path, token);
                    string hash;

                    if (PCPGuidParser.IsGuidParseable(path))
                    {
                        // Normalize YAML: compute hash from metadata key in cache if available
                        var normalizedHash = cache.GetMetadata(path, "dup.normalizedHash");
                        if (normalizedHash != null && !cache.IsStale(path))
                        {
                            hash = normalizedHash;
                        }
                        else
                        {
                            hash = ComputeNormalizedYamlHash(bytes);
                            cache.SetMetadata(path, "dup.normalizedHash", hash);
                        }
                    }
                    else
                    {
                        hash = ComputeSHA256(bytes);
                    }

                    hashMap[path] = hash;
                    cache.SetHash(path, hash);
                }
                catch (OperationCanceledException) { throw; }
                catch (System.Exception ex)
                {
                    m_Warnings.Enqueue($"Skipped {path}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Increment(ref m_ProcessedCount);
                }
            }, PCPThreading.DefaultConcurrency, ct);

            // Group by hash — only groups with 2+ entries are duplicates
            var groups = hashMap
                .GroupBy(kv => kv.Value)
                .Where(g => g.Count() > 1)
                .ToList();

            // === PHASE 2: QUERY — Import settings comparison (main thread, budgeted) ===
            if (settings.duplicateCompareImportSettings && context.Scheduler != null)
            {
                groups = await RefineByImportSettingsAsync(groups, context, ct);
            }

            // === PHASE 3: ANALYZE — Build results (background) ===
            var resolver = context.DependencyResolver;

            _results.Clear();
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                var dupGroup = BuildDuplicateGroup(group, resolver);
                if (dupGroup != null)
                    _results.Add(dupGroup);
            }

            _results.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));
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
        // Phase 2: Import settings refinement
        // ----------------------------------------------------------------

        private async Task<List<IGrouping<string, KeyValuePair<string, string>>>> RefineByImportSettingsAsync(
            List<IGrouping<string, KeyValuePair<string, string>>> groups,
            PCPScanContext context,
            CancellationToken ct)
        {
            var refined = new List<IGrouping<string, KeyValuePair<string, string>>>();

            foreach (var group in groups)
            {
                var paths = group.Select(kv => kv.Key).ToList();
                if (paths.Count < 2) continue;

                var importSettings = await context.Scheduler.BatchOnMainThread(
                    paths,
                    path =>
                    {
                        var importer = AssetImporter.GetAtPath(path);
                        return importer != null ? EditorJsonUtility.ToJson(importer) : "";
                    },
                    ct);

                var subGroups = paths
                    .Select((p, i) => new { Path = p, Hash = group.Key, Settings = importSettings[i] })
                    .GroupBy(x => x.Hash + "|" + x.Settings)
                    .Where(g => g.Count() > 1);

                foreach (var sub in subGroups)
                {
                    refined.Add(sub.Select(x =>
                        new KeyValuePair<string, string>(x.Path, group.Key))
                        .GroupBy(kv => kv.Value).First());
                }
            }

            return refined;
        }

        // ----------------------------------------------------------------
        // Phase 3: Build duplicate group from hash grouping
        // ----------------------------------------------------------------

        private PCPDuplicateGroup BuildDuplicateGroup(
            IGrouping<string, KeyValuePair<string, string>> group,
            IPCPDependencyResolver resolver)
        {
            var paths = group.Select(kv => kv.Key).ToList();
            if (paths.Count < 2)
                return null;

            var dupGroup = new PCPDuplicateGroup { hash = group.Key };

            foreach (var path in paths)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);

                int refCount = 0;
                if (resolver != null && resolver.IsBuilt)
                {
                    refCount = resolver.GetDependentCount(path);
                }

                long sizeBytes = 0;
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    try { sizeBytes = new FileInfo(fullPath).Length; }
                    catch { /* ignore — entry will have 0 size */ }
                }

                dupGroup.entries.Add(new PCPDuplicateEntry
                {
                    path = path,
                    guid = guid,
                    sizeBytes = sizeBytes,
                    referenceCount = refCount,
                    isCanonical = false
                });
            }

            if (dupGroup.entries.Count < 2)
                return null;

            dupGroup.ElectCanonical();
            return dupGroup;
        }

        // ----------------------------------------------------------------
        // Helpers: extension filter
        // ----------------------------------------------------------------

        private static bool IsExcludedExtension(string path, PCPSettings settings)
        {
            if (settings?.excludedExtensions == null || settings.excludedExtensions.Count == 0)
                return false;

            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return false;

            foreach (var excluded in settings.excludedExtensions)
            {
                if (string.Equals(ext, excluded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ----------------------------------------------------------------
        // Helpers: hashing
        // ----------------------------------------------------------------

        private static string ComputeSHA256(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha.ComputeHash(data);
            var sb = new System.Text.StringBuilder(hashBytes.Length * 2);
            foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string ComputeNormalizedYamlHash(byte[] data)
        {
            // Strip GUID-based lines that differ between copies of the same asset
            var text = System.Text.Encoding.UTF8.GetString(data);
            var lines = text.Split('\n');
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                // Skip guid references — they differ between duplicates of the same content
                if (trimmed.StartsWith("guid:") || trimmed.StartsWith("m_Script:") ||
                    trimmed.StartsWith("fileID:"))
                    continue;
                sb.AppendLine(line);
            }
            return ComputeSHA256(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        }
    }
}
