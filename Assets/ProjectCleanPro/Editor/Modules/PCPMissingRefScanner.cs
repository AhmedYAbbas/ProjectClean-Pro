using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectCleanPro.Editor.Core;
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

        private List<PCPMissingReference> _results = new List<PCPMissingReference>();

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

            var allAssets = await context.GetAllProjectAssetsAsync(ct);
            var candidates = allAssets
                .Where(p =>
                {
                    var ext = System.IO.Path.GetExtension(p);
                    return ext is ".prefab" or ".unity" or ".asset";
                })
                .Where(p => !IsIgnored(p, context))
                .Where(p => context.Settings.scanEditorAssets || !PCPAssetUtils.IsEditorOnlyPath(p))
                .ToList();

            Interlocked.Exchange(ref m_TotalCount, candidates.Count);

            // === PHASE 1: GATHER — Pre-filter on background threads ===
            var suspicious = new ConcurrentBag<string>();

            await PCPThreading.ParallelForEachAsync(candidates, async (path, token) =>
            {
                try
                {
                    if (!context.Cache.IsStale(path))
                    {
                        var cachedCount = context.Cache.GetMetadata(path, "missing.count");
                        if (cachedCount == "0")
                        {
                            Interlocked.Increment(ref m_ProcessedCount);
                            return;
                        }
                    }

                    var content = await Task.Run(() => System.IO.File.ReadAllText(path), token);
                    if (ContainsSuspiciousPatterns(content))
                        suspicious.Add(path);
                }
                catch (OperationCanceledException) { throw; }
                catch (System.Exception ex)
                {
                    m_Warnings.Enqueue($"Skipped {path}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Increment(ref m_ProcessedCount);
                }
            }, PCPThreading.DefaultConcurrency, ct);

            // === PHASE 2: QUERY — Deep inspection on main thread (frame-budgeted) ===
            var suspiciousList = suspicious.ToList();

            if (suspiciousList.Count > 0 && context.Scheduler != null)
            {
                var results = await context.Scheduler.BatchOnMainThread(
                    suspiciousList,
                    path => InspectForMissingRefs(path),
                    ct);

                // === PHASE 3: ANALYZE — Build results ===
                var combined = new List<PCPMissingReference>();
                for (int i = 0; i < suspiciousList.Count; i++)
                {
                    var refs = results[i];
                    if (refs != null && refs.Count > 0)
                        combined.AddRange(refs);

                    context.Cache.SetMetadata(suspiciousList[i], "missing.count",
                        (refs?.Count ?? 0).ToString());
                }
                _results = combined;
            }
            else
            {
                _results = new List<PCPMissingReference>();
            }
        }

        // ----------------------------------------------------------------
        // Background pre-filter
        // ----------------------------------------------------------------

        private static bool ContainsSuspiciousPatterns(string content)
        {
            return content.Contains("fileID: 0,") ||
                   content.Contains("guid: 00000000000000000000000000000000") ||
                   content.Contains("{fileID: 0}");
        }

        // ----------------------------------------------------------------
        // Main-thread deep inspection
        // ----------------------------------------------------------------

        /// <summary>
        /// Loads and inspects a single asset on the main thread for missing references.
        /// Returns all findings for that asset, or an empty list if none.
        /// </summary>
        private List<PCPMissingReference> InspectForMissingRefs(string assetPath)
        {
            var findings = new List<PCPMissingReference>();
            string assetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            string ext = System.IO.Path.GetExtension(assetPath);

            if (ext.Equals(".unity", StringComparison.OrdinalIgnoreCase))
            {
                CollectSceneFindings(assetPath, assetName, findings);
                return findings;
            }

            UnityEngine.Object[] allObjects;

            try
            {
                allObjects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            }
            catch (Exception)
            {
                return findings;
            }

            if (allObjects == null || allObjects.Length == 0)
                return findings;

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
                        CollectMissingScriptFindings(child, assetPath, assetName, findings);

                        Component[] components = child.GetComponents<Component>();
                        foreach (Component comp in components)
                        {
                            if (comp == null) continue;
                            CollectSerializedPropertyFindings(comp, assetPath, assetName, findings);
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
                        CollectMissingScriptFindings(go, assetPath, assetName, findings);

                    CollectSerializedPropertyFindings(obj, assetPath, assetName, findings);
                }
            }

            return findings;
        }

        /// <summary>
        /// Scans a scene file by opening it additively, scanning all root objects, then closing it.
        /// </summary>
        private void CollectSceneFindings(string assetPath, string assetName, List<PCPMissingReference> findings)
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
                        CollectMissingScriptFindings(go, assetPath, assetName, findings);

                        Component[] components = go.GetComponents<Component>();
                        foreach (Component comp in components)
                        {
                            if (comp == null) continue;
                            CollectSerializedPropertyFindings(comp, assetPath, assetName, findings);
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
        private void CollectMissingScriptFindings(GameObject go, string assetPath, string assetName,
            List<PCPMissingReference> findings)
        {
            Component[] components = go.GetComponents<Component>();

            for (int c = 0; c < components.Length; c++)
            {
                if (components[c] == null)
                {
                    findings.Add(new PCPMissingReference
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
        private void CollectSerializedPropertyFindings(UnityEngine.Object obj, string assetPath, string assetName,
            List<PCPMissingReference> findings)
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

                    findings.Add(new PCPMissingReference
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
