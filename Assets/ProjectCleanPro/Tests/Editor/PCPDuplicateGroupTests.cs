using System.Collections.Generic;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPDuplicateGroup"/> and <see cref="PCPDuplicateEntry"/>
    /// covering canonical election, wasted bytes, duplicate count, and edge cases.
    /// </summary>
    [TestFixture]
    public sealed class PCPDuplicateGroupTests
    {
        // ================================================================
        // 1. WASTED BYTES
        // ================================================================

        [Test]
        public void WastedBytes_TwoEntries_ReturnsOneCopySize()
        {
            var group = MakeGroup(2, sizeBytes: 1024);
            Assert.AreEqual(1024L, group.WastedBytes);
        }

        [Test]
        public void WastedBytes_ThreeEntries_ReturnsTwoCopiesSizes()
        {
            var group = MakeGroup(3, sizeBytes: 500);
            Assert.AreEqual(1000L, group.WastedBytes);
        }

        [Test]
        public void WastedBytes_SingleEntry_ReturnsZero()
        {
            var group = MakeGroup(1, sizeBytes: 1024);
            Assert.AreEqual(0L, group.WastedBytes);
        }

        [Test]
        public void WastedBytes_EmptyEntries_ReturnsZero()
        {
            var group = new PCPDuplicateGroup { hash = "abc", entries = new List<PCPDuplicateEntry>() };
            Assert.AreEqual(0L, group.WastedBytes);
        }

        [Test]
        public void WastedBytes_NullEntries_ReturnsZero()
        {
            var group = new PCPDuplicateGroup { hash = "abc", entries = null };
            Assert.AreEqual(0L, group.WastedBytes);
        }

        [Test]
        public void WastedBytes_LargeFileSize()
        {
            var group = MakeGroup(5, sizeBytes: 10_000_000L);
            // (5-1) * 10 MB = 40 MB
            Assert.AreEqual(40_000_000L, group.WastedBytes);
        }

        // ================================================================
        // 2. DUPLICATE COUNT
        // ================================================================

        [Test]
        public void DuplicateCount_TwoEntries_ReturnsOne()
        {
            var group = MakeGroup(2, sizeBytes: 100);
            Assert.AreEqual(1, group.DuplicateCount);
        }

        [Test]
        public void DuplicateCount_FiveEntries_ReturnsFour()
        {
            var group = MakeGroup(5, sizeBytes: 100);
            Assert.AreEqual(4, group.DuplicateCount);
        }

        [Test]
        public void DuplicateCount_SingleEntry_ReturnsZero()
        {
            var group = MakeGroup(1, sizeBytes: 100);
            Assert.AreEqual(0, group.DuplicateCount);
        }

        [Test]
        public void DuplicateCount_EmptyEntries_ReturnsZero()
        {
            var group = new PCPDuplicateGroup { entries = new List<PCPDuplicateEntry>() };
            Assert.AreEqual(0, group.DuplicateCount);
        }

        [Test]
        public void DuplicateCount_NullEntries_ReturnsZero()
        {
            var group = new PCPDuplicateGroup { entries = null };
            Assert.AreEqual(0, group.DuplicateCount);
        }

        // ================================================================
        // 3. ELECT CANONICAL - BY REFERENCE COUNT
        // ================================================================

        [Test]
        public void ElectCanonical_HighestRefCount_BecomesCanonical()
        {
            var group = new PCPDuplicateGroup
            {
                hash = "abc",
                entries = new List<PCPDuplicateEntry>
                {
                    MakeEntry("Assets/a.png", refs: 1),
                    MakeEntry("Assets/b.png", refs: 5),
                    MakeEntry("Assets/c.png", refs: 3),
                }
            };

            group.ElectCanonical();

            Assert.IsFalse(group.entries[0].isCanonical, "a should not be canonical");
            Assert.IsTrue(group.entries[1].isCanonical, "b should be canonical (highest refs)");
            Assert.IsFalse(group.entries[2].isCanonical, "c should not be canonical");
        }

        [Test]
        public void ElectCanonical_TiedRefCount_ShortestPathWins()
        {
            var group = new PCPDuplicateGroup
            {
                hash = "abc",
                entries = new List<PCPDuplicateEntry>
                {
                    MakeEntry("Assets/VeryLongPath/SubFolder/texture.png", refs: 3),
                    MakeEntry("Assets/tex.png", refs: 3),
                    MakeEntry("Assets/Medium/texture.png", refs: 3),
                }
            };

            group.ElectCanonical();

            Assert.IsFalse(group.entries[0].isCanonical, "long path should not be canonical");
            Assert.IsTrue(group.entries[1].isCanonical, "shortest path should be canonical");
            Assert.IsFalse(group.entries[2].isCanonical, "medium path should not be canonical");
        }

        [Test]
        public void ElectCanonical_ClearsAllCanonicalFirst()
        {
            var group = new PCPDuplicateGroup
            {
                hash = "abc",
                entries = new List<PCPDuplicateEntry>
                {
                    MakeEntry("Assets/a.png", refs: 1, canonical: true),
                    MakeEntry("Assets/b.png", refs: 5, canonical: true),
                }
            };

            group.ElectCanonical();

            // Only one should be canonical.
            int canonicalCount = 0;
            foreach (var e in group.entries)
                if (e.isCanonical) canonicalCount++;

            Assert.AreEqual(1, canonicalCount);
            Assert.IsTrue(group.entries[1].isCanonical);
        }

        [Test]
        public void ElectCanonical_SingleEntry_BecomesCanonical()
        {
            var group = new PCPDuplicateGroup
            {
                hash = "abc",
                entries = new List<PCPDuplicateEntry>
                {
                    MakeEntry("Assets/a.png", refs: 0),
                }
            };

            group.ElectCanonical();

            Assert.IsTrue(group.entries[0].isCanonical);
        }

        [Test]
        public void ElectCanonical_EmptyEntries_DoesNotThrow()
        {
            var group = new PCPDuplicateGroup { entries = new List<PCPDuplicateEntry>() };
            Assert.DoesNotThrow(() => group.ElectCanonical());
        }

        [Test]
        public void ElectCanonical_NullEntries_DoesNotThrow()
        {
            var group = new PCPDuplicateGroup { entries = null };
            Assert.DoesNotThrow(() => group.ElectCanonical());
        }

        [Test]
        public void ElectCanonical_AllZeroRefs_ShortestPathWins()
        {
            var group = new PCPDuplicateGroup
            {
                hash = "abc",
                entries = new List<PCPDuplicateEntry>
                {
                    MakeEntry("Assets/Long/Path/asset.png", refs: 0),
                    MakeEntry("Assets/a.png", refs: 0),
                }
            };

            group.ElectCanonical();

            Assert.IsTrue(group.entries[1].isCanonical, "shortest path should win when all refs are 0");
        }

        // ================================================================
        // 4. TOSTRING
        // ================================================================

        [Test]
        public void DuplicateGroup_ToString_ContainsHashPrefix()
        {
            var group = new PCPDuplicateGroup
            {
                hash = "abcdef1234567890",
                entries = new List<PCPDuplicateEntry>
                {
                    MakeEntry("Assets/a.png", refs: 0),
                    MakeEntry("Assets/b.png", refs: 0),
                }
            };

            string result = group.ToString();
            Assert.IsTrue(result.Contains("abcdef12"), "Should contain first 8 chars of hash");
            Assert.IsTrue(result.Contains("2 copies"), "Should contain entry count");
        }

        [Test]
        public void DuplicateEntry_ToString_IncludesPath()
        {
            var entry = MakeEntry("Assets/Textures/bg.png", refs: 3);
            string result = entry.ToString();
            Assert.IsTrue(result.Contains("Assets/Textures/bg.png"));
            Assert.IsTrue(result.Contains("3 refs"));
        }

        [Test]
        public void DuplicateEntry_Canonical_ToString_HasTag()
        {
            var entry = MakeEntry("Assets/a.png", refs: 0, canonical: true);
            Assert.IsTrue(entry.ToString().Contains("[canonical]"));
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static PCPDuplicateGroup MakeGroup(int entryCount, long sizeBytes)
        {
            var group = new PCPDuplicateGroup
            {
                hash = "testhash1234567890abcdef",
                entries = new List<PCPDuplicateEntry>()
            };

            for (int i = 0; i < entryCount; i++)
            {
                group.entries.Add(new PCPDuplicateEntry
                {
                    path = $"Assets/file_{i}.png",
                    guid = $"guid_{i}",
                    sizeBytes = sizeBytes,
                    referenceCount = 0,
                    isCanonical = false
                });
            }

            return group;
        }

        private static PCPDuplicateEntry MakeEntry(string path, int refs, bool canonical = false)
        {
            return new PCPDuplicateEntry
            {
                path = path,
                guid = "guid_" + path.GetHashCode(),
                sizeBytes = 1024,
                referenceCount = refs,
                isCanonical = canonical
            };
        }
    }
}
