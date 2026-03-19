using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Metadata for a single file that was archived (backed up) during a
    /// delete or clean-up operation.
    /// </summary>
    [Serializable]
    public class PCPArchivedFile
    {
        /// <summary>The original project-relative path before archiving.</summary>
        public string originalPath;

        /// <summary>Unity GUID of the archived asset.</summary>
        public string guid;

        /// <summary>File size in bytes at the time of archiving.</summary>
        public long sizeBytes;

        public override string ToString()
        {
            return $"{originalPath} ({PCPAssetInfo.FormatBytes(sizeBytes)})";
        }
    }

}
