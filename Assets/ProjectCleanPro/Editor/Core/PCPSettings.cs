using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Persistent, project-wide settings for ProjectCleanPro.
    /// Stored at ProjectSettings/PCPSettings.asset via ScriptableSingleton.
    /// </summary>
    [FilePath("ProjectSettings/PCPSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class PCPSettings : ScriptableSingleton<PCPSettings>
    {
        // ----------------------------------------------------------------
        // Scan scope
        // ----------------------------------------------------------------

        [Header("Scan Scope")]
        [Tooltip("Include all scenes in the project (not just those in Build Settings).")]
        public bool includeAllScenes;

        [Tooltip("Include Addressable entries as roots (requires com.unity.addressables).")]
        public bool includeAddressables = true;

        [Tooltip("Include assets assigned to AssetBundles as roots.")]
        public bool includeAssetBundles = true;

        [Tooltip("Scan assets inside Editor/ folders.")]
        public bool scanEditorAssets;

        // ----------------------------------------------------------------
        // Ignore rules
        // ----------------------------------------------------------------

        [Header("Ignore Rules")]
        [Tooltip("Rules that exclude assets from scan results.")]
        public List<PCPIgnoreRule> ignoreRules = new List<PCPIgnoreRule>();

        // ----------------------------------------------------------------
        // Deletion behaviour
        // ----------------------------------------------------------------

        [Header("Deletion")]
        [Tooltip("Archive assets to Library/ProjectCleanPro/Archive/ before deleting.")]
        public bool archiveBeforeDelete = true;

        [Tooltip("Use 'git rm' instead of AssetDatabase.DeleteAsset when inside a Git repo.")]
        public bool useGitRm;

        [Tooltip("Null-out references to deleted assets in scenes and prefabs.")]
        public bool nullOutReferencesOnDelete;

        // ----------------------------------------------------------------
        // Archive
        // ----------------------------------------------------------------

        [Header("Archive")]
        [Tooltip("Number of days to retain archived assets before auto-cleanup.")]
        [Min(1)]
        public int archiveRetentionDays = 30;

        // ----------------------------------------------------------------
        // Dependency graph
        // ----------------------------------------------------------------

        [Header("Dependency Graph")]
        [Tooltip("Maximum depth for the visual dependency graph.")]
        [Range(1, 10)]
        public int dependencyGraphMaxDepth = 2;

        // ----------------------------------------------------------------
        // Module-specific
        // ----------------------------------------------------------------

        [Header("Shader Analyzer")]
        [Tooltip("Check shaders for render-pipeline compatibility.")]
        public bool shaderAnalyzerCheckPipeline = true;

        [Header("Duplicate Detector")]
        [Tooltip("Compare importer settings when detecting duplicates.")]
        public bool duplicateCompareImportSettings = true;

        // ----------------------------------------------------------------
        // UI / Theming
        // ----------------------------------------------------------------

        [Header("Module Colors")]
        [Tooltip("Eight colours used to tint module headers in the UI.")]
        public Color[] moduleColors = new Color[]
        {
            HexColor("FF6B6B"), // 0  Unused Assets
            HexColor("4ECDC4"), // 1  Missing References
            HexColor("45B7D1"), // 2  Shader Analyzer
            HexColor("96CEB4"), // 3  Duplicate Detector
            HexColor("FFEAA7"), // 4  Texture Optimizer
            HexColor("DDA0DD"), // 5  Dependency Graph
            HexColor("98D8C8"), // 6  Audio Analyzer
            HexColor("F7DC6F"), // 7  Build Report
        };

        // ----------------------------------------------------------------
        // Custom scan roots
        // ----------------------------------------------------------------

        [Header("Custom Scan Roots")]
        [Tooltip("Additional folder paths to include as scan roots (e.g. Assets/MyContent).")]
        public List<string> customScanRoots = new List<string>();

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Persists the current settings to disk.
        /// </summary>
        public void Save()
        {
            Save(true);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Converts a hex colour string (6 chars, no '#') to a Unity Color.
        /// </summary>
        private static Color HexColor(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex, out Color color))
                return color;
            return Color.white;
        }
    }
}
