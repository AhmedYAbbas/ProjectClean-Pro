using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Tests.Editor
{
    [TestFixture]
    public class PCPGuidParserTests
    {
        private string m_TempDir;

        [SetUp]
        public void SetUp()
        {
            m_TempDir = Path.Combine(Path.GetTempPath(), "PCPGuidParserTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(m_TempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_TempDir))
                Directory.Delete(m_TempDir, true);
        }

        [Test]
        public void ParseReferences_FindsGuidsInYaml()
        {
            var content = @"--- !u!114 &123
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: e4f18583b7a683c4b9db3b1f46a8b93a, type: 3}
  m_Material: {fileID: 2100000, guid: c22c5a2f3fa1e0947a1e82e283a6b70c, type: 2}";

            var filePath = Path.Combine(m_TempDir, "test.prefab");
            File.WriteAllText(filePath, content);

            var guids = PCPGuidParser.ParseReferences(filePath, CancellationToken.None);

            Assert.AreEqual(2, guids.Count);
            Assert.IsTrue(guids.Contains("e4f18583b7a683c4b9db3b1f46a8b93a"));
            Assert.IsTrue(guids.Contains("c22c5a2f3fa1e0947a1e82e283a6b70c"));
        }

        [Test]
        public void ParseReferences_DeduplicatesGuids()
        {
            var content = @"  m_A: {fileID: 0, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1, type: 2}
  m_B: {fileID: 0, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1, type: 2}";

            var filePath = Path.Combine(m_TempDir, "test.prefab");
            File.WriteAllText(filePath, content);

            var guids = PCPGuidParser.ParseReferences(filePath, CancellationToken.None);

            Assert.AreEqual(1, guids.Count);
        }

        [Test]
        public void ParseReferences_EmptyFile_ReturnsEmpty()
        {
            var filePath = Path.Combine(m_TempDir, "empty.prefab");
            File.WriteAllText(filePath, "");

            var guids = PCPGuidParser.ParseReferences(filePath, CancellationToken.None);

            Assert.AreEqual(0, guids.Count);
        }

        [Test]
        public void ParseReferences_IgnoresInvalidHex()
        {
            var content = "  m_A: {fileID: 0, guid: not_a_valid_hex_string_here!!, type: 2}";

            var filePath = Path.Combine(m_TempDir, "test.prefab");
            File.WriteAllText(filePath, content);

            var guids = PCPGuidParser.ParseReferences(filePath, CancellationToken.None);

            Assert.AreEqual(0, guids.Count);
        }

        [Test]
        public void IsGuidParseable_ReturnsTrueForYamlAssets()
        {
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".prefab"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".unity"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".asset"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".mat"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".controller"));
            Assert.IsTrue(PCPGuidParser.IsGuidParseable(".anim"));
        }

        [Test]
        public void IsGuidParseable_ReturnsFalseForBinaryAssets()
        {
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".png"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".fbx"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".wav"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".cs"));
            Assert.IsFalse(PCPGuidParser.IsGuidParseable(".dll"));
        }
    }
}
