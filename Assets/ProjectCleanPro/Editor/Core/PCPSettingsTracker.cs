using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Tracks changes to <see cref="PCPSettings"/> and marks affected modules
    /// as dirty so they re-run on the next scan. Listens to
    /// <see cref="PCPSettings.OnSettingsSaved"/>.
    /// <para>
    /// This ensures that changing scan roots, ignore rules, or module-specific
    /// settings invalidates only the relevant modules — not the entire cache.
    /// </para>
    /// </summary>
    public static class PCPSettingsTracker
    {
        private static readonly HashSet<PCPModuleId> s_DirtyModules = new HashSet<PCPModuleId>();

        // Snapshot of settings values taken at the end of the last scan.
        private static bool s_LastIncludeAllScenes;
        private static bool s_LastIncludeAddressables;
        private static bool s_LastIncludeAssetBundles;
        private static bool s_LastScanEditorAssets;
        private static bool s_LastShaderCheckPipeline;
        private static bool s_LastDuplicateCompareImport;
        private static int s_LastIgnoreRulesHash;
        private static int s_LastAlwaysUsedRootsHash;
        private static bool s_Initialized;

        /// <summary>
        /// Whether any modules were dirtied by settings changes.
        /// </summary>
        public static bool HasDirtyModules => s_DirtyModules.Count > 0;

        /// <summary>
        /// Returns true if the given module was invalidated by a settings change.
        /// </summary>
        public static bool IsModuleDirty(PCPModuleId id) => s_DirtyModules.Contains(id);

        /// <summary>
        /// Clears all settings-dirty flags. Call after a scan completes.
        /// </summary>
        public static void Reset()
        {
            s_DirtyModules.Clear();
        }

        /// <summary>
        /// Takes a snapshot of the current settings so the next
        /// <see cref="OnSettingsChanged"/> can diff against it.
        /// Call after a scan completes or on first initialization.
        /// </summary>
        public static void TakeSnapshot(PCPSettings settings)
        {
            if (settings == null) return;

            s_LastIncludeAllScenes = settings.includeAllScenes;
            s_LastIncludeAddressables = settings.includeAddressables;
            s_LastIncludeAssetBundles = settings.includeAssetBundles;
            s_LastScanEditorAssets = settings.scanEditorAssets;
            s_LastShaderCheckPipeline = settings.shaderAnalyzerCheckPipeline;
            s_LastDuplicateCompareImport = settings.duplicateCompareImportSettings;
            s_LastIgnoreRulesHash = ComputeIgnoreRulesHash(settings);
            s_LastAlwaysUsedRootsHash = ComputeRootsHash(settings);
            s_Initialized = true;
        }

        /// <summary>
        /// Compares current settings against the last snapshot and marks
        /// affected modules as dirty. Called by <see cref="PCPSettings.OnSettingsSaved"/>.
        /// </summary>
        public static void OnSettingsChanged(PCPSettings settings)
        {
            if (settings == null) return;

            // On first call before any snapshot, mark everything dirty.
            if (!s_Initialized)
            {
                MarkAllDirty();
                TakeSnapshot(settings);
                return;
            }

            // Root-affecting settings -> Unused + Dependencies.
            if (settings.includeAllScenes != s_LastIncludeAllScenes ||
                settings.includeAddressables != s_LastIncludeAddressables ||
                settings.includeAssetBundles != s_LastIncludeAssetBundles ||
                ComputeRootsHash(settings) != s_LastAlwaysUsedRootsHash)
            {
                s_DirtyModules.Add(PCPModuleId.Unused);
                s_DirtyModules.Add(PCPModuleId.Dependencies);
            }

            // Ignore rules or scan scope -> ALL modules.
            if (ComputeIgnoreRulesHash(settings) != s_LastIgnoreRulesHash ||
                settings.scanEditorAssets != s_LastScanEditorAssets)
            {
                MarkAllDirty();
            }

            // Shader pipeline setting -> Shaders only.
            if (settings.shaderAnalyzerCheckPipeline != s_LastShaderCheckPipeline)
            {
                s_DirtyModules.Add(PCPModuleId.Shaders);
            }

            // Duplicate import compare -> Duplicates only.
            if (settings.duplicateCompareImportSettings != s_LastDuplicateCompareImport)
            {
                s_DirtyModules.Add(PCPModuleId.Duplicates);
            }

            // Update snapshot for next diff.
            TakeSnapshot(settings);
        }

        private static void MarkAllDirty()
        {
            foreach (PCPModuleId id in Enum.GetValues(typeof(PCPModuleId)))
                s_DirtyModules.Add(id);
        }

        private static int ComputeIgnoreRulesHash(PCPSettings settings)
        {
            if (settings.ignoreRules == null || settings.ignoreRules.Count == 0)
                return 0;

            int hash = 17;
            for (int i = 0; i < settings.ignoreRules.Count; i++)
            {
                var rule = settings.ignoreRules[i];
                hash = hash * 31 + (rule?.ToString()?.GetHashCode() ?? 0);
            }
            return hash;
        }

        private static int ComputeRootsHash(PCPSettings settings)
        {
            if (settings.alwaysUsedRoots == null || settings.alwaysUsedRoots.Count == 0)
                return 0;

            int hash = 17;
            for (int i = 0; i < settings.alwaysUsedRoots.Count; i++)
                hash = hash * 31 + (settings.alwaysUsedRoots[i]?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
