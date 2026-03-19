using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Classification of a UPM package's usage status within the project.
    /// </summary>
    public enum PCPPackageStatus
    {
        /// <summary>The package is actively referenced by project code or assets.</summary>
        Used,

        /// <summary>The package is installed but no references to it were found.</summary>
        Unused,

        /// <summary>The package is only present as a transitive dependency of another package.</summary>
        TransitiveOnly,

        /// <summary>Usage status could not be determined.</summary>
        Unknown
    }

    /// <summary>
    /// Audit result for a single Unity Package Manager package, recording how
    /// (and whether) the package is used by the project.
    /// </summary>
    [Serializable]
    public class PCPPackageAuditEntry
    {
        /// <summary>
        /// Package identifier as it appears in the manifest
        /// (e.g. "com.unity.textmeshpro").
        /// </summary>
        public string packageName;

        /// <summary>Installed version string (e.g. "3.0.6").</summary>
        public string version;

        /// <summary>Human-readable display name (e.g. "TextMeshPro").</summary>
        public string displayName;

        /// <summary>Package description from its package.json.</summary>
        public string description;

        /// <summary>Computed usage status.</summary>
        public PCPPackageStatus status;

        /// <summary>
        /// Number of direct asset references (e.g. materials, prefabs) that point
        /// into this package's folders.
        /// </summary>
        public int directReferenceCount;

        /// <summary>
        /// Number of C# source files in the project that contain a <c>using</c>
        /// directive for a namespace owned by this package.
        /// </summary>
        public int codeReferenceCount;

        /// <summary>
        /// Other packages that list this package as a dependency. Empty if no
        /// other installed package depends on it.
        /// </summary>
        public List<string> dependedOnBy = new List<string>();

        /// <summary>Total number of references (direct + code).</summary>
        public int TotalReferenceCount => directReferenceCount + codeReferenceCount;

        /// <summary>True if this package is only installed because another package requires it.</summary>
        public bool IsTransitive => status == PCPPackageStatus.TransitiveOnly;

        /// <summary>
        /// Returns a one-line summary suitable for list views and log output.
        /// </summary>
        public string GetSummary()
        {
            var deps = dependedOnBy.Count > 0
                ? $", depended on by: {string.Join(", ", dependedOnBy)}"
                : "";

            return $"{displayName} ({packageName}@{version}) [{status}] " +
                   $"refs: {TotalReferenceCount}{deps}";
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }
}
