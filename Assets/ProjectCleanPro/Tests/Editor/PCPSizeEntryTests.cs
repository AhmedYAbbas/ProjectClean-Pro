using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPSizeEntry"/> covering formatted size, summary,
    /// factory method, and edge cases.
    /// </summary>
    [TestFixture]
    public sealed class PCPSizeEntryTests
    {
        // ================================================================
        // 1. FORMATTED SIZE
        // ================================================================

        [Test]
        public void FormattedSize_ZeroBytes_Returns0B()
        {
            var entry = new PCPSizeEntry { sizeBytes = 0 };
            Assert.AreEqual("0 B", entry.FormattedSize);
        }

        [Test]
        public void FormattedSize_Kilobytes()
        {
            var entry = new PCPSizeEntry { sizeBytes = 2048 };
            Assert.IsTrue(entry.FormattedSize.Contains("KB"));
        }

        [Test]
        public void FormattedSize_Megabytes()
        {
            var entry = new PCPSizeEntry { sizeBytes = 5 * 1024 * 1024 };
            Assert.IsTrue(entry.FormattedSize.Contains("MB"));
        }

        // ================================================================
        // 2. GET SUMMARY
        // ================================================================

        [Test]
        public void GetSummary_ContainsName()
        {
            var entry = MakeEntry("Hero", "Texture2D", 1024, 5.5f, false);
            Assert.IsTrue(entry.GetSummary().Contains("Hero"));
        }

        [Test]
        public void GetSummary_ContainsAssetType()
        {
            var entry = MakeEntry("Hero", "Texture2D", 1024, 5.5f, false);
            Assert.IsTrue(entry.GetSummary().Contains("Texture2D"));
        }

        [Test]
        public void GetSummary_ContainsPercent()
        {
            var entry = MakeEntry("Hero", "Texture2D", 1024, 15.3f, false);
            Assert.IsTrue(entry.GetSummary().Contains("15.3%"));
        }

        [Test]
        public void GetSummary_WithOptimization_ContainsSuggestion()
        {
            var entry = MakeEntry("BigTex", "Texture2D", 5_000_000, 25.0f, true);
            entry.optimizationSuggestion = "Enable crunch compression";
            string summary = entry.GetSummary();
            Assert.IsTrue(summary.Contains("Enable crunch compression"));
            Assert.IsTrue(summary.Contains("**"));
        }

        [Test]
        public void GetSummary_WithoutOptimization_NoSuggestion()
        {
            var entry = MakeEntry("SmallTex", "Texture2D", 1024, 0.1f, false);
            string summary = entry.GetSummary();
            Assert.IsFalse(summary.Contains("**"));
        }

        // ================================================================
        // 3. TOSTRING
        // ================================================================

        [Test]
        public void ToString_EqualsGetSummary()
        {
            var entry = MakeEntry("test", "Texture2D", 1024, 1.0f, false);
            Assert.AreEqual(entry.GetSummary(), entry.ToString());
        }

        // ================================================================
        // 4. FROM ASSET INFO
        // ================================================================

        [Test]
        public void FromAssetInfo_CopiesFields()
        {
            var info = new PCPAssetInfo
            {
                path = "Assets/Textures/Hero.png",
                name = "Hero",
                assetTypeName = "Texture2D",
                sizeBytes = 4096
            };

            var entry = PCPSizeEntry.FromAssetInfo(info);

            Assert.AreEqual("Assets/Textures/Hero.png", entry.path);
            Assert.AreEqual("Hero", entry.name);
            Assert.AreEqual("Texture2D", entry.assetTypeName);
            Assert.AreEqual(4096, entry.sizeBytes);
            Assert.IsNotNull(entry.folderPath);
            Assert.AreEqual(string.Empty, entry.compressionInfo);
            Assert.AreEqual(string.Empty, entry.optimizationSuggestion);
            Assert.IsFalse(entry.hasOptimizationSuggestion);
        }

        [Test]
        public void FromAssetInfo_FolderPath_UsesForwardSlashes()
        {
            var info = new PCPAssetInfo
            {
                path = "Assets/Textures/Sub/Hero.png",
                name = "Hero",
                assetTypeName = "Texture2D",
                sizeBytes = 1024
            };

            var entry = PCPSizeEntry.FromAssetInfo(info);

            Assert.IsFalse(entry.folderPath.Contains("\\"),
                "Folder path should use forward slashes");
            Assert.IsTrue(entry.folderPath.Contains("Assets/Textures/Sub") ||
                          entry.folderPath.Contains("Assets\\Textures\\Sub") == false);
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static PCPSizeEntry MakeEntry(string name, string type, long size, float percent, bool hasOpt)
        {
            return new PCPSizeEntry
            {
                path = $"Assets/{name}.png",
                name = name,
                assetTypeName = type,
                folderPath = "Assets",
                sizeBytes = size,
                percentOfTotal = percent,
                hasOptimizationSuggestion = hasOpt,
                optimizationSuggestion = hasOpt ? "Test suggestion" : "",
                compressionInfo = ""
            };
        }
    }
}
