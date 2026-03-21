using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// A single entry within a <see cref="PCPDuplicateGroup"/> representing one copy
    /// of a duplicated asset.
    /// </summary>
    [Serializable]
    public class PCPDuplicateEntry
    {
        /// <summary>Project-relative asset path.</summary>
        public string path;

        /// <summary>Unity GUID.</summary>
        public string guid;

        /// <summary>File size in bytes.</summary>
        public long sizeBytes;

        /// <summary>
        /// Number of other assets / scenes / prefabs that reference this particular copy.
        /// Used to decide which copy is the "canonical" one to keep.
        /// </summary>
        public int referenceCount;

        /// <summary>
        /// True if this entry has been elected as the canonical (kept) copy.
        /// Typically the copy with the highest <see cref="referenceCount"/>.
        /// </summary>
        public bool isCanonical;

        public override string ToString()
        {
            var tag = isCanonical ? " [canonical]" : "";
            return $"{path} ({PCPAssetInfo.FormatBytes(sizeBytes)}, {referenceCount} refs){tag}";
        }
    }

    /// <summary>
    /// A group of assets that share identical content (by SHA-256 hash).
    /// Contains two or more <see cref="PCPDuplicateEntry"/> instances.
    /// </summary>
    [Serializable]
    public class PCPDuplicateGroup
    {
        /// <summary>SHA-256 content hash shared by all entries in this group.</summary>
        public string hash;

        /// <summary>All copies of the duplicated asset.</summary>
        public List<PCPDuplicateEntry> entries = new List<PCPDuplicateEntry>();

        /// <summary>
        /// True when the user has manually chosen which entry to keep.
        /// Prevents <see cref="ElectCanonical"/> from overriding the selection.
        /// </summary>
        [NonSerialized] public bool hasUserOverride;

        /// <summary>
        /// The total bytes that could be reclaimed by removing all but the canonical copy.
        /// Returns zero if the group has fewer than two entries.
        /// </summary>
        public long WastedBytes
        {
            get
            {
                if (entries == null || entries.Count < 2)
                    return 0L;

                return (entries.Count - 1) * entries[0].sizeBytes;
            }
        }

        /// <summary>Number of duplicate copies (total entries minus one for the canonical).</summary>
        public int DuplicateCount => entries != null ? Math.Max(0, entries.Count - 1) : 0;

        /// <summary>
        /// Elects the entry with the highest reference count as canonical.
        /// Ties are broken by preferring the shortest path (closer to project root).
        /// </summary>
        public void ElectCanonical()
        {
            if (entries == null || entries.Count == 0)
                return;

            PCPDuplicateEntry best = entries[0];
            foreach (var entry in entries)
            {
                entry.isCanonical = false;

                if (entry.referenceCount > best.referenceCount)
                {
                    best = entry;
                }
                else if (entry.referenceCount == best.referenceCount &&
                         entry.path.Length < best.path.Length)
                {
                    best = entry;
                }
            }

            best.isCanonical = true;
        }

        public override string ToString()
        {
            return $"Duplicate group [{hash.Substring(0, 8)}...] - {entries.Count} copies, " +
                   $"{PCPAssetInfo.FormatBytes(WastedBytes)} wasted";
        }
    }
}
