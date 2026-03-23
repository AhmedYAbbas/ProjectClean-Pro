using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Static helper methods for querying the <see cref="AssetDatabase"/> and
    /// performing common asset-level operations throughout ProjectCleanPro.
    /// </summary>
    public static class PCPAssetUtils
    {
        // ----------------------------------------------------------------
        // Asset enumeration
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns all project asset paths under "Assets/" excluding folders.
        /// Uses <c>Path.GetExtension</c> instead of <c>AssetDatabase.IsValidFolder</c>
        /// to avoid an expensive Unity API call per path.
        /// </summary>
        public static string[] GetAllProjectAssets()
        {
            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            var result = new List<string>(allPaths.Length / 2);

            for (int i = 0; i < allPaths.Length; i++)
            {
                string p = allPaths[i];
                if (IsValidAssetPath(p))
                    result.Add(p);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Returns paths to all .unity scene files from an existing asset list.
        /// Falls back to scanning if <paramref name="allProjectAssets"/> is null.
        /// </summary>
        public static string[] GetAllScenePaths(string[] allProjectAssets = null)
        {
            if (allProjectAssets != null)
            {
                var scenes = new List<string>();
                for (int i = 0; i < allProjectAssets.Length; i++)
                {
                    if (allProjectAssets[i].EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                        scenes.Add(allProjectAssets[i]);
                }
                return scenes.ToArray();
            }

            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            var result = new List<string>();
            for (int i = 0; i < allPaths.Length; i++)
            {
                string p = allPaths[i];
                if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    result.Add(p);
            }
            return result.ToArray();
        }

        /// <summary>
        /// Returns paths to all enabled scenes in the Build Settings.
        /// </summary>
        public static string[] GetBuildScenePaths()
        {
            var scenes = EditorBuildSettings.scenes;
            var result = new List<string>();
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled && !string.IsNullOrEmpty(scenes[i].path))
                    result.Add(scenes[i].path);
            }
            return result.ToArray();
        }

        /// <summary>
        /// Returns all asset paths located under any <c>Resources/</c> folder.
        /// Derives from <paramref name="allProjectAssets"/> when provided.
        /// </summary>
        public static string[] GetResourcesPaths(string[] allProjectAssets = null)
        {
            if (allProjectAssets != null)
            {
                var result = new List<string>();
                for (int i = 0; i < allProjectAssets.Length; i++)
                {
                    if (IsResourcesPath(allProjectAssets[i]))
                        result.Add(allProjectAssets[i]);
                }
                return result.ToArray();
            }

            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            var res = new List<string>();
            for (int i = 0; i < allPaths.Length; i++)
            {
                string p = allPaths[i];
                if (!string.IsNullOrEmpty(p)
                    && p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(Path.GetExtension(p))
                    && IsResourcesPath(p))
                    res.Add(p);
            }
            return res.ToArray();
        }

        /// <summary>
        /// Returns all asset paths that are assigned to an AssetBundle.
        /// </summary>
        public static string[] GetAssetBundleRoots()
        {
            var result = new List<string>();
            string[] allBundles = AssetDatabase.GetAllAssetBundleNames();

            foreach (string bundleName in allBundles)
            {
                string[] paths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                if (paths != null)
                {
                    result.AddRange(paths);
                }
            }

            return result.Distinct().ToArray();
        }

        // ----------------------------------------------------------------
        // Path validation
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns <c>true</c> if the path is a valid project asset path:
        /// non-empty, starts with "Assets/", and has a file extension
        /// (folders never have extensions in the AssetDatabase).
        /// Uses <c>Path.GetExtension</c> instead of the expensive
        /// <c>AssetDatabase.IsValidFolder</c> Unity API call.
        /// </summary>
        public static bool IsValidAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(Path.GetExtension(path));
        }

        /// <summary>
        /// Returns <c>true</c> if the path ends with ".unity" (a scene file).
        /// </summary>
        public static bool IsScenePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            return path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
        }

        // ----------------------------------------------------------------
        // Path classification
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns <c>true</c> if the asset path resides under an <c>Editor/</c> folder.
        /// Handles both top-level <c>Assets/Editor/</c> and nested <c>Assets/.../Editor/</c>.
        /// </summary>
        public static bool IsEditorOnlyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Check for /Editor/ anywhere in the path, or the path being exactly in Assets/Editor/.
            // We normalize to forward slashes for consistency.
            string normalized = path.Replace('\\', '/');

            if (normalized.StartsWith("Assets/Editor/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.Contains("/Editor/"))
                return true;

            // Also check if the last segment before the filename is "Editor".
            string directory = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(directory))
            {
                string dirNormalized = directory.Replace('\\', '/');
                if (dirNormalized.EndsWith("/Editor", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dirNormalized, "Assets/Editor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns <c>true</c> if the asset path is inside a Resources/ folder.
        /// </summary>
        public static bool IsResourcesPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Resources/") ||
                   normalized.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase);
        }

        // ----------------------------------------------------------------
        // Asset queries
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the main asset type at the given path, or <c>null</c> if unknown.
        /// Wrapper around <see cref="AssetDatabase.GetMainAssetTypeAtPath"/>.
        /// </summary>
        public static Type GetAssetType(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            return AssetDatabase.GetMainAssetTypeAtPath(path);
        }

        // ----------------------------------------------------------------
        // Path conversion
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the absolute path to the Unity project root directory
        /// (the parent of <c>Application.dataPath</c>).
        /// </summary>
        public static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        /// <summary>
        /// Converts a project-relative asset path (e.g. "Assets/Textures/Hero.png") to an
        /// absolute file system path.
        /// </summary>
        public static string GetFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return string.Empty;

            return Path.Combine(GetProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        // ----------------------------------------------------------------
        // Formatting
        // ----------------------------------------------------------------

        /// <summary>
        /// Formats a byte count into a human-readable string (B, KB, MB, GB).
        /// </summary>
        /// <param name="bytes">Size in bytes.</param>
        /// <returns>Formatted string such as "1.5 MB".</returns>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0)
                bytes = 0;

            if (bytes < 1024L)
                return $"{bytes} B";
            if (bytes < 1024L * 1024L)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024L * 1024L)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}
