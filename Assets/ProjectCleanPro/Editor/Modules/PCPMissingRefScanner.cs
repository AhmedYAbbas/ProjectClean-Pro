using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 2 - Missing Reference Finder.
    /// Scans prefabs, scenes, and ScriptableObjects for broken serialized references
    /// and missing MonoBehaviour scripts.
    /// </summary>
    public sealed class PCPMissingRefScanner : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override PCPModuleId Id => PCPModuleId.Missing;
        public override string DisplayName => "Missing References";
        public override string Icon => "\u26A0"; // ⚠
        public override Color AccentColor => new Color(0.902f, 0.494f, 0.133f, 1f); // #E67E22
        public override IReadOnlyCollection<string> RelevantExtensions => s_ScannableExtensions;
        public override bool RequiresDependencyGraph => false;

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        private readonly List<PCPMissingReference> _results = new List<PCPMissingReference>();

        /// <summary>Read-only access to the scan results.</summary>
        public IReadOnlyList<PCPMissingReference> Results => _results;

        public override int FindingCount => _results.Count;

        public override long TotalSizeBytes => 0L;

        // ----------------------------------------------------------------
        // Extensions to scan
        // ----------------------------------------------------------------

        private static readonly HashSet<string> s_ScannableExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".prefab", ".unity", ".asset"
        };

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
        {
            _results.Clear();

            // ----------------------------------------------------------
            // Phase 1: Collect all scannable assets
            // ----------------------------------------------------------
            ReportProgress(0f, "Finding prefabs, scenes, and assets...");

            var assetPaths = new List<string>();
            string[] allPaths = context.AllProjectAssets;

            for (int i = 0; i < allPaths.Length; i++)
            {
                string path = allPaths[i];

                string ext = System.IO.Path.GetExtension(path);
                if (!s_ScannableExtensions.Contains(ext))
                    continue;

                if (IsIgnored(path, context))
                    continue;

                // Skip editor-only paths unless settings opt-in.
                if (!context.Settings.scanEditorAssets && PCPAssetUtils.IsEditorOnlyPath(path))
                    continue;

                assetPaths.Add(path);
            }

            ct.ThrowIfCancellationRequested();

            int total = assetPaths.Count;
            if (total == 0)
            {
                ReportProgress(1f, "No scannable assets found.");
                return;
            }

            // ----------------------------------------------------------
            // Phase 2: Scan each asset for missing references
            // ----------------------------------------------------------
            for (int i = 0; i < total; i++)
            {
                string assetPath = assetPaths[i];

                // If the asset hasn't changed and was previously scanned with
                // zero missing references, we can safely skip it.
                if (!context.Cache.IsStale(assetPath))
                {
                    string cachedCount = context.Cache.GetMetadata(assetPath, "missing.count");
                    if (cachedCount != null && cachedCount == "0")
                        continue;
                }

                await YieldIfNeeded(i, total, $"Scanning asset {i}/{total}...", ct, interval: 32);

                int countBefore = _results.Count;
                ScanAsset(assetPath);
                int found = _results.Count - countBefore;

                // Cache the result count so we can skip clean assets next time.
                context.Cache.SetMetadata(assetPath, "missing.count", found.ToString());
            }

            ReportProgress(1f, $"Found {_results.Count} missing references.");
        }

        /// <summary>
        /// Scans a single asset for missing script components and broken serialized references.
        /// </summary>
        private void ScanAsset(string assetPath)
        {
            string assetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            string ext = System.IO.Path.GetExtension(assetPath);

            if (ext.Equals(".unity", StringComparison.OrdinalIgnoreCase))
            {
                ScanScene(assetPath, assetName);
                return;
            }

            UnityEngine.Object[] allObjects;

            try
            {
                allObjects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            }
            catch (Exception)
            {
                // If we cannot load the asset, skip it.
                return;
            }

            if (allObjects == null || allObjects.Length == 0)
                return;

            // For prefabs, traverse the full GameObject hierarchy just like scenes,
            // then skip GameObjects/Components since they were already covered.
            bool isPrefab = ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase);
            bool prefabHandled = false;

            for (int objIdx = 0; objIdx < allObjects.Length; objIdx++)
            {
                UnityEngine.Object obj = allObjects[objIdx];

                // A null entry in LoadAllAssetsAtPath typically indicates a missing sub-asset.
                if (obj == null)
                    continue;

                GameObject go = obj as GameObject;

                if (isPrefab && go != null && go.transform.parent == null && !prefabHandled)
                {
                    prefabHandled = true;

                    // Walk the full child hierarchy from the prefab root.
                    Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
                    foreach (Transform t in transforms)
                    {
                        GameObject child = t.gameObject;
                        CheckForMissingScripts(child, assetPath, assetName);

                        Component[] components = child.GetComponents<Component>();
                        foreach (Component comp in components)
                        {
                            if (comp == null) continue;
                            CheckSerializedProperties(comp, assetPath, assetName);
                        }
                    }
                }
                else if (isPrefab && (go != null || obj is Component))
                {
                    // Skip — already processed during hierarchy traversal above.
                    continue;
                }
                else
                {
                    // Non-prefab assets (ScriptableObjects, etc.)
                    if (go != null)
                        CheckForMissingScripts(go, assetPath, assetName);

                    CheckSerializedProperties(obj, assetPath, assetName);
                }
            }
        }

        /// <summary>
        /// Scans a scene file by opening it additively, scanning all root objects, then closing it.
        /// </summary>
        private void ScanScene(string assetPath, string assetName)
        {
            // Check if the scene is already open so we don't close it afterwards.
            Scene alreadyOpen = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (string.Equals(s.path, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyOpen = s;
                    break;
                }
            }

            Scene scene;
            bool wasAlreadyOpen = alreadyOpen.IsValid();

            try
            {
                scene = wasAlreadyOpen
                    ? alreadyOpen
                    : EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive);
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                if (!scene.IsValid() || !scene.isLoaded)
                    return;

                GameObject[] roots = scene.GetRootGameObjects();
                foreach (GameObject root in roots)
                {
                    Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (Transform t in transforms)
                    {
                        GameObject go = t.gameObject;
                        CheckForMissingScripts(go, assetPath, assetName);

                        Component[] components = go.GetComponents<Component>();
                        foreach (Component comp in components)
                        {
                            if (comp == null) continue;
                            CheckSerializedProperties(comp, assetPath, assetName);
                        }
                    }
                }
            }
            finally
            {
                if (!wasAlreadyOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Checks a GameObject's components for missing (null) MonoBehaviour scripts.
        /// </summary>
        private void CheckForMissingScripts(GameObject go, string assetPath, string assetName)
        {
            Component[] components = go.GetComponents<Component>();

            for (int c = 0; c < components.Length; c++)
            {
                if (components[c] == null)
                {
                    _results.Add(new PCPMissingReference
                    {
                        sourceAssetPath = assetPath,
                        sourceAssetName = assetName,
                        componentType = "(Missing Script)",
                        propertyPath = string.Empty,
                        missingGuid = string.Empty,
                        severity = PCPSeverity.Error,
                        gameObjectPath = GetGameObjectPath(go)
                    });
                }
            }
        }

        /// <summary>
        /// Iterates all serialized properties on an object to find broken object references.
        /// </summary>
        private void CheckSerializedProperties(UnityEngine.Object obj, string assetPath, string assetName)
        {
            SerializedObject so;

            try
            {
                so = new SerializedObject(obj);
            }
            catch (Exception)
            {
                return;
            }

            SerializedProperty prop = so.GetIterator();

            // enterChildren = true on first call to enter the root.
            bool enterChildren = true;

            while (prop.Next(enterChildren))
            {
                enterChildren = true;

                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                // Skip if the reference is legitimately null (never assigned).
                // A missing reference is identified by: objectReferenceValue == null
                // but objectReferenceInstanceIDValue != 0, meaning it was once assigned
                // to something that no longer exists.
                if (prop.objectReferenceValue == null &&
                    prop.objectReferenceInstanceIDValue != 0)
                {
                    string componentType = obj.GetType().FullName ?? obj.GetType().Name;
                    string goPath = string.Empty;

                    // If the object is a Component, get the hierarchy path.
                    Component comp = obj as Component;
                    if (comp != null && comp.gameObject != null)
                    {
                        goPath = GetGameObjectPath(comp.gameObject);
                        componentType = comp.GetType().FullName ?? comp.GetType().Name;
                    }

                    _results.Add(new PCPMissingReference
                    {
                        sourceAssetPath = assetPath,
                        sourceAssetName = assetName,
                        componentType = componentType,
                        propertyPath = prop.propertyPath,
                        missingGuid = string.Empty,
                        severity = PCPSeverity.Warning,
                        gameObjectPath = goPath
                    });
                }
            }

            so.Dispose();
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
                writer.Write(r.sourceAssetPath ?? string.Empty);
                writer.Write(r.sourceAssetName ?? string.Empty);
                writer.Write(r.componentType ?? string.Empty);
                writer.Write(r.propertyPath ?? string.Empty);
                writer.Write(r.missingGuid ?? string.Empty);
                writer.Write((byte)r.severity);
                writer.Write(r.gameObjectPath ?? string.Empty);
            }
        }

        public override void ReadResults(BinaryReader reader)
        {
            _results.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                _results.Add(new PCPMissingReference
                {
                    sourceAssetPath = reader.ReadString(),
                    sourceAssetName = reader.ReadString(),
                    componentType = reader.ReadString(),
                    propertyPath = reader.ReadString(),
                    missingGuid = reader.ReadString(),
                    severity = (PCPSeverity)reader.ReadByte(),
                    gameObjectPath = reader.ReadString()
                });
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Builds the full hierarchy path to a GameObject (e.g. "Canvas/Panel/Button").
        /// </summary>
        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null)
                return string.Empty;

            string path = go.name;
            Transform current = go.transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
