using System;
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
    /// Module 1 - Unused Asset Scanner.
    /// Identifies assets that are not reachable from any build scene,
    /// Resources folder, AssetBundle, or always-used root.
    /// Uses a three-phase async pattern:
    /// Phase 1 (GATHER): Collect all asset paths via background I/O.
    /// Phase 2 (QUERY): No main-thread queries needed — reachability is pre-computed by the dependency resolver.
    /// Phase 3 (ANALYZE): Cross-reference candidates against the reachable set on a background thread.
    /// </summary>
    public sealed class PCPUnusedScanner : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override PCPModuleId Id => PCPModuleId.Unused;
        public override string DisplayName => "Unused Assets";
        public override string Icon => "\u2718"; // ✘
        public override Color AccentColor => new Color(0.753f, 0.224f, 0.169f, 1f); // #C0392B
        public override IReadOnlyCollection<string> RelevantExtensions => null;
        public override bool RequiresDependencyGraph => true;

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        private readonly List<PCPUnusedAsset> _results = new List<PCPUnusedAsset>();

        /// <summary>Read-only access to the scan results.</summary>
        public IReadOnlyList<PCPUnusedAsset> Results => _results;

        public override int FindingCount => _results.Count;

        public override long TotalSizeBytes
        {
            get
            {
                long total = 0L;
                for (int i = 0; i < _results.Count; i++)
                    total += _results[i].SizeBytes;
                return total;
            }
        }

        // ----------------------------------------------------------------
        // Scan implementation — three-phase async pattern
        // ----------------------------------------------------------------

        protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
        {
            _results.Clear();

            // Get the reachable set from the dependency resolver.
            var resolver = context.DependencyResolver;
            var reachable = resolver != null ? resolver.GetReachableAssets() : Array.Empty<string>();
            var reachableSet = new HashSet<string>(reachable, StringComparer.Ordinal);

            // === PHASE 1: GATHER — Collect and filter asset paths (main thread) ===
            ReportProgress(0.05f, "Gathering asset paths...");
            var allAssets = await context.GetAllProjectAssetsAsync(ct);
            var candidates = allAssets
                .Where(p => !IsExcludedExtension(p, context.Settings)
                            && !IsEditorOnlyPath(p, context.Settings)
                            && !IsIgnored(p, context))
                .ToList();

            Interlocked.Exchange(ref m_TotalCount, candidates.Count);

            // === PHASE 2: QUERY — No main-thread queries needed ===
            // Reachability is already computed by the dependency resolver.

            // === PHASE 3: ANALYZE — Cross-reference against reachable set (background) ===
            ReportProgress(0.15f, "Identifying unused assets...");

            var localResults = new List<PCPUnusedAsset>();

            await PCPThreading.RunOnBackground(() =>
            {
                foreach (var path in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref m_ProcessedCount);

                    if (reachableSet.Contains(path))
                        continue;

                    var asset = BuildUnusedAsset(path);
                    if (asset != null)
                        localResults.Add(asset);
                }

                localResults.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
                return Task.CompletedTask;
            }, ct);

            _results.AddRange(localResults);

            ReportProgress(1f, $"Found {_results.Count} unused assets.");
        }

        public override void Clear()
        {
            base.Clear();
            _results.Clear();
        }

        // ----------------------------------------------------------------
        // Helpers: extension + path filters
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

        private static bool IsEditorOnlyPath(string path, PCPSettings settings)
        {
            if (settings != null && settings.scanEditorAssets)
                return false;
            return PCPAssetUtils.IsEditorOnlyPath(path);
        }

        // ----------------------------------------------------------------
        // Helpers: result construction (background-safe, filesystem only)
        // ----------------------------------------------------------------

        private static PCPUnusedAsset BuildUnusedAsset(string path)
        {
            var info = new PCPAssetInfo
            {
                path = path,
                name = Path.GetFileNameWithoutExtension(path),
                extension = Path.GetExtension(path),
                assetTypeName = "Unknown"
            };

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    var fi = new FileInfo(fullPath);
                    info.sizeBytes = fi.Length;
                    info.lastModifiedTicks = fi.LastWriteTimeUtc.Ticks;
                }
            }
            catch
            {
                // Leave sizeBytes = 0 if file stat fails.
            }

            bool isInResources = PCPAssetUtils.IsResourcesPath(path);
            bool isInPackage = path.StartsWith("Packages/");

            string suggestedAction;
            if (isInResources)
                suggestedAction = "In Resources folder - verify not loaded at runtime";
            else if (isInPackage)
                suggestedAction = "Package asset - consider removing the package";
            else
                suggestedAction = "Safe to delete";

            return new PCPUnusedAsset
            {
                assetInfo = info,
                isInResources = isInResources,
                isInPackage = isInPackage,
                suggestedAction = suggestedAction
            };
        }

        // ----------------------------------------------------------------
        // Binary persistence
        // ----------------------------------------------------------------

        public override void WriteResults(BinaryWriter writer)
        {
            writer.Write(_results.Count);
            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                writer.Write(r.assetInfo?.path ?? string.Empty);
                writer.Write(r.assetInfo?.guid ?? string.Empty);
                writer.Write(r.assetInfo?.name ?? string.Empty);
                writer.Write(r.assetInfo?.extension ?? string.Empty);
                writer.Write(r.assetInfo?.assetTypeName ?? string.Empty);
                writer.Write(r.assetInfo?.sizeBytes ?? 0L);
                writer.Write(r.isInResources);
                writer.Write(r.isInPackage);
                writer.Write(r.suggestedAction ?? string.Empty);
            }
        }

        public override void ReadResults(BinaryReader reader)
        {
            _results.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var info = new PCPAssetInfo
                {
                    path = reader.ReadString(),
                    guid = reader.ReadString(),
                    name = reader.ReadString(),
                    extension = reader.ReadString(),
                    assetTypeName = reader.ReadString(),
                    sizeBytes = reader.ReadInt64()
                };
                _results.Add(new PCPUnusedAsset
                {
                    assetInfo = info,
                    isInResources = reader.ReadBoolean(),
                    isInPackage = reader.ReadBoolean(),
                    suggestedAction = reader.ReadString()
                });
            }
        }

    }
}
