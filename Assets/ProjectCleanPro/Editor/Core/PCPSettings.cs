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

        [Tooltip("Treat Resources/ folder assets as roots (always reachable). When off, Resources assets can appear as unused with a warning.")]
        public bool treatResourcesAsRoots;

        // ----------------------------------------------------------------
        // Excluded extensions
        // ----------------------------------------------------------------

        [Header("Excluded Extensions")]
        [Tooltip("File extensions excluded from Unused Asset and Duplicate scans (e.g. .cs, .dll). Include the leading dot.")]
        public List<string> excludedExtensions = new List<string>(s_DefaultExcludedExtensions);

        internal static readonly string[] s_DefaultExcludedExtensions =
        {
            ".cs", ".meta", ".asmdef", ".asmref", ".dll", ".so", ".dylib",
            ".a", ".rsp", ".cginc", ".hlsl", ".glslinc"
        };

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
        // Performance
        // ----------------------------------------------------------------

        public float mainThreadBudgetMs = 8f;

        // ----------------------------------------------------------------
        // Logging
        // ----------------------------------------------------------------

        [Header("Logging")]
        [Tooltip("Log scan progress, cache activity, and timing to the Console.")]
        public bool enableLogging = true;

        /// <summary>
        /// Logs a message to the Console if logging is enabled.
        /// </summary>
        public static void Log(string message)
        {
            if (instance.enableLogging)
                Debug.Log(message);
        }

        // ----------------------------------------------------------------
        // UI / Theming
        // ----------------------------------------------------------------

        [Header("Module Colors")]
        [Tooltip("Eight colours used to tint module headers, dashboard cards, and tab accents.")]
        public Color[] moduleColors = new Color[]
        {
            new Color(1.000f, 0.420f, 0.420f), // 0  Unused Assets     #FF6B6B
            new Color(0.306f, 0.804f, 0.769f), // 1  Missing References #4ECDC4
            new Color(0.588f, 0.808f, 0.706f), // 2  Duplicates         #96CEB4
            new Color(0.867f, 0.627f, 0.867f), // 3  Dependencies       #DDA0DD
            new Color(1.000f, 0.918f, 0.655f), // 4  Packages           #FFEAA7
            new Color(0.271f, 0.718f, 0.820f), // 5  Shaders            #45B7D1
            new Color(0.969f, 0.863f, 0.435f), // 6  Size Profiler      #F7DC6F
            new Color(0.596f, 0.847f, 0.784f), // 7  Archive            #98D8C8
        };

        /// <summary>
        /// Safely returns the module color at the given index, with a fallback.
        /// </summary>
        public Color GetModuleColor(int index)
        {
            if (moduleColors != null && index >= 0 && index < moduleColors.Length)
                return moduleColors[index];
            return new Color(0.337f, 0.612f, 0.839f);
        }

        // ----------------------------------------------------------------
        // Always-used roots
        // ----------------------------------------------------------------

        [Header("Always-Used Roots")]
        [Tooltip("Folders or assets treated as used during scanning. Anything here (and its dependencies) will never be flagged as unused.")]
        public List<string> alwaysUsedRoots = new List<string>();

        // ----------------------------------------------------------------
        // Events
        // ----------------------------------------------------------------

        /// <summary>
        /// Raised after settings are saved to disk.
        /// </summary>
        public static event Action OnSettingsSaved;

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Persists the current settings to disk.
        /// </summary>
        public void Save()
        {
            Save(true);
            OnSettingsSaved?.Invoke();
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
