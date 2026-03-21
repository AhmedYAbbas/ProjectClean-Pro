using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    // ----------------------------------------------------------------
    // Safe-delete engine
    // ----------------------------------------------------------------

    /// <summary>
    /// Deletion engine for ProjectCleanPro.
    /// Provides preview, archive-and-delete, and restore functionality with
    /// full safety checks.
    /// </summary>
    public static class PCPSafeDelete
    {
        private const int MaxReferencingAssetsToShow = 10;

        /// <summary>
        /// Builds a <see cref="PCPDeletePreview"/> for the given asset paths by
        /// computing sizes, reference counts, and generating warnings.
        /// </summary>
        /// <param name="assetPaths">Project-relative asset paths to preview for deletion.</param>
        /// <param name="resolver">A built dependency resolver for reference counting.</param>
        /// <returns>A fully populated delete preview.</returns>
        public static PCPDeletePreview Preview(List<string> assetPaths, PCPDependencyResolver resolver)
        {
            if (assetPaths == null)
                throw new ArgumentNullException(nameof(assetPaths));

            var preview = new PCPDeletePreview();
            long totalSize = 0L;
            int warnCount = 0;
            int errCount = 0;

            // Build a set for fast "is this being deleted" lookups.
            var deletionSet = new HashSet<string>(assetPaths, StringComparer.Ordinal);

            for (int i = 0; i < assetPaths.Count; i++)
            {
                string assetPath = assetPaths[i];
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                var item = new PCPDeleteItem
                {
                    path = assetPath,
                    isInResources = PCPAssetUtils.IsResourcesPath(assetPath),
                    isEditorOnly = PCPAssetUtils.IsEditorOnlyPath(assetPath)
                };

                // Size
                string fullPath = PCPAssetUtils.GetFullPath(assetPath);
                if (File.Exists(fullPath))
                {
                    item.sizeBytes = new FileInfo(fullPath).Length;
                    item.formattedSize = PCPAssetUtils.FormatSize(item.sizeBytes);
                }
                else
                {
                    item.sizeBytes = 0;
                    item.formattedSize = "0 B";
                }

                // Asset type
                Type assetType = PCPAssetUtils.GetAssetType(assetPath);
                item.assetTypeName = assetType != null ? assetType.Name : "Unknown";

                // Reference counting (only available if the resolver has been built)
                if (resolver != null && resolver.IsBuilt)
                {
                    var dependents = resolver.GetDependents(assetPath);
                    // Filter out assets that are also in the deletion set.
                    var externalDependents = new List<string>();
                    foreach (string dep in dependents)
                    {
                        if (!deletionSet.Contains(dep))
                            externalDependents.Add(dep);
                    }

                    item.referenceCount = externalDependents.Count;
                    item.referencingAssets = externalDependents
                        .Take(MaxReferencingAssetsToShow)
                        .ToList();
                }

                totalSize += item.sizeBytes;
                preview.items.Add(item);

                // ---- Generate warnings ----

                // Warning: asset has external references
                if (item.referenceCount > 0)
                {
                    string refNames = string.Join(", ",
                        item.referencingAssets.Select(Path.GetFileName));
                    preview.warnings.Add(new PCPDeleteWarning(
                        assetPath,
                        $"Referenced by {item.referenceCount} asset(s): {refNames}",
                        PCPSeverity.Warning));
                    warnCount++;
                }

                // Warning: asset is in Resources
                if (item.isInResources)
                {
                    preview.warnings.Add(new PCPDeleteWarning(
                        assetPath,
                        "Asset is in a Resources folder and may be loaded at runtime via Resources.Load.",
                        PCPSeverity.Warning));
                    warnCount++;
                }

                // Warning: scene in build settings
                if (PCPAssetUtils.IsScenePath(assetPath))
                {
                    bool isInBuild = EditorBuildSettings.scenes
                        .Any(s => s.enabled && string.Equals(s.path, assetPath, StringComparison.Ordinal));
                    if (isInBuild)
                    {
                        preview.warnings.Add(new PCPDeleteWarning(
                            assetPath,
                            "Scene is included in Build Settings. Deleting it will break your build.",
                            PCPSeverity.Error));
                        errCount++;
                    }
                }

                // Warning: script files
                if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    assetPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                    assetPath.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase))
                {
                    preview.warnings.Add(new PCPDeleteWarning(
                        assetPath,
                        "Deleting scripts or assembly definitions may cause compilation errors.",
                        PCPSeverity.Warning));
                    warnCount++;
                }
            }

            preview.totalSizeBytes = totalSize;
            preview.formattedTotalSize = PCPAssetUtils.FormatSize(totalSize);
            preview.warningCount = warnCount;
            preview.errorCount = errCount;

            return preview;
        }

        /// <summary>
        /// Archives all assets in the preview (if enabled in settings) and then deletes them.
        /// </summary>
        /// <param name="preview">A preview previously built via <see cref="Preview"/>.</param>
        /// <param name="settings">Project settings controlling archive and git behaviour.</param>
        public static void ArchiveAndDelete(PCPDeletePreview preview, PCPSettings settings,
            PCPDependencyResolver resolver = null)
        {
            if (preview == null || !preview.HasItems)
                return;

            List<string> paths = preview.items.Select(item => item.path).ToList();

            // ---- Archive phase ----
            PCPArchiveSession session = null;
            if (settings.archiveBeforeDelete)
            {
                try
                {
                    session = PCPArchiveManager.CreateSession(paths);
                    Debug.Log($"[ProjectCleanPro] Archived {paths.Count} asset(s) to {session.archivePath}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProjectCleanPro] Archive failed: {ex.Message}");
                    if (!EditorUtility.DisplayDialog(
                            "ProjectCleanPro - Archive Failed",
                            $"Failed to archive assets:\n{ex.Message}\n\nProceed with deletion anyway?",
                            "Delete Anyway", "Cancel"))
                    {
                        return;
                    }
                }
            }

            // ---- Null-out references phase ----
            if (settings.nullOutReferencesOnDelete)
            {
                NullOutReferences(preview, resolver);
            }

            // ---- Delete phase ----
            bool useGit = settings.useGitRm && PCPGitUtils.IsGitRepository();

            try
            {
                AssetDatabase.StartAssetEditing();

                if (useGit)
                {
                    // Separate tracked and untracked files.
                    var tracked = new List<string>();
                    var untracked = new List<string>();

                    foreach (string path in paths)
                    {
                        if (PCPGitUtils.IsTracked(path))
                            tracked.Add(path);
                        else
                            untracked.Add(path);
                    }

                    // Batch git rm for tracked files.
                    if (tracked.Count > 0)
                    {
                        PCPGitUtils.GitRmMultiple(tracked);

                        // git rm --cached only removes from the index; delete the
                        // files from disk so they don't reappear as untracked.
                        foreach (string path in tracked)
                        {
                            AssetDatabase.DeleteAsset(path);
                        }
                    }

                    // Delete untracked files via AssetDatabase.
                    foreach (string path in untracked)
                    {
                        AssetDatabase.DeleteAsset(path);
                    }
                }
                else
                {
                    foreach (string path in paths)
                    {
                        if (!AssetDatabase.DeleteAsset(path))
                        {
                            Debug.LogWarning($"[ProjectCleanPro] Failed to delete: {path}");
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();

            int count = paths.Count;
            string size = preview.formattedTotalSize;
            Debug.Log($"[ProjectCleanPro] Deleted {count} asset(s) ({size}).");
        }

        /// <summary>
        /// Walks every asset that references a to-be-deleted asset and sets those
        /// references to <c>null</c>, preventing missing-reference errors at runtime.
        /// Must be called <b>before</b> the assets are actually deleted so that the
        /// GUIDs can still be resolved.
        /// </summary>
        private static void NullOutReferences(PCPDeletePreview preview,
            PCPDependencyResolver resolver)
        {
            // Collect the set of asset paths being deleted.
            var deletionSet = new HashSet<string>(
                preview.items.Select(i => i.path), StringComparer.Ordinal);

            // Collect all unique referencing assets that are NOT themselves being deleted.
            var referencingPaths = new HashSet<string>(StringComparer.Ordinal);
            if (resolver != null && resolver.IsBuilt)
            {
                foreach (string deletedPath in deletionSet)
                {
                    foreach (string dep in resolver.GetDependents(deletedPath))
                    {
                        if (!deletionSet.Contains(dep))
                            referencingPaths.Add(dep);
                    }
                }
            }
            else
            {
                // Resolver not built — compute reverse dependencies directly.
                // Check every asset's dependencies against the deletion set.
                string[] allPaths = AssetDatabase.GetAllAssetPaths();
                for (int i = 0; i < allPaths.Length; i++)
                {
                    string path = allPaths[i];
                    if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (deletionSet.Contains(path))
                        continue;

                    string[] deps = AssetDatabase.GetDependencies(path, false);
                    for (int d = 0; d < deps.Length; d++)
                    {
                        if (deletionSet.Contains(deps[d]))
                        {
                            referencingPaths.Add(path);
                            break;
                        }
                    }
                }
            }

            if (referencingPaths.Count == 0)
                return;

            // Build a lookup of GUIDs we are about to delete.
            var deletedGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in deletionSet)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    deletedGuids.Add(guid);
            }

            if (deletedGuids.Count == 0)
                return;

            int nulledCount = 0;

            foreach (string refPath in referencingPaths)
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(refPath);
                if (assets == null)
                    continue;

                foreach (UnityEngine.Object asset in assets)
                {
                    if (asset == null)
                        continue;

                    using (var so = new SerializedObject(asset))
                    {
                        SerializedProperty prop = so.GetIterator();
                        bool modified = false;

                        while (prop.Next(true))
                        {
                            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                                continue;

                            // Read the file-id / guid stored in the property.
                            string refGuid = null;
                            if (prop.objectReferenceValue != null)
                            {
                                string objPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                                if (!string.IsNullOrEmpty(objPath))
                                    refGuid = AssetDatabase.AssetPathToGUID(objPath);
                            }
                            else if (prop.objectReferenceInstanceIDValue != 0)
                            {
                                // The reference may point to an asset we can resolve via instance ID.
                                var obj = EditorUtility.EntityIdToObject(prop.entityIdValue);
                                if (obj != null)
                                {
                                    string objPath = AssetDatabase.GetAssetPath(obj);
                                    if (!string.IsNullOrEmpty(objPath))
                                        refGuid = AssetDatabase.AssetPathToGUID(objPath);
                                }
                            }

                            if (refGuid != null && deletedGuids.Contains(refGuid))
                            {
                                prop.objectReferenceValue = null;
                                modified = true;
                                nulledCount++;
                            }
                        }

                        if (modified)
                            so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            if (nulledCount > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[ProjectCleanPro] Nulled out {nulledCount} reference(s) in " +
                          $"{referencingPaths.Count} asset(s).");
            }
        }

        /// <summary>
        /// Restores all assets from an archive session back to their original locations.
        /// </summary>
        /// <param name="session">The archive session to restore from.</param>
        public static void Restore(PCPArchiveSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            PCPArchiveManager.RestoreSession(session.sessionId);

            AssetDatabase.Refresh();

            Debug.Log($"[ProjectCleanPro] Restored {session.assetCount} asset(s) from archive " +
                      $"'{session.sessionId}'.");
        }
    }
}
