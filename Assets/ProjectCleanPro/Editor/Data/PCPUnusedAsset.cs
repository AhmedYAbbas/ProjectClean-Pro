using System;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Represents an asset that the unused-asset scanner has determined is not
    /// referenced anywhere in the project's build or scene graph.
    /// </summary>
    [Serializable]
    public class PCPUnusedAsset
    {
        /// <summary>Full metadata for the unused asset.</summary>
        public PCPAssetInfo assetInfo;

        /// <summary>
        /// True if the asset resides under a Resources/ folder and will be included
        /// in the build regardless of explicit references.
        /// </summary>
        public bool isInResources;

        /// <summary>True if the asset originates from an installed package (read-only).</summary>
        public bool isInPackage;

        /// <summary>
        /// A human-readable suggestion such as "Safe to delete" or
        /// "Referenced only by disabled component".
        /// </summary>
        public string suggestedAction;

        /// <summary>Convenience accessor for the asset path.</summary>
        public string Path => assetInfo != null ? assetInfo.path : string.Empty;

        /// <summary>Convenience accessor for the asset size in bytes.</summary>
        public long SizeBytes => assetInfo != null ? assetInfo.sizeBytes : 0L;

        /// <summary>
        /// Creates a <see cref="PCPUnusedAsset"/> from an asset path with default analysis fields.
        /// </summary>
        public static PCPUnusedAsset FromPath(string assetPath)
        {
            var unused = new PCPUnusedAsset
            {
                assetInfo = PCPAssetInfo.FromPath(assetPath),
                isInResources = PCPAssetUtils.IsResourcesPath(assetPath),
                isInPackage = assetPath.StartsWith("Packages/"),
                suggestedAction = "Safe to delete"
            };

            if (unused.isInResources)
                unused.suggestedAction = "In Resources folder - verify not loaded at runtime";
            else if (unused.isInPackage)
                unused.suggestedAction = "Package asset - consider removing the package";

            return unused;
        }

        public override string ToString()
        {
            return $"[Unused] {assetInfo} - {suggestedAction}";
        }
    }
}
