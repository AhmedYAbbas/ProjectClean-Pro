using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 1 - Unused Asset Scanner.
    /// Identifies assets that are not reachable from any build scene,
    /// Resources folder, AssetBundle, or custom scan root.
    /// </summary>
    public sealed class PCPUnusedScanner : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override string ModuleId => "unused";
        public override string DisplayName => "Unused Assets";
        public override string Icon => "\u2718"; // ✘
        public override Color AccentColor => new Color(0.753f, 0.224f, 0.169f, 1f); // #C0392B

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

        private static readonly HashSet<string> s_SkippedExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".dll", ".so", ".dylib",
            ".a", ".rsp", ".cginc", ".hlsl", ".glslinc"
        };

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override void DoScan(PCPScanContext context)
        {
            _results.Clear();

            // ----------------------------------------------------------
            // Phase 1: Collect root assets (scenes, Resources, bundles)
            // ----------------------------------------------------------
            ReportProgress(0f, "Collecting root assets...");
            var roots = new HashSet<string>(StringComparer.Ordinal);

            // 1a. Enabled build scenes.
            var buildScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                if (buildScenes[i].enabled && !string.IsNullOrEmpty(buildScenes[i].path))
                    roots.Add(buildScenes[i].path);
            }

            // 1b. All assets under any Resources/ folder.
            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < allPaths.Length; i++)
            {
                string p = allPaths[i];
                if (IsInsideResourcesFolder(p))
                    roots.Add(p);
            }

            // 1c. Assets assigned to AssetBundles.
            string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();
            for (int i = 0; i < bundleNames.Length; i++)
            {
                string[] bundleAssets = AssetDatabase.GetAssetPathsFromAssetBundle(bundleNames[i]);
                for (int j = 0; j < bundleAssets.Length; j++)
                    roots.Add(bundleAssets[j]);
            }

            // 1d. Custom scan roots from context.
            if (context.CustomScanRoots != null)
            {
                for (int i = 0; i < context.CustomScanRoots.Count; i++)
                {
                    string cr = context.CustomScanRoots[i];
                    if (!string.IsNullOrEmpty(cr))
                        roots.Add(cr);
                }
            }

            // 1e. Always-included shaders from GraphicsSettings.
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

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 2: Build / use the dependency graph to find reachable set
            // ----------------------------------------------------------
            ReportProgress(0.1f, "Building dependency graph...");
            var resolver = context.DependencyResolver;

            if (!resolver.IsBuilt)
            {
                resolver.Build(roots, (p, label) =>
                {
                    ReportProgress(0.1f + p * 0.5f, label);
                });
            }

            if (ShouldCancel()) return;

            var reachable = new HashSet<string>(resolver.GetAllReachable(), StringComparer.Ordinal);
            // Also mark the roots themselves as reachable.
            foreach (string root in roots)
                reachable.Add(root);

            // ----------------------------------------------------------
            // Phase 3: Enumerate all project assets and subtract reachable
            // ----------------------------------------------------------
            ReportProgress(0.65f, "Identifying unused assets...");

            // Re-fetch in case the DB changed; filter to Assets/ prefix only.
            string[] projectAssets = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            int total = projectAssets.Length;
            int processed = 0;

            for (int i = 0; i < total; i++)
            {
                if (ShouldCancel()) return;

                string path = projectAssets[i];

                // Report every 128 assets.
                if ((i & 127) == 0)
                {
                    float pct = 0.65f + 0.3f * ((float)i / total);
                    ReportProgress(pct, $"Checking asset {i}/{total}...");
                }

                // Skip folders.
                if (AssetDatabase.IsValidFolder(path))
                    continue;

                // Skip scripts, assembly definitions, DLLs.
                string ext = System.IO.Path.GetExtension(path);
                if (s_SkippedExtensions.Contains(ext))
                    continue;

                // Skip editor-only paths (unless settings opt-in).
                if (IsEditorOnlyPath(path))
                    continue;

                // Skip Packages/.
                if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
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
                processed++;
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
        // Helpers
        // ----------------------------------------------------------------

        private static bool IsInsideResourcesFolder(string path)
        {
            // Match both "Assets/Resources/..." and "Assets/.../Resources/..."
            return path.Contains("/Resources/") ||
                   path.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEditorOnlyPath(string path)
        {
            // A path segment named "Editor" at any level makes it editor-only.
            // We check for "/Editor/" or paths that start/end with it.
            return path.Contains("/Editor/") ||
                   path.StartsWith("Assets/Editor/", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/Editor", StringComparison.OrdinalIgnoreCase);
        }
    }
}
