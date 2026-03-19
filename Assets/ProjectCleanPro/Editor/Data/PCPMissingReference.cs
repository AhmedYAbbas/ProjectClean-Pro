using System;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Severity level for a missing-reference finding.
    /// </summary>
    public enum PCPSeverity
    {
        /// <summary>Informational - unlikely to cause issues.</summary>
        Info,

        /// <summary>Warning - may cause issues at runtime.</summary>
        Warning,

        /// <summary>Error - will cause issues at runtime or in the build.</summary>
        Error
    }

    /// <summary>
    /// A single missing-reference finding: a serialized property on a component or asset
    /// that points to a GUID that can no longer be resolved.
    /// </summary>
    [Serializable]
    public class PCPMissingReference
    {
        /// <summary>
        /// Project-relative path of the asset that contains the broken reference
        /// (e.g. a scene, prefab, or ScriptableObject).
        /// </summary>
        public string sourceAssetPath;

        /// <summary>Display name of the source asset.</summary>
        public string sourceAssetName;

        /// <summary>
        /// Fully-qualified type name of the component that holds the broken reference
        /// (e.g. "UnityEngine.MeshRenderer"). May be "(Missing Script)" for missing MonoBehaviours.
        /// </summary>
        public string componentType;

        /// <summary>
        /// The serialized property path that is broken (e.g. "m_Materials.Array.data[0]").
        /// </summary>
        public string propertyPath;

        /// <summary>
        /// The GUID that the reference points to, if recoverable from the serialized data.
        /// May be empty if the reference was a direct object reference that is simply null.
        /// </summary>
        public string missingGuid;

        /// <summary>Severity assessment of this missing reference.</summary>
        public PCPSeverity severity;

        /// <summary>
        /// For scene and prefab assets, the full hierarchy path to the GameObject
        /// containing the broken reference (e.g. "Canvas/Panel/Button").
        /// Empty for non-hierarchical assets like ScriptableObjects.
        /// </summary>
        public string gameObjectPath;

        /// <summary>
        /// Returns a formatted single-line summary suitable for log output and list views.
        /// </summary>
        public string GetSummary()
        {
            var location = string.IsNullOrEmpty(gameObjectPath)
                ? sourceAssetPath
                : $"{sourceAssetPath} -> {gameObjectPath}";

            return $"[{severity}] {location} | {componentType}.{propertyPath}";
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }
}
