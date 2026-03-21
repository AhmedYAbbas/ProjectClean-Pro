using System;
using UnityEditor;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Lightweight metadata snapshot of a single asset in the project.
    /// Created via <see cref="FromPath"/> and intended to be serialized in scan results.
    /// </summary>
    [Serializable]
    public class PCPAssetInfo
    {
        /// <summary>Asset path relative to the project root (e.g. "Assets/Textures/Hero.png").</summary>
        public string path;

        /// <summary>Unity GUID for this asset.</summary>
        public string guid;

        /// <summary>File name without extension.</summary>
        public string name;

        /// <summary>File extension including the leading dot (e.g. ".png").</summary>
        public string extension;

        /// <summary>The main asset type reported by <see cref="AssetDatabase"/>.</summary>
        public Type assetType;

        /// <summary>Human-readable type name, safe for serialization when <see cref="assetType"/> is null.</summary>
        public string assetTypeName;

        /// <summary>File size on disk in bytes. Zero if the file does not exist.</summary>
        public long sizeBytes;

        /// <summary>Last write time (UTC) stored as ticks for deterministic comparison.</summary>
        public long lastModifiedTicks;

        /// <summary>
        /// Creates a <see cref="PCPAssetInfo"/> from an asset path by querying
        /// the <see cref="AssetDatabase"/> and the file system.
        /// </summary>
        /// <param name="assetPath">A project-relative asset path (e.g. "Assets/...").</param>
        /// <returns>A fully populated <see cref="PCPAssetInfo"/> instance.</returns>
        public static PCPAssetInfo FromPath(string assetPath)
        {
            var info = new PCPAssetInfo
            {
                path = assetPath,
                guid = AssetDatabase.AssetPathToGUID(assetPath),
                name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                extension = System.IO.Path.GetExtension(assetPath),
                assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath)
            };

            info.assetTypeName = info.assetType != null ? info.assetType.Name : "Unknown";

            var fullPath = System.IO.Path.GetFullPath(assetPath);
            if (System.IO.File.Exists(fullPath))
            {
                var fi = new System.IO.FileInfo(fullPath);
                info.sizeBytes = fi.Length;
                info.lastModifiedTicks = fi.LastWriteTimeUtc.Ticks;
            }

            return info;
        }

        public override string ToString()
        {
            return $"{name}{extension} ({assetTypeName}, {FormatBytes(sizeBytes)})";
        }

        /// <summary>
        /// Formats a byte count into a human-readable string (B, KB, MB, GB).
        /// Delegates to <see cref="PCPAssetUtils.FormatSize"/>.
        /// </summary>
        internal static string FormatBytes(long bytes)
        {
            return PCPAssetUtils.FormatSize(bytes);
        }
    }
}
