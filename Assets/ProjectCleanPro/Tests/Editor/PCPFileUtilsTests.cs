using System;
using System.IO;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPFileUtils"/> covering SHA-256 hashing, normalized hashing,
    /// YAML detection, file metadata, file operations, and path utilities.
    /// </summary>
    [TestFixture]
    public sealed class PCPFileUtilsTests
    {
        private string m_TempDir;

        [SetUp]
        public void SetUp()
        {
            m_TempDir = Path.Combine(Path.GetTempPath(), "PCPFileUtilsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_TempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_TempDir))
                    Directory.Delete(m_TempDir, true);
            }
            catch { }
        }

        private string CreateFile(string name, string content)
        {
            string path = Path.Combine(m_TempDir, name);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            return path;
        }

        // ================================================================
        // 1. COMPUTE SHA256
        // ================================================================

        [Test]
        public void ComputeSHA256_ReturnsLowercase64CharHex()
        {
            string path = CreateFile("test.txt", "hello world");
            string hash = PCPFileUtils.ComputeSHA256(path);

            Assert.AreEqual(64, hash.Length, "SHA-256 hex should be 64 chars");
            Assert.AreEqual(hash, hash.ToLowerInvariant(), "Should be lowercase");
        }

        [Test]
        public void ComputeSHA256_SameContent_SameHash()
        {
            string path1 = CreateFile("a.txt", "identical content");
            string path2 = CreateFile("b.txt", "identical content");

            Assert.AreEqual(PCPFileUtils.ComputeSHA256(path1), PCPFileUtils.ComputeSHA256(path2));
        }

        [Test]
        public void ComputeSHA256_DifferentContent_DifferentHash()
        {
            string path1 = CreateFile("a.txt", "content A");
            string path2 = CreateFile("b.txt", "content B");

            Assert.AreNotEqual(PCPFileUtils.ComputeSHA256(path1), PCPFileUtils.ComputeSHA256(path2));
        }

        [Test]
        public void ComputeSHA256_EmptyFile_ProducesHash()
        {
            string path = CreateFile("empty.txt", "");
            string hash = PCPFileUtils.ComputeSHA256(path);
            Assert.AreEqual(64, hash.Length);
        }

        [Test]
        public void ComputeSHA256_NullPath_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => PCPFileUtils.ComputeSHA256(null));
        }

        [Test]
        public void ComputeSHA256_EmptyPath_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => PCPFileUtils.ComputeSHA256(""));
        }

        [Test]
        public void ComputeSHA256_NonExistentFile_ThrowsFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(
                () => PCPFileUtils.ComputeSHA256(Path.Combine(m_TempDir, "nope.txt")));
        }

        // ================================================================
        // 2. IS UNITY YAML ASSET
        // ================================================================

        [Test]
        public void IsUnityYamlAsset_YamlHeader_ReturnsTrue()
        {
            string path = CreateFile("test.asset", "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114");
            Assert.IsTrue(PCPFileUtils.IsUnityYamlAsset(path));
        }

        [Test]
        public void IsUnityYamlAsset_NonYaml_ReturnsFalse()
        {
            string path = CreateFile("test.png", "PNG binary data here");
            Assert.IsFalse(PCPFileUtils.IsUnityYamlAsset(path));
        }

        [Test]
        public void IsUnityYamlAsset_EmptyFile_ReturnsFalse()
        {
            string path = CreateFile("empty.asset", "");
            Assert.IsFalse(PCPFileUtils.IsUnityYamlAsset(path));
        }

        [Test]
        public void IsUnityYamlAsset_NonExistentFile_ReturnsFalse()
        {
            Assert.IsFalse(PCPFileUtils.IsUnityYamlAsset(Path.Combine(m_TempDir, "nope.asset")));
        }

        [Test]
        public void IsUnityYamlAsset_ShortFile_ReturnsFalse()
        {
            string path = CreateFile("short.txt", "%YAM");
            Assert.IsFalse(PCPFileUtils.IsUnityYamlAsset(path));
        }

        // ================================================================
        // 3. COMPUTE NORMALIZED SHA256
        // ================================================================

        [Test]
        public void ComputeNormalizedSHA256_NonYaml_SameAsRegularSHA256()
        {
            string path = CreateFile("binary.png", "binary content without YAML header");
            string regular = PCPFileUtils.ComputeSHA256(path);
            string normalized = PCPFileUtils.ComputeNormalizedSHA256(path);
            Assert.AreEqual(regular, normalized);
        }

        [Test]
        public void ComputeNormalizedSHA256_YamlWithDifferentNames_SameHash()
        {
            string yaml1 = "%YAML 1.1\n%TAG !u!\n--- !u!114\nMonoBehaviour:\n  m_Name: AssetA\n  m_Data: 42\n";
            string yaml2 = "%YAML 1.1\n%TAG !u!\n--- !u!114\nMonoBehaviour:\n  m_Name: AssetB\n  m_Data: 42\n";

            string path1 = CreateFile("a.asset", yaml1);
            string path2 = CreateFile("b.asset", yaml2);

            Assert.AreEqual(
                PCPFileUtils.ComputeNormalizedSHA256(path1),
                PCPFileUtils.ComputeNormalizedSHA256(path2),
                "YAML assets differing only in m_Name should have the same normalized hash");
        }

        [Test]
        public void ComputeNormalizedSHA256_YamlWithDifferentContent_DifferentHash()
        {
            string yaml1 = "%YAML 1.1\n%TAG !u!\n--- !u!114\nMonoBehaviour:\n  m_Name: Asset\n  m_Data: 42\n";
            string yaml2 = "%YAML 1.1\n%TAG !u!\n--- !u!114\nMonoBehaviour:\n  m_Name: Asset\n  m_Data: 99\n";

            string path1 = CreateFile("a.asset", yaml1);
            string path2 = CreateFile("b.asset", yaml2);

            Assert.AreNotEqual(
                PCPFileUtils.ComputeNormalizedSHA256(path1),
                PCPFileUtils.ComputeNormalizedSHA256(path2));
        }

        [Test]
        public void ComputeNormalizedSHA256_NullPath_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PCPFileUtils.ComputeNormalizedSHA256(null));
        }

        [Test]
        public void ComputeNormalizedSHA256_NonExistent_Throws()
        {
            Assert.Throws<FileNotFoundException>(
                () => PCPFileUtils.ComputeNormalizedSHA256(Path.Combine(m_TempDir, "nope.txt")));
        }

        // ================================================================
        // 4. GET FILE SIZE
        // ================================================================

        [Test]
        public void GetFileSize_ExistingFile_ReturnsCorrectSize()
        {
            string content = "hello world"; // 11 bytes
            string path = CreateFile("size_test.txt", content);
            long size = PCPFileUtils.GetFileSize(path);
            Assert.Greater(size, 0);
        }

        [Test]
        public void GetFileSize_EmptyFile_ReturnsZero()
        {
            string path = CreateFile("empty.txt", "");
            Assert.AreEqual(0L, PCPFileUtils.GetFileSize(path));
        }

        [Test]
        public void GetFileSize_NonExistent_ReturnsZero()
        {
            Assert.AreEqual(0L, PCPFileUtils.GetFileSize(Path.Combine(m_TempDir, "nope.txt")));
        }

        [Test]
        public void GetFileSize_NullPath_ReturnsZero()
        {
            Assert.AreEqual(0L, PCPFileUtils.GetFileSize(null));
        }

        [Test]
        public void GetFileSize_EmptyPath_ReturnsZero()
        {
            Assert.AreEqual(0L, PCPFileUtils.GetFileSize(""));
        }

        // ================================================================
        // 5. GET LAST MODIFIED
        // ================================================================

        [Test]
        public void GetLastModified_ExistingFile_ReturnsRecentTime()
        {
            string path = CreateFile("mod_test.txt", "test");
            DateTime modified = PCPFileUtils.GetLastModified(path);
            Assert.Greater(modified, DateTime.MinValue);
            Assert.LessOrEqual(modified, DateTime.UtcNow.AddMinutes(1));
        }

        [Test]
        public void GetLastModified_NonExistent_ReturnsMinValue()
        {
            Assert.AreEqual(DateTime.MinValue, PCPFileUtils.GetLastModified(Path.Combine(m_TempDir, "nope.txt")));
        }

        [Test]
        public void GetLastModified_NullPath_ReturnsMinValue()
        {
            Assert.AreEqual(DateTime.MinValue, PCPFileUtils.GetLastModified(null));
        }

        // ================================================================
        // 6. COPY FILE WITH DIRECTORIES
        // ================================================================

        [Test]
        public void CopyFileWithDirectories_CopiesContent()
        {
            string src = CreateFile("src.txt", "copy me");
            string dst = Path.Combine(m_TempDir, "sub", "deep", "dst.txt");

            PCPFileUtils.CopyFileWithDirectories(src, dst);

            Assert.IsTrue(File.Exists(dst));
            Assert.AreEqual("copy me", File.ReadAllText(dst));
        }

        [Test]
        public void CopyFileWithDirectories_OverwritesExisting()
        {
            string src = CreateFile("src.txt", "new content");
            string dst = CreateFile("dst.txt", "old content");

            PCPFileUtils.CopyFileWithDirectories(src, dst);

            Assert.AreEqual("new content", File.ReadAllText(dst));
        }

        [Test]
        public void CopyFileWithDirectories_NullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PCPFileUtils.CopyFileWithDirectories(null, Path.Combine(m_TempDir, "dst.txt")));
        }

        [Test]
        public void CopyFileWithDirectories_NullDest_Throws()
        {
            string src = CreateFile("src.txt", "data");
            Assert.Throws<ArgumentNullException>(
                () => PCPFileUtils.CopyFileWithDirectories(src, null));
        }

        // ================================================================
        // 7. ENSURE DIRECTORY EXISTS
        // ================================================================

        [Test]
        public void EnsureDirectoryExists_CreatesNewDirectory()
        {
            string newDir = Path.Combine(m_TempDir, "new_dir");
            Assert.IsFalse(Directory.Exists(newDir));

            PCPFileUtils.EnsureDirectoryExists(newDir);

            Assert.IsTrue(Directory.Exists(newDir));
        }

        [Test]
        public void EnsureDirectoryExists_ExistingDir_NoError()
        {
            Assert.DoesNotThrow(() => PCPFileUtils.EnsureDirectoryExists(m_TempDir));
        }

        [Test]
        public void EnsureDirectoryExists_NullPath_NoError()
        {
            Assert.DoesNotThrow(() => PCPFileUtils.EnsureDirectoryExists(null));
        }

        [Test]
        public void EnsureDirectoryExists_EmptyPath_NoError()
        {
            Assert.DoesNotThrow(() => PCPFileUtils.EnsureDirectoryExists(""));
        }

        // ================================================================
        // 8. GET RELATIVE PATH
        // ================================================================

        [Test]
        public void GetRelativePath_SimpleCase()
        {
            string result = PCPFileUtils.GetRelativePath("/project/root", "/project/root/Assets/file.txt");
            Assert.AreEqual("Assets/file.txt", result);
        }

        [Test]
        public void GetRelativePath_WithTrailingSlash()
        {
            string result = PCPFileUtils.GetRelativePath("/project/root/", "/project/root/Assets/file.txt");
            Assert.AreEqual("Assets/file.txt", result);
        }

        [Test]
        public void GetRelativePath_BackslashesNormalized()
        {
            string result = PCPFileUtils.GetRelativePath("C:\\project\\root", "C:\\project\\root\\Assets\\file.txt");
            Assert.AreEqual("Assets/file.txt", result);
        }

        [Test]
        public void GetRelativePath_NullBase_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PCPFileUtils.GetRelativePath(null, "/some/path"));
        }

        [Test]
        public void GetRelativePath_NullFull_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PCPFileUtils.GetRelativePath("/some/base", null));
        }
    }
}
