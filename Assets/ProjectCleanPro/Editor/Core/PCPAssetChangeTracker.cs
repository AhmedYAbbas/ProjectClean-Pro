using System.Collections.Generic;
using UnityEditor;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Listens to Unity's asset import pipeline to track which assets
    /// have changed (imported, deleted, moved) between scans.
    /// This allows <see cref="PCPScanCache.RefreshStaleness"/> to skip
    /// the expensive per-file timestamp check when nothing has changed.
    /// <para>
    /// Also tracks changes to <c>Packages/manifest.json</c> and
    /// <c>Packages/packages-lock.json</c> so the package auditor
    /// module can be skipped when no packages were added or removed.
    /// </para>
    /// </summary>
    public sealed class PCPAssetChangeTracker : AssetPostprocessor
    {
        // After domain reload we don't know what changed → force full check.
        private static bool s_FullCheckNeeded = true;

        private static readonly HashSet<string> s_ChangedAssets =
            new HashSet<string>(System.StringComparer.Ordinal);

        // Tracks whether the UPM manifest changed between scans.
        private static bool s_PackagesChanged;

        /// <summary>
        /// True when any asset change has been detected since the last
        /// <see cref="Reset"/> (or since domain reload).
        /// </summary>
        public static bool HasChanges => s_FullCheckNeeded || s_ChangedAssets.Count > 0;

        /// <summary>
        /// True after a domain reload until the first scan completes.
        /// </summary>
        public static bool FullCheckNeeded => s_FullCheckNeeded;

        /// <summary>
        /// The set of asset paths that changed since the last <see cref="Reset"/>.
        /// Only meaningful when <see cref="FullCheckNeeded"/> is false.
        /// </summary>
        public static IReadOnlyCollection<string> ChangedAssets => s_ChangedAssets;

        /// <summary>
        /// True when <c>Packages/manifest.json</c> or <c>packages-lock.json</c>
        /// changed since the last <see cref="Reset"/>. The package auditor module
        /// uses this to decide whether it needs re-running.
        /// </summary>
        public static bool PackagesChanged => s_PackagesChanged;

        /// <summary>
        /// Clears all tracked changes. Call after a scan completes.
        /// </summary>
        public static void Reset()
        {
            s_ChangedAssets.Clear();
            s_FullCheckNeeded = false;
            s_PackagesChanged = false;
        }

        // Unity callback — fires for every import, delete, or move.
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            AddPaths(importedAssets);
            AddPaths(deletedAssets);
            AddPaths(movedAssets);
            AddPaths(movedFromAssetPaths);
        }

        private static void AddPaths(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                s_ChangedAssets.Add(path);

                // Detect package manifest changes.
                if (path == "Packages/manifest.json" ||
                    path == "Packages/packages-lock.json")
                {
                    s_PackagesChanged = true;
                }
            }
        }
    }
}
