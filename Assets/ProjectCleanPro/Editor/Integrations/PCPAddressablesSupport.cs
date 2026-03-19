#if PCP_ADDRESSABLES
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Provides Addressable Asset System integration for ProjectCleanPro.
    /// This file is only compiled when the PCP_ADDRESSABLES scripting define is set,
    /// which indicates that the Addressables package is installed and available.
    /// </summary>
    public static class PCPAddressablesSupport
    {
        // ----------------------------------------------------------------
        // Availability
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns <c>true</c> when the Addressables integration is compiled and available.
        /// Because this entire file is wrapped in <c>#if PCP_ADDRESSABLES</c>, the method
        /// only exists when the package is present.
        /// </summary>
        public static bool IsAvailable() => true;

        // ----------------------------------------------------------------
        // Root Collection
        // ----------------------------------------------------------------

        /// <summary>
        /// Collects all asset paths from every Addressable group and entry.
        /// </summary>
        /// <returns>
        /// A list of asset paths (e.g. "Assets/Prefabs/Player.prefab") that are
        /// registered in any Addressable group. Returns an empty list if settings
        /// are not configured or no entries exist.
        /// </returns>
        public static List<string> GetAddressableRoots()
        {
            var roots = new List<string>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return roots;

            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null)
                    continue;

                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (entry == null)
                        continue;

                    string path = entry.AssetPath;
                    if (!string.IsNullOrEmpty(path))
                        roots.Add(path);
                }
            }

            return roots;
        }

        // ----------------------------------------------------------------
        // Labels
        // ----------------------------------------------------------------

        /// <summary>
        /// Retrieves all Addressable labels defined in the project settings.
        /// </summary>
        /// <returns>
        /// A list of label strings. Returns an empty list if settings are not configured.
        /// </returns>
        public static List<string> GetAddressableLabels()
        {
            var labels = new List<string>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return labels;

            labels.AddRange(settings.GetLabels());
            return labels;
        }

        // ----------------------------------------------------------------
        // Single-Asset Query
        // ----------------------------------------------------------------

        /// <summary>
        /// Checks whether the asset at the given path belongs to any Addressable group.
        /// </summary>
        /// <param name="assetPath">
        /// A project-relative asset path (e.g. "Assets/Textures/Icon.png").
        /// </param>
        /// <returns><c>true</c> if the asset is found in an Addressable group entry.</returns>
        public static bool IsAddressable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return false;

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                return false;

            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            return entry != null;
        }

        // ----------------------------------------------------------------
        // Group Map
        // ----------------------------------------------------------------

        /// <summary>
        /// Builds a mapping from asset path to the name of the Addressable group
        /// that contains it.
        /// </summary>
        /// <returns>
        /// A dictionary where keys are asset paths and values are group names.
        /// Returns an empty dictionary if settings are not configured.
        /// </returns>
        public static Dictionary<string, string> GetAddressableGroupMap()
        {
            var map = new Dictionary<string, string>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return map;

            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null)
                    continue;

                string groupName = group.Name;

                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (entry == null)
                        continue;

                    string path = entry.AssetPath;
                    if (!string.IsNullOrEmpty(path) && !map.ContainsKey(path))
                        map[path] = groupName;
                }
            }

            return map;
        }
    }
}
#endif
