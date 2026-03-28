using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Comprehensive tests for <see cref="PCPScanCache"/>.
    /// Covers: hash storage, dependency storage, file size storage, generic metadata,
    /// staleness checks (including meta-file staleness), cache versioning,
    /// persistence (save/load round-trip), bulk operations, and edge cases.
    /// </summary>
    [TestFixture]
    public sealed class PCPScanCacheTests
    {
        // ----------------------------------------------------------------
        // Test infrastructure
        // ----------------------------------------------------------------

        private PCPScanCache m_Cache;
        private string m_TempDir;
        private readonly List<string> m_TempFiles = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Cache = new PCPScanCache();
            m_TempDir = Path.Combine(Path.GetTempPath(), "PCPScanCacheTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_TempDir);
        }

        [TearDown]
        public void TearDown()
        {
            m_TempFiles.Clear();

            // Clean up temp directory.
            try
            {
                if (Directory.Exists(m_TempDir))
                    Directory.Delete(m_TempDir, recursive: true);
            }
            catch (Exception)
            {
                // Best-effort cleanup.
            }
        }

        /// <summary>
        /// Creates a temporary file with given content and returns its path
        /// relative to a fake project root (just the filename, since we use
        /// full-path overrides via GetFullPath).
        /// For staleness tests we need real files, so we return the full path.
        /// </summary>
        private string CreateTempFile(string name, string content = "hello")
        {
            string path = Path.Combine(m_TempDir, name);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            m_TempFiles.Add(path);
            return path;
        }

        // ================================================================
        // 1. HASH STORAGE
        // ================================================================

        [Test]
        public void GetHash_ReturnsNull_WhenNothingCached()
        {
            Assert.IsNull(m_Cache.GetHash("Assets/missing.png"));
        }

        [Test]
        public void SetHash_ThenGetHash_RoundTrips()
        {
            string path = CreateTempFile("test.png");
            string hash = "abc123def456";

            m_Cache.SetHash(path, hash);
            Assert.AreEqual(hash, m_Cache.GetHash(path));
        }

        [Test]
        public void SetHash_Overwrites_PreviousHash()
        {
            string path = CreateTempFile("test.png");

            m_Cache.SetHash(path, "old_hash");
            m_Cache.SetHash(path, "new_hash");

            Assert.AreEqual("new_hash", m_Cache.GetHash(path));
        }

        [Test]
        public void SetHash_UpdatesTimestamp()
        {
            string path = CreateTempFile("test.png");

            m_Cache.SetHash(path, "hash1");

            // After setting the hash, the entry should not be stale.
            Assert.IsFalse(m_Cache.IsStale(path));
        }

        // ================================================================
        // 2. FILE SIZE STORAGE
        // ================================================================

        [Test]
        public void GetFileSize_ReturnsNegativeOne_WhenNothingCached()
        {
            Assert.AreEqual(-1, m_Cache.GetFileSize("Assets/missing.png"));
        }

        [Test]
        public void SetFileSize_ThenGetFileSize_RoundTrips()
        {
            string path = CreateTempFile("texture.png");
            long size = 1048576L; // 1 MB

            m_Cache.SetFileSize(path, size);
            Assert.AreEqual(size, m_Cache.GetFileSize(path));
        }

        [Test]
        public void SetFileSize_Overwrites_PreviousSize()
        {
            string path = CreateTempFile("texture.png");

            m_Cache.SetFileSize(path, 100L);
            m_Cache.SetFileSize(path, 200L);

            Assert.AreEqual(200L, m_Cache.GetFileSize(path));
        }

        [Test]
        public void GetFileSize_ReturnsNegativeOne_ForZeroSize()
        {
            // fileSizeBytes == 0 is treated as "not cached" by the getter.
            string path = CreateTempFile("empty.txt", "");
            m_Cache.SetFileSize(path, 0L);

            Assert.AreEqual(-1, m_Cache.GetFileSize(path));
        }

        [Test]
        public void SetFileSize_UpdatesTimestamp()
        {
            string path = CreateTempFile("data.bin");

            m_Cache.SetFileSize(path, 42L);
            Assert.IsFalse(m_Cache.IsStale(path));
        }

        // ================================================================
        // 3. DEPENDENCY STORAGE
        // ================================================================

        [Test]
        public void GetDependencies_ReturnsNull_WhenNothingCached()
        {
            Assert.IsNull(m_Cache.GetDependencies("Assets/missing.prefab"));
        }

        [Test]
        public void SetDependencies_ThenGetDependencies_RoundTrips()
        {
            string path = CreateTempFile("prefab.prefab");
            string[] deps = { "Assets/mat.mat", "Assets/tex.png" };

            m_Cache.SetDependencies(path, deps);
            string[] result = m_Cache.GetDependencies(path);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("Assets/mat.mat", result[0]);
            Assert.AreEqual("Assets/tex.png", result[1]);
        }

        [Test]
        public void SetDependencies_Overwrites_PreviousDeps()
        {
            string path = CreateTempFile("scene.unity");

            m_Cache.SetDependencies(path, new[] { "a", "b" });
            m_Cache.SetDependencies(path, new[] { "x" });

            string[] result = m_Cache.GetDependencies(path);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("x", result[0]);
        }

        [Test]
        public void SetDependencies_EmptyArray_IsPreserved()
        {
            string path = CreateTempFile("isolated.asset");
            m_Cache.SetDependencies(path, Array.Empty<string>());

            string[] result = m_Cache.GetDependencies(path);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void SetDependencies_UpdatesTimestamp()
        {
            string path = CreateTempFile("go.prefab");
            m_Cache.SetDependencies(path, new[] { "dep" });
            Assert.IsFalse(m_Cache.IsStale(path));
        }

        // ================================================================
        // 4. GENERIC METADATA
        // ================================================================

        [Test]
        public void GetMetadata_ReturnsNull_WhenNothingCached()
        {
            Assert.IsNull(m_Cache.GetMetadata("Assets/foo.cs", "packages.usings"));
        }

        [Test]
        public void GetMetadata_ReturnsNull_ForUnknownKey()
        {
            string path = CreateTempFile("file.cs");
            m_Cache.SetMetadata(path, "key1", "val1");

            Assert.IsNull(m_Cache.GetMetadata(path, "key2"));
        }

        [Test]
        public void SetMetadata_ThenGetMetadata_RoundTrips()
        {
            string path = CreateTempFile("shader.shader");

            m_Cache.SetMetadata(path, "shader.keywords", "FOG,SHADOWS");
            Assert.AreEqual("FOG,SHADOWS", m_Cache.GetMetadata(path, "shader.keywords"));
        }

        [Test]
        public void SetMetadata_Overwrites_ExistingKey()
        {
            string path = CreateTempFile("shader.shader");

            m_Cache.SetMetadata(path, "shader.passCount", "3");
            m_Cache.SetMetadata(path, "shader.passCount", "5");

            Assert.AreEqual("5", m_Cache.GetMetadata(path, "shader.passCount"));
        }

        [Test]
        public void SetMetadata_MultipleKeys_SameAsset()
        {
            string path = CreateTempFile("shader.shader");

            m_Cache.SetMetadata(path, "shader.keywords", "A,B");
            m_Cache.SetMetadata(path, "shader.passCount", "2");
            m_Cache.SetMetadata(path, "shader.variants", "16");

            Assert.AreEqual("A,B", m_Cache.GetMetadata(path, "shader.keywords"));
            Assert.AreEqual("2", m_Cache.GetMetadata(path, "shader.passCount"));
            Assert.AreEqual("16", m_Cache.GetMetadata(path, "shader.variants"));
        }

        [Test]
        public void SetMetadata_EmptyString_IsPreserved()
        {
            string path = CreateTempFile("file.cs");
            m_Cache.SetMetadata(path, "packages.usings", "");

            Assert.AreEqual("", m_Cache.GetMetadata(path, "packages.usings"));
        }

        [Test]
        public void RemoveMetadata_RemovesKey()
        {
            string path = CreateTempFile("file.cs");
            m_Cache.SetMetadata(path, "key1", "val1");
            m_Cache.SetMetadata(path, "key2", "val2");

            m_Cache.RemoveMetadata(path, "key1");

            Assert.IsNull(m_Cache.GetMetadata(path, "key1"));
            Assert.AreEqual("val2", m_Cache.GetMetadata(path, "key2"));
        }

        [Test]
        public void RemoveMetadata_DoesNothing_ForMissingKey()
        {
            string path = CreateTempFile("file.cs");
            m_Cache.SetMetadata(path, "key1", "val1");

            // Should not throw.
            m_Cache.RemoveMetadata(path, "nonexistent");
            Assert.AreEqual("val1", m_Cache.GetMetadata(path, "key1"));
        }

        [Test]
        public void RemoveMetadata_DoesNothing_ForMissingEntry()
        {
            // Should not throw when asset doesn't exist in cache.
            m_Cache.RemoveMetadata("Assets/nonexistent.cs", "key");
        }

        [Test]
        public void Metadata_IndependentAcrossAssets()
        {
            string path1 = CreateTempFile("a.cs");
            string path2 = CreateTempFile("b.cs");

            m_Cache.SetMetadata(path1, "missing.count", "0");
            m_Cache.SetMetadata(path2, "missing.count", "3");

            Assert.AreEqual("0", m_Cache.GetMetadata(path1, "missing.count"));
            Assert.AreEqual("3", m_Cache.GetMetadata(path2, "missing.count"));
        }

        // ================================================================
        // 5. STALENESS CHECKS
        // ================================================================

        [Test]
        public void IsStale_ReturnsTrue_ForNullPath()
        {
            Assert.IsTrue(m_Cache.IsStale(null));
        }

        [Test]
        public void IsStale_ReturnsTrue_ForEmptyPath()
        {
            Assert.IsTrue(m_Cache.IsStale(""));
        }

        [Test]
        public void IsStale_ReturnsTrue_WhenNoCacheEntry()
        {
            Assert.IsTrue(m_Cache.IsStale("Assets/uncached.png"));
        }

        [Test]
        public void IsStale_ReturnsFalse_AfterSetHash()
        {
            string path = CreateTempFile("fresh.png");
            m_Cache.SetHash(path, "abc");

            Assert.IsFalse(m_Cache.IsStale(path));
        }

        [Test]
        public void IsStale_ReturnsTrue_AfterFileModified()
        {
            string path = CreateTempFile("data.txt", "original");
            m_Cache.SetHash(path, "hash1");

            Assert.IsFalse(m_Cache.IsStale(path));

            // Modify the file — ensure different timestamp.
            System.Threading.Thread.Sleep(50);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));

            Assert.IsTrue(m_Cache.IsStale(path));
        }

        [Test]
        public void IsStale_ReturnsTrue_WhenFileDeleted()
        {
            string path = CreateTempFile("ephemeral.txt");
            m_Cache.SetHash(path, "h");

            File.Delete(path);

            Assert.IsTrue(m_Cache.IsStale(path));
        }

        // ================================================================
        // 6. META-FILE STALENESS (IsStaleOrMetaStale)
        // ================================================================

        [Test]
        public void IsStaleOrMetaStale_ReturnsTrue_WhenAssetIsStale()
        {
            // If the asset itself is stale, the combined check should also be true.
            Assert.IsTrue(m_Cache.IsStaleOrMetaStale("Assets/uncached.png"));
        }

        [Test]
        public void IsStaleOrMetaStale_ReturnsFalse_AfterStampMeta()
        {
            string path = CreateTempFile("texture.png");
            string metaPath = path + ".meta";
            File.WriteAllText(metaPath, "fileFormatVersion: 2");

            m_Cache.SetHash(path, "h");
            m_Cache.StampMeta(path);

            Assert.IsFalse(m_Cache.IsStaleOrMetaStale(path));
        }

        [Test]
        public void IsStaleOrMetaStale_ReturnsTrue_AfterMetaModified()
        {
            string path = CreateTempFile("texture.png");
            string metaPath = path + ".meta";
            File.WriteAllText(metaPath, "fileFormatVersion: 2");

            m_Cache.SetHash(path, "h");
            m_Cache.StampMeta(path);

            Assert.IsFalse(m_Cache.IsStaleOrMetaStale(path));

            // Modify the meta file.
            System.Threading.Thread.Sleep(50);
            File.SetLastWriteTimeUtc(metaPath, DateTime.UtcNow.AddSeconds(2));

            Assert.IsTrue(m_Cache.IsStaleOrMetaStale(path));
        }

        [Test]
        public void IsStaleOrMetaStale_ReturnsFalse_WhenNoMetaFile()
        {
            string path = CreateTempFile("no_meta.txt");
            m_Cache.SetHash(path, "h");

            // No .meta file exists. Should not be stale on meta account.
            Assert.IsFalse(m_Cache.IsStaleOrMetaStale(path));
        }

        [Test]
        public void IsStaleOrMetaStale_ReturnsTrue_WhenMetaNotStamped()
        {
            string path = CreateTempFile("texture.png");
            string metaPath = path + ".meta";
            File.WriteAllText(metaPath, "fileFormatVersion: 2");

            m_Cache.SetHash(path, "h");
            // Do NOT call StampMeta — meta ticks not stored.

            Assert.IsTrue(m_Cache.IsStaleOrMetaStale(path));
        }

        [Test]
        public void StampMeta_DoesNothing_WhenNoMetaFile()
        {
            string path = CreateTempFile("no_meta.txt");
            m_Cache.SetHash(path, "h");

            // Should not throw or create metadata.
            m_Cache.StampMeta(path);

            // The internal metadata key should not exist.
            Assert.IsNull(m_Cache.GetMetadata(path, "cache.metaTicks"));
        }

        // ================================================================
        // 7. BULK OPERATIONS
        // ================================================================

        [Test]
        public void RemoveEntry_RemovesAllDataForAsset()
        {
            string path = CreateTempFile("doomed.png");
            m_Cache.SetHash(path, "abc");
            m_Cache.SetFileSize(path, 100);
            m_Cache.SetDependencies(path, new[] { "dep" });
            m_Cache.SetMetadata(path, "key", "val");

            m_Cache.RemoveEntry(path);

            Assert.IsNull(m_Cache.GetHash(path));
            Assert.AreEqual(-1, m_Cache.GetFileSize(path));
            Assert.IsNull(m_Cache.GetDependencies(path));
            Assert.IsNull(m_Cache.GetMetadata(path, "key"));
            Assert.IsTrue(m_Cache.IsStale(path));
        }

        [Test]
        public void RemoveEntry_DoesNothing_ForMissingEntry()
        {
            // Should not throw.
            m_Cache.RemoveEntry("Assets/never_cached.png");
            Assert.AreEqual(0, m_Cache.Count);
        }

        [Test]
        public void GetAllCachedPaths_ReturnsEmpty_WhenCacheEmpty()
        {
            var paths = m_Cache.GetAllCachedPaths().ToList();
            Assert.AreEqual(0, paths.Count);
        }

        [Test]
        public void GetAllCachedPaths_ReturnsAllCachedAssets()
        {
            string path1 = CreateTempFile("a.png");
            string path2 = CreateTempFile("b.mat");
            string path3 = CreateTempFile("c.shader");

            m_Cache.SetHash(path1, "h1");
            m_Cache.SetFileSize(path2, 50);
            m_Cache.SetMetadata(path3, "k", "v");

            var paths = new HashSet<string>(m_Cache.GetAllCachedPaths());
            Assert.AreEqual(3, paths.Count);
            Assert.IsTrue(paths.Contains(path1));
            Assert.IsTrue(paths.Contains(path2));
            Assert.IsTrue(paths.Contains(path3));
        }

        [Test]
        public void Count_ReflectsNumberOfEntries()
        {
            Assert.AreEqual(0, m_Cache.Count);

            string p1 = CreateTempFile("a.png");
            string p2 = CreateTempFile("b.png");

            m_Cache.SetHash(p1, "h");
            Assert.AreEqual(1, m_Cache.Count);

            m_Cache.SetFileSize(p2, 10);
            Assert.AreEqual(2, m_Cache.Count);

            // Setting another field on existing entry should not increase count.
            m_Cache.SetMetadata(p1, "k", "v");
            Assert.AreEqual(2, m_Cache.Count);
        }

        // ================================================================
        // 8. CLEAR
        // ================================================================

        [Test]
        public void Clear_RemovesAllEntries()
        {
            string path = CreateTempFile("test.png");
            m_Cache.SetHash(path, "h");
            m_Cache.SetFileSize(path, 100);

            m_Cache.Clear();

            Assert.AreEqual(0, m_Cache.Count);
            Assert.IsNull(m_Cache.GetHash(path));
            Assert.AreEqual(-1, m_Cache.GetFileSize(path));
        }

        // ================================================================
        // 9. PERSISTENCE (SAVE/LOAD ROUND-TRIP)
        // ================================================================

        [Test]
        public void SaveAndLoad_PreservesHashes()
        {
            string path = CreateTempFile("persist.png");
            m_Cache.SetHash(path, "abc123");

            m_Cache.Save();

            var loaded = new PCPScanCache();
            loaded.Load();

            Assert.AreEqual("abc123", loaded.GetHash(path));
        }

        [Test]
        public void SaveAndLoad_PreservesFileSize()
        {
            string path = CreateTempFile("big.bin");
            m_Cache.SetFileSize(path, 999999L);

            m_Cache.Save();

            var loaded = new PCPScanCache();
            loaded.Load();

            Assert.AreEqual(999999L, loaded.GetFileSize(path));
        }

        [Test]
        public void SaveAndLoad_PreservesDependencies()
        {
            string path = CreateTempFile("scene.unity");
            string[] deps = { "Assets/a.mat", "Assets/b.tex" };
            m_Cache.SetDependencies(path, deps);

            m_Cache.Save();

            var loaded = new PCPScanCache();
            loaded.Load();

            string[] result = loaded.GetDependencies(path);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("Assets/a.mat", result[0]);
            Assert.AreEqual("Assets/b.tex", result[1]);
        }

        [Test]
        public void SaveAndLoad_PreservesMetadata()
        {
            string path = CreateTempFile("shader.shader");
            m_Cache.SetMetadata(path, "shader.keywords", "FOG,SHADOWS");
            m_Cache.SetMetadata(path, "shader.passCount", "4");

            m_Cache.Save();

            var loaded = new PCPScanCache();
            loaded.Load();

            Assert.AreEqual("FOG,SHADOWS", loaded.GetMetadata(path, "shader.keywords"));
            Assert.AreEqual("4", loaded.GetMetadata(path, "shader.passCount"));
        }

        [Test]
        public void SaveAndLoad_PreservesTimestamp_SoNotStale()
        {
            string path = CreateTempFile("stable.txt");
            m_Cache.SetHash(path, "h");

            Assert.IsFalse(m_Cache.IsStale(path));

            m_Cache.Save();

            var loaded = new PCPScanCache();
            loaded.Load();

            // File hasn't changed, so loaded cache should agree it's not stale.
            Assert.IsFalse(loaded.IsStale(path));
        }

        [Test]
        public void SaveAndLoad_MultipleEntries()
        {
            string p1 = CreateTempFile("a.png");
            string p2 = CreateTempFile("b.mat");
            string p3 = CreateTempFile("c.cs");

            m_Cache.SetHash(p1, "h1");
            m_Cache.SetFileSize(p2, 42);
            m_Cache.SetMetadata(p3, "packages.usings", "UnityEngine,System");

            m_Cache.Save();

            var loaded = new PCPScanCache();
            loaded.Load();

            Assert.AreEqual(3, loaded.Count);
            Assert.AreEqual("h1", loaded.GetHash(p1));
            Assert.AreEqual(42, loaded.GetFileSize(p2));
            Assert.AreEqual("UnityEngine,System", loaded.GetMetadata(p3, "packages.usings"));
        }

        [Test]
        public void Load_HandlesNonexistentFile()
        {
            // Loading when no file exists should result in an empty cache.
            var fresh = new PCPScanCache();
            fresh.Load();

            Assert.AreEqual(0, fresh.Count);
        }

        // ================================================================
        // 10. CACHE VERSIONING
        // ================================================================

        [Test]
        public void CurrentVersion_IsPositive()
        {
            Assert.Greater(PCPScanCache.CurrentVersion, 0);
        }

        [Test]
        public void Load_ClearsCache_WhenVersionMismatch()
        {
            // Save a valid cache.
            string path = CreateTempFile("versioned.png");
            m_Cache.SetHash(path, "h");
            m_Cache.Save();

            // Tamper with the version in the file.
            string cacheFilePath = Path.Combine(PCPScanCache.CacheDirectory, "ScanCache.json");
            if (File.Exists(cacheFilePath))
            {
                string json = File.ReadAllText(cacheFilePath);
                // Replace the current version with a bogus one.
                json = json.Replace(
                    $"\"version\":{PCPScanCache.CurrentVersion}",
                    "\"version\":99999");
                File.WriteAllText(cacheFilePath, json);
            }

            var loaded = new PCPScanCache();
            loaded.Load();

            // Should have cleared due to version mismatch.
            Assert.AreEqual(0, loaded.Count);
        }

        // ================================================================
        // 11. COMPUTE FILE HASH
        // ================================================================

        [Test]
        public void ComputeFileHash_ReturnsConsistentHash()
        {
            string path = CreateTempFile("hashme.txt", "hello world");
            string hash1 = PCPScanCache.ComputeFileHash(path);
            string hash2 = PCPScanCache.ComputeFileHash(path);

            Assert.IsNotNull(hash1);
            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void ComputeFileHash_ReturnsNull_ForMissingFile()
        {
            string result = PCPScanCache.ComputeFileHash(
                Path.Combine(m_TempDir, "nonexistent.bin"));
            Assert.IsNull(result);
        }

        [Test]
        public void ComputeFileHash_DifferentContent_DifferentHash()
        {
            string path1 = CreateTempFile("file1.txt", "content A");
            string path2 = CreateTempFile("file2.txt", "content B");

            string hash1 = PCPScanCache.ComputeFileHash(path1);
            string hash2 = PCPScanCache.ComputeFileHash(path2);

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void ComputeFileHash_ReturnsLowercaseHex()
        {
            string path = CreateTempFile("hex.txt", "test");
            string hash = PCPScanCache.ComputeFileHash(path);

            Assert.IsNotNull(hash);
            Assert.AreEqual(64, hash.Length); // SHA-256 = 32 bytes = 64 hex chars
            Assert.IsTrue(hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')),
                "Hash should be lowercase hex only");
        }

        // ================================================================
        // 12. MIXED FIELD INTERACTIONS
        // ================================================================

        [Test]
        public void SingleEntry_CanStoreAllFieldTypes()
        {
            string path = CreateTempFile("multi.prefab");

            m_Cache.SetHash(path, "sha256hash");
            m_Cache.SetFileSize(path, 12345L);
            m_Cache.SetDependencies(path, new[] { "dep1", "dep2" });
            m_Cache.SetMetadata(path, "missing.count", "0");
            m_Cache.SetMetadata(path, "size.type", "GameObject");

            Assert.AreEqual("sha256hash", m_Cache.GetHash(path));
            Assert.AreEqual(12345L, m_Cache.GetFileSize(path));
            Assert.AreEqual(2, m_Cache.GetDependencies(path).Length);
            Assert.AreEqual("0", m_Cache.GetMetadata(path, "missing.count"));
            Assert.AreEqual("GameObject", m_Cache.GetMetadata(path, "size.type"));

            // Should be one entry, not five.
            Assert.AreEqual(1, m_Cache.Count);
        }

        [Test]
        public void SaveAndLoad_PreservesAllFieldTypes()
        {
            string path = CreateTempFile("full.prefab");

            m_Cache.SetHash(path, "fullhash");
            m_Cache.SetFileSize(path, 54321L);
            m_Cache.SetDependencies(path, new[] { "d1" });
            m_Cache.SetMetadata(path, "shader.keywords", "A,B,C");
            m_Cache.SetMetadata(path, "shader.passCount", "7");

            m_Cache.Save();

            var loaded = new PCPScanCache();
            loaded.Load();

            Assert.AreEqual("fullhash", loaded.GetHash(path));
            Assert.AreEqual(54321L, loaded.GetFileSize(path));
            Assert.AreEqual(1, loaded.GetDependencies(path).Length);
            Assert.AreEqual("A,B,C", loaded.GetMetadata(path, "shader.keywords"));
            Assert.AreEqual("7", loaded.GetMetadata(path, "shader.passCount"));
        }

        [Test]
        public void RemoveEntry_ThenReAdd_WorksCleanly()
        {
            string path = CreateTempFile("recycle.png");

            m_Cache.SetHash(path, "old");
            m_Cache.SetMetadata(path, "key", "old_val");
            m_Cache.RemoveEntry(path);

            m_Cache.SetHash(path, "new");
            Assert.AreEqual("new", m_Cache.GetHash(path));
            Assert.IsNull(m_Cache.GetMetadata(path, "key")); // Old metadata should be gone.
        }

        // ================================================================
        // 13. EDGE CASES
        // ================================================================

        [Test]
        public void SetHash_WithNullHash_IsStored()
        {
            string path = CreateTempFile("null_hash.png");
            m_Cache.SetHash(path, null);

            Assert.IsNull(m_Cache.GetHash(path));
            // Entry should still exist.
            Assert.AreEqual(1, m_Cache.Count);
        }

        [Test]
        public void SetDependencies_WithNull_IsStored()
        {
            string path = CreateTempFile("null_deps.prefab");
            m_Cache.SetDependencies(path, null);

            Assert.IsNull(m_Cache.GetDependencies(path));
        }

        [Test]
        public void Metadata_SpecialCharacters_Preserved()
        {
            string path = CreateTempFile("special.cs");
            string value = "has,commas|and|pipes\nand\nnewlines";

            m_Cache.SetMetadata(path, "complex.value", value);
            Assert.AreEqual(value, m_Cache.GetMetadata(path, "complex.value"));
        }

        [Test]
        public void LargeNumberOfEntries_WorksCorrectly()
        {
            for (int i = 0; i < 500; i++)
            {
                string path = CreateTempFile($"asset_{i}.png", $"content_{i}");
                m_Cache.SetHash(path, $"hash_{i}");
                m_Cache.SetFileSize(path, (i + 1) * 100L);
            }

            Assert.AreEqual(500, m_Cache.Count);

            // Spot check.
            string p0 = Path.Combine(m_TempDir, "asset_0.png");
            string p499 = Path.Combine(m_TempDir, "asset_499.png");
            Assert.AreEqual("hash_0", m_Cache.GetHash(p0));
            Assert.AreEqual("hash_499", m_Cache.GetHash(p499));
            Assert.AreEqual(100L, m_Cache.GetFileSize(p0));
            Assert.AreEqual(50000L, m_Cache.GetFileSize(p499));
        }

        [Test]
        public void GetAllCachedPaths_AfterRemove_ExcludesRemoved()
        {
            string p1 = CreateTempFile("keep.png");
            string p2 = CreateTempFile("remove.png");

            m_Cache.SetHash(p1, "h1");
            m_Cache.SetHash(p2, "h2");

            m_Cache.RemoveEntry(p2);

            var paths = m_Cache.GetAllCachedPaths().ToList();
            Assert.AreEqual(1, paths.Count);
            Assert.AreEqual(p1, paths[0]);
        }

        [Test]
        public void MultipleClears_DoNotThrow()
        {
            m_Cache.Clear();
            m_Cache.Clear();
            m_Cache.Clear();

            Assert.AreEqual(0, m_Cache.Count);
        }

        [Test]
        public void CacheDirectory_IsNotNullOrEmpty()
        {
            Assert.IsFalse(string.IsNullOrEmpty(PCPScanCache.CacheDirectory));
        }

        // ================================================================
        // 14. SCAN CACHE STANDALONE OPERATIONS
        // ================================================================

        [Test]
        public void ScanCache_RefreshStaleness_DoesNotThrow()
        {
            // Verify cache staleness refresh works with an empty asset list.
            var cache = new PCPScanCache();
            Assert.DoesNotThrow(() => cache.RefreshStaleness(new string[0]));
        }

        [Test]
        public void ScanCache_Save_DoesNotThrow()
        {
            // Verify cache can be saved without error.
            var cache = new PCPScanCache();
            Assert.DoesNotThrow(() => cache.Save());
        }
    }
}
