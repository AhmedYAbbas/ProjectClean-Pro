using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Warning attached to a single asset in a <see cref="PCPDeletePreview"/>.
    /// </summary>
    [Serializable]
    public sealed class PCPDeleteWarning
    {
        public string assetPath;
        public string message;
        public PCPSeverity severity;

        public PCPDeleteWarning(string assetPath, string message, PCPSeverity severity)
        {
            this.assetPath = assetPath;
            this.message = message;
            this.severity = severity;
        }

        public override string ToString()
        {
            return $"[{severity}] {assetPath}: {message}";
        }
    }

    /// <summary>
    /// Information about a single asset that is staged for deletion.
    /// </summary>
    [Serializable]
    public sealed class PCPDeleteItem
    {
        /// <summary>Project-relative asset path.</summary>
        public string path;

        /// <summary>File size in bytes.</summary>
        public long sizeBytes;

        /// <summary>Human-readable formatted size.</summary>
        public string formattedSize;

        /// <summary>Number of assets that directly reference this asset.</summary>
        public int referenceCount;

        /// <summary>Names of referencing assets (capped for display).</summary>
        public List<string> referencingAssets = new List<string>();

        /// <summary>True if this asset is in a Resources folder.</summary>
        public bool isInResources;

        /// <summary>True if this asset resides under an Editor folder.</summary>
        public bool isEditorOnly;

        /// <summary>Type name of the asset.</summary>
        public string assetTypeName;
    }

    /// <summary>
    /// A preview of what will happen when a batch of assets is deleted.
    /// Built by <see cref="PCPSafeDelete.Preview"/> before any destructive action is taken.
    /// </summary>
    [Serializable]
    public sealed class PCPDeletePreview
    {
        /// <summary>Items staged for deletion with full metadata.</summary>
        public List<PCPDeleteItem> items = new List<PCPDeleteItem>();

        /// <summary>Warnings generated during preview analysis.</summary>
        public List<PCPDeleteWarning> warnings = new List<PCPDeleteWarning>();

        /// <summary>Total size of all items in bytes.</summary>
        public long totalSizeBytes;

        /// <summary>Human-readable total size.</summary>
        public string formattedTotalSize;

        /// <summary>Number of items with at least one warning.</summary>
        public int warningCount;

        /// <summary>Number of items with error-level warnings.</summary>
        public int errorCount;

        /// <summary>Whether the preview contains any items.</summary>
        public bool HasItems => items != null && items.Count > 0;

        /// <summary>Whether there are error-severity warnings that should block deletion.</summary>
        public bool HasErrors => errorCount > 0;
    }
}
