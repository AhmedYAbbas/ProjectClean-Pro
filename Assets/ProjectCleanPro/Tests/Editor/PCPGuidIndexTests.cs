using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Tests.Editor
{
    [TestFixture]
    public class PCPGuidIndexTests
    {
        private string m_TempDir;

        [SetUp]
        public void SetUp()
        {
            m_TempDir = Path.Combine(Path.GetTempPath(), "PCPGuidIndexTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(m_TempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_TempDir))
                Directory.Delete(m_TempDir, true);
        }

        private string CreateMetaFile(string assetName, string guid)
        {
            var metaPath = Path.Combine(m_TempDir, assetName + ".meta");
            File.WriteAllText(metaPath, $"fileFormatVersion: 2\nguid: {guid}\n");
            return metaPath;
        }

        [Test]
        public async Task BuildAsync_FullBuild_IndexesAllMetas()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var meta2 = CreateMetaFile("b.mat", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1, meta2 }, null, CancellationToken.None);

            var path1 = index.Resolve("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var path2 = index.Resolve("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            Assert.IsNotNull(path1);
            Assert.IsTrue(path1.EndsWith("a.png"));
            Assert.IsNotNull(path2);
            Assert.IsTrue(path2.EndsWith("b.mat"));
        }

        [Test]
        public async Task BuildAsync_IncrementalBuild_OnlyProcessesChanged()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var meta2 = CreateMetaFile("b.mat", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1, meta2 }, null, CancellationToken.None);

            // changedFiles uses asset paths (without .meta suffix), matching production usage
            var assetPath2 = meta2.Substring(0, meta2.Length - 5); // strip ".meta"
            var changed = new HashSet<string> { assetPath2 };
            await index.BuildAsync(new List<string> { meta1, meta2 }, changed, CancellationToken.None);

            Assert.IsNotNull(index.Resolve("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1"));
            Assert.IsNotNull(index.Resolve("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        }

        [Test]
        public async Task BuildAsync_IncrementalBuild_RemovesDeletedEntries()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
            var meta2 = CreateMetaFile("b.mat", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1, meta2 }, null, CancellationToken.None);

            var changed = new HashSet<string>();
            await index.BuildAsync(new List<string> { meta1 }, changed, CancellationToken.None);

            Assert.IsNotNull(index.Resolve("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1"));
            Assert.IsNull(index.Resolve("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        }

        [Test]
        public async Task Resolve_UnknownGuid_ReturnsNull()
        {
            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string>(), null, CancellationToken.None);

            Assert.IsNull(index.Resolve("00000000000000000000000000000000"));
        }

        [Test]
        public async Task ResolveAll_ResolvesKnownGuids()
        {
            var meta1 = CreateMetaFile("a.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");

            var index = new PCPGuidIndex();
            await index.BuildAsync(new List<string> { meta1 }, null, CancellationToken.None);

            var guids = new[] { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1", "unknown_guid_here_not_existing_" };
            var resolved = index.ResolveAll(guids);

            Assert.AreEqual(1, resolved.Count);
        }
    }
}
