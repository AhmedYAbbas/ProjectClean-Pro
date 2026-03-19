using System;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Size profiler entry representing a single asset or folder with its
    /// disk footprint and optimization suggestions.
    /// </summary>
    [Serializable]
    public class PCPSizeEntry
    {
        /// <summary>Project-relative path to the asset or folder.</summary>
        public string path;

        /// <summary>Display name (file name without extension, or folder name).</summary>
        public string name;

        /// <summary>Human-readable asset type name (e.g. "Texture2D", "AudioClip").</summary>
        public string assetTypeName;

        /// <summary>
        /// Parent folder path (e.g. "Assets/Textures" for "Assets/Textures/Hero.png").
        /// Used for folder-level aggregation in the tree view.
        /// </summary>
        public string folderPath;

        /// <summary>Size on disk in bytes.</summary>
        public long sizeBytes;

        /// <summary>
        /// This asset's size as a percentage of the total project size (0-100).
        /// Populated during scan aggregation.
        /// </summary>
        public float percentOfTotal;

        /// <summary>
        /// Import/compression information for the asset. For textures this might be
        /// "ASTC 6x6 (2048x2048)", for audio "Vorbis @ 44100 Hz".
        /// Empty for asset types where compression is not applicable.
        /// </summary>
        public string compressionInfo;

        /// <summary>True if the scanner identified a potential optimization.</summary>
        public bool hasOptimizationSuggestion;

        /// <summary>
        /// Human-readable optimization suggestion, e.g.
        /// "Texture is 4096x4096 - consider 2048x2048 for mobile".
        /// Empty when <see cref="hasOptimizationSuggestion"/> is false.
        /// </summary>
        public string optimizationSuggestion;

        /// <summary>
        /// Returns the size formatted as a human-readable string (B, KB, MB, GB).
        /// </summary>
        public string FormattedSize => PCPAssetInfo.FormatBytes(sizeBytes);

        /// <summary>
        /// Returns a one-line summary suitable for list views and log output.
        /// </summary>
        public string GetSummary()
        {
            var suggestion = hasOptimizationSuggestion
                ? $" ** {optimizationSuggestion}"
                : "";

            return $"{name} ({assetTypeName}) - {FormattedSize} ({percentOfTotal:F1}%){suggestion}";
        }

        /// <summary>
        /// Creates a <see cref="PCPSizeEntry"/> from a <see cref="PCPAssetInfo"/>.
        /// </summary>
        public static PCPSizeEntry FromAssetInfo(PCPAssetInfo assetInfo)
        {
            return new PCPSizeEntry
            {
                path = assetInfo.path,
                name = assetInfo.name,
                assetTypeName = assetInfo.assetTypeName,
                folderPath = System.IO.Path.GetDirectoryName(assetInfo.path)?
                    .Replace('\\', '/') ?? string.Empty,
                sizeBytes = assetInfo.sizeBytes,
                compressionInfo = string.Empty,
                optimizationSuggestion = string.Empty
            };
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }
}
