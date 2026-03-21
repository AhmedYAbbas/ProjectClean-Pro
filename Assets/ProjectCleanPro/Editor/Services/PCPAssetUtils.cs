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
        /// Filters out directories and Packages/ paths.
        /// </summary>
        public static string[] GetAllProjectAssets()
        {
            return AssetDatabase.GetAllAssetPaths()
                .Where(p => IsValidAssetPath(p))
                .ToArray();
        }

        /// <summary>
        /// Returns paths to all .unity scene files in the project.
        /// </summary>
        public static string[] GetAllScenePaths()
        {
            return AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                            && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        /// <summary>
        /// Returns paths to all enabled scenes in the Build Settings.
        /// </summary>
        public static string[] GetBuildScenePaths()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
        }

        /// <summary>
        /// Returns all asset paths located under any <c>Resources/</c> folder in the project.
        /// This includes both top-level <c>Assets/Resources/</c> and nested
        /// <c>Assets/.../Resources/</c> directories.
        /// </summary>
        public static string[] GetResourcesPaths()
        {
            return AssetDatabase.GetAllAssetPaths()
                .Where(p => !string.IsNullOrEmpty(p)
                            && p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                            && !AssetDatabase.IsValidFolder(p)
                            && IsResourcesPath(p))
                .ToArray();
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
        /// non-empty, starts with "Assets/", and is not a folder.
        /// </summary>
        public static bool IsValidAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !AssetDatabase.IsValidFolder(path);
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
