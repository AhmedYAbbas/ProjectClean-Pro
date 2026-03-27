using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 1 - Unused Asset Scanner.
    /// Identifies assets that are not reachable from any build scene,
    /// Resources folder, AssetBundle, or always-used root.
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
        // Extensions and paths to skip
        // ----------------------------------------------------------------

        private static HashSet<string> BuildSkippedExtensions(PCPSettings settings)
        {
            return new HashSet<string>(settings.excludedExtensions, StringComparer.OrdinalIgnoreCase);
        }

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
        {
            _results.Clear();

            // ----------------------------------------------------------
            // Phase 1: Collect root assets (scenes, Resources, bundles)
            // ----------------------------------------------------------
            ReportProgress(0f, "Collecting root assets...");
            var roots = new HashSet<string>(StringComparer.Ordinal);

            // 1a. Scenes — either all project scenes or just enabled build scenes.
            if (context.Settings.includeAllScenes)
            {
                string[] allScenes = PCPAssetUtils.GetAllScenePaths(context.AllProjectAssets);
                for (int i = 0; i < allScenes.Length; i++)
                    roots.Add(allScenes[i]);
            }
            else
            {
                string[] buildScenePaths = PCPAssetUtils.GetBuildScenePaths();
                for (int i = 0; i < buildScenePaths.Length; i++)
                    roots.Add(buildScenePaths[i]);
            }

            // 1b. All assets under any Resources/ folder.
            string[] resourcesPaths = PCPAssetUtils.GetResourcesPaths(context.AllProjectAssets);
            for (int i = 0; i < resourcesPaths.Length; i++)
                roots.Add(resourcesPaths[i]);

            // 1c. Assets assigned to AssetBundles.
            if (context.Settings.includeAssetBundles)
            {
                string[] bundleRoots = PCPAssetUtils.GetAssetBundleRoots();
                for (int i = 0; i < bundleRoots.Length; i++)
                    roots.Add(bundleRoots[i]);
            }

            // 1d. Addressable entries.
            if (context.Settings.includeAddressables && PCPAddressablesBridge.HasAddressables)
            {
                var addressableRoots = PCPAddressablesBridge.GetRoots();
                for (int i = 0; i < addressableRoots.Count; i++)
                    roots.Add(addressableRoots[i]);
            }

            // 1e. Custom scan roots from context.
            if (context.AlwaysUsedRoots != null)
            {
                for (int i = 0; i < context.AlwaysUsedRoots.Count; i++)
                {
                    string cr = context.AlwaysUsedRoots[i];
                    if (string.IsNullOrEmpty(cr))
                        continue;

                    // If the custom root is a folder, expand to all assets under it.
                    if (AssetDatabase.IsValidFolder(cr))
                    {
                        string[] guids = AssetDatabase.FindAssets("", new[] { cr });
                        for (int j = 0; j < guids.Length; j++)
                        {
                            string assetPath = AssetDatabase.GUIDToAssetPath(guids[j]);
                            if (!string.IsNullOrEmpty(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
                                roots.Add(assetPath);
                        }
                    }
                    else
                    {
                        roots.Add(cr);
                    }
                }
            }

            // 1f. Always-included shaders from GraphicsSettings.
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings != null)
            {
                var so = new SerializedObject(graphicsSettings);
                var alwaysIncluded = so.FindProperty("m_AlwaysIncludedShaders");
                if (alwaysIncluded != null && alwaysIncluded.isArray)
                {
                    for (int i = 0; i < alwaysIncluded.arraySize; i++)
                    {
                        var elem = alwaysIncluded.GetArrayElementAtIndex(i);
                        if (elem.objectReferenceValue != null)
                        {
                            string shaderPath = AssetDatabase.GetAssetPath(elem.objectReferenceValue);
                            if (!string.IsNullOrEmpty(shaderPath))
                                roots.Add(shaderPath);
                        }
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            // ----------------------------------------------------------
            // Phase 2: Build / use the dependency graph to find reachable set
            // ----------------------------------------------------------
            ReportProgress(0.1f, "Building dependency graph...");
            var resolver = context.DependencyResolver;

            // Build or incrementally update the dependency graph.
            await resolver.BuildAsync(roots, (p, label) =>
            {
                ReportProgress(0.1f + p * 0.5f, label);
            }, context.Cache, context.AllProjectAssets, ct: ct);

            ct.ThrowIfCancellationRequested();

            var reachable = new HashSet<string>(resolver.GetAllReachable(), StringComparer.Ordinal);
            // Also mark the roots themselves as reachable.
            foreach (string root in roots)
                reachable.Add(root);

            // ----------------------------------------------------------
            // Phase 3: Enumerate all project assets and subtract reachable
            // ----------------------------------------------------------
            ReportProgress(0.65f, "Identifying unused assets...");

            // Use cached asset list from the scan context.
            string[] projectAssets = context.AllProjectAssets;
            var skippedExtensions = BuildSkippedExtensions(context.Settings);

            int total = projectAssets.Length;

            for (int i = 0; i < total; i++)
            {
                await YieldIfNeeded(i, total, $"Checking asset {i}/{total}...", ct, interval: 128);

                string path = projectAssets[i];

                // Skip excluded extensions (configured in settings).
                string ext = System.IO.Path.GetExtension(path);
                if (skippedExtensions.Contains(ext))
                    continue;

                // Skip editor-only paths (unless settings opt-in).
                if (!context.Settings.scanEditorAssets && PCPAssetUtils.IsEditorOnlyPath(path))
                    continue;

                // Skip ignored paths.
                if (IsIgnored(path, context))
                    continue;

                // If reachable, it is used.
                if (reachable.Contains(path))
                    continue;

                // This asset is unused.
                var entry = PCPUnusedAsset.FromPath(path);
                _results.Add(entry);
            }

            // ----------------------------------------------------------
            // Phase 4: Sort results by size descending
            // ----------------------------------------------------------
            ReportProgress(0.95f, "Sorting results...");
            _results.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

            ReportProgress(1f, $"Found {_results.Count} unused assets.");
        }

        public override void Clear()
        {
            base.Clear();
            _results.Clear();
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
