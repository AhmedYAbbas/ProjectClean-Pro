using System.Collections.Generic;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPScanResult"/> covering computed properties,
    /// health score calculation, clear, and summary generation.
    /// </summary>
    [TestFixture]
    public sealed class PCPScanResultTests
    {
        private PCPScanResult m_Result;

        [SetUp]
        public void SetUp()
        {
            m_Result = new PCPScanResult();
        }

        // ================================================================
        // 1. TOTAL FINDING COUNT
        // ================================================================

        [Test]
        public void TotalFindingCount_EmptyResult_IsZero()
        {
            Assert.AreEqual(0, m_Result.TotalFindingCount);
        }

        [Test]
        public void TotalFindingCount_SumsAllModules()
        {
            m_Result.unusedAssets.Add(MakeUnusedAsset(100));
            m_Result.unusedAssets.Add(MakeUnusedAsset(200));
            m_Result.missingReferences.Add(MakeMissingRef(PCPSeverity.Error));
            m_Result.duplicateGroups.Add(MakeDuplicateGroup(2, 500));
            m_Result.packageAuditEntries.Add(MakePackageEntry(PCPPackageStatus.Unused));
            m_Result.shaderEntries.Add(MakeShaderEntry(true, false, 100));
            m_Result.sizeEntries.Add(MakeSizeEntry(1000, true));

            Assert.AreEqual(7, m_Result.TotalFindingCount);
        }

        // ================================================================
        // 2. INDIVIDUAL COUNTS
        // ================================================================

        [Test]
        public void UnusedAssetCount_ReturnsCorrectCount()
        {
            m_Result.unusedAssets.Add(MakeUnusedAsset(100));
            m_Result.unusedAssets.Add(MakeUnusedAsset(200));
            Assert.AreEqual(2, m_Result.UnusedAssetCount);
        }

        [Test]
        public void MissingReferenceCount_ReturnsCorrectCount()
        {
            m_Result.missingReferences.Add(MakeMissingRef(PCPSeverity.Warning));
            Assert.AreEqual(1, m_Result.MissingReferenceCount);
        }

        [Test]
        public void DuplicateGroupCount_ReturnsCorrectCount()
        {
            m_Result.duplicateGroups.Add(MakeDuplicateGroup(3, 1000));
            m_Result.duplicateGroups.Add(MakeDuplicateGroup(2, 500));
            Assert.AreEqual(2, m_Result.DuplicateGroupCount);
        }

        // ================================================================
        // 3. SIZE CALCULATIONS
        // ================================================================

        [Test]
        public void UnusedAssetsTotalSize_SumsAllUnusedAssetSizes()
        {
            m_Result.unusedAssets.Add(MakeUnusedAsset(1000));
            m_Result.unusedAssets.Add(MakeUnusedAsset(2000));
            Assert.AreEqual(3000L, m_Result.UnusedAssetsTotalSize);
        }

        [Test]
        public void UnusedAssetsTotalSize_Empty_ReturnsZero()
        {
            Assert.AreEqual(0L, m_Result.UnusedAssetsTotalSize);
        }

        [Test]
        public void DuplicateWastedSize_SumsGroupWastedBytes()
        {
            m_Result.duplicateGroups.Add(MakeDuplicateGroup(3, 1000)); // wasted: (3-1) * 1000 = 2000
            m_Result.duplicateGroups.Add(MakeDuplicateGroup(2, 500));  // wasted: (2-1) * 500 = 500
            Assert.AreEqual(2500L, m_Result.DuplicateWastedSize);
        }

        [Test]
        public void DuplicateWastedSize_Empty_ReturnsZero()
        {
            Assert.AreEqual(0L, m_Result.DuplicateWastedSize);
        }

        [Test]
        public void TotalWastedBytes_SumsUnusedAndDuplicate()
        {
            m_Result.unusedAssets.Add(MakeUnusedAsset(1000));
            m_Result.duplicateGroups.Add(MakeDuplicateGroup(3, 500)); // wasted: 1000
            Assert.AreEqual(2000L, m_Result.TotalWastedBytes);
        }

        // ================================================================
        // 4. IS CLEAN
        // ================================================================

        [Test]
        public void IsClean_EmptyResult_IsTrue()
        {
            Assert.IsTrue(m_Result.IsClean);
        }

        [Test]
        public void IsClean_WithFindings_IsFalse()
        {
            m_Result.unusedAssets.Add(MakeUnusedAsset(100));
            Assert.IsFalse(m_Result.IsClean);
        }

        // ================================================================
        // 5. HEALTH SCORE
        // ================================================================

        [Test]
        public void HealthScore_EmptyResult_Is100()
        {
            m_Result.totalAssetsScanned = 100;
            Assert.AreEqual(100, m_Result.HealthScore);
        }

        [Test]
        public void HealthScore_IsAlwaysBetween0And100()
        {
            m_Result.totalAssetsScanned = 10;

            // Add many severe findings.
            for (int i = 0; i < 100; i++)
            {
                m_Result.missingReferences.Add(MakeMissingRef(PCPSeverity.Error));
                m_Result.unusedAssets.Add(MakeUnusedAsset(10_000_000));
                m_Result.duplicateGroups.Add(MakeDuplicateGroup(5, 10_000_000));
                m_Result.shaderEntries.Add(MakeShaderEntry(true, false, 50000));
            }

            int score = m_Result.HealthScore;
            Assert.GreaterOrEqual(score, 0);
            Assert.LessOrEqual(score, 100);
        }

        [Test]
        public void HealthScore_MissingRefErrors_PenalizeMoreThanWarnings()
        {
            m_Result.totalAssetsScanned = 100;

            var resultErrors = new PCPScanResult { totalAssetsScanned = 100 };
            for (int i = 0; i < 10; i++)
                resultErrors.missingReferences.Add(MakeMissingRef(PCPSeverity.Error));

            var resultWarnings = new PCPScanResult { totalAssetsScanned = 100 };
            for (int i = 0; i < 10; i++)
                resultWarnings.missingReferences.Add(MakeMissingRef(PCPSeverity.Warning));

            Assert.Less(resultErrors.HealthScore, resultWarnings.HealthScore,
                "Error severity should produce a lower score than Warning severity");
        }

        [Test]
        public void HealthScore_LargerProjectLessPenalizedForSameFindings()
        {
            var smallProject = new PCPScanResult { totalAssetsScanned = 50 };
            var largeProject = new PCPScanResult { totalAssetsScanned = 5000 };

            for (int i = 0; i < 10; i++)
            {
                smallProject.unusedAssets.Add(MakeUnusedAsset(1000));
                largeProject.unusedAssets.Add(MakeUnusedAsset(1000));
            }

            Assert.Greater(largeProject.HealthScore, smallProject.HealthScore,
                "Larger project should have higher score for the same number of findings");
        }

        [Test]
        public void HealthScore_UnusedAssetsOver1MB_ExtraPenalty()
        {
            var smallAssets = new PCPScanResult { totalAssetsScanned = 100 };
            var largeAssets = new PCPScanResult { totalAssetsScanned = 100 };

            for (int i = 0; i < 5; i++)
            {
                smallAssets.unusedAssets.Add(MakeUnusedAsset(500_000)); // 500 KB
                largeAssets.unusedAssets.Add(MakeUnusedAsset(2_000_000)); // 2 MB
            }

            Assert.Less(largeAssets.HealthScore, smallAssets.HealthScore,
                "Large unused assets (>1MB) should produce a lower score");
        }

        [Test]
        public void HealthScore_SizeEntriesWithOptimization_AddPenalty()
        {
            var withOpt = new PCPScanResult { totalAssetsScanned = 100 };
            var withoutOpt = new PCPScanResult { totalAssetsScanned = 100 };

            for (int i = 0; i < 20; i++)
            {
                withOpt.sizeEntries.Add(MakeSizeEntry(5000, true));
                withoutOpt.sizeEntries.Add(MakeSizeEntry(5000, false));
            }

            Assert.Less(withOpt.HealthScore, withoutOpt.HealthScore,
                "Size entries with optimization suggestions should penalize score");
        }

        [Test]
        public void HealthScore_ZeroAssetsScanned_DoesNotDivideByZero()
        {
            m_Result.totalAssetsScanned = 0;
            m_Result.unusedAssets.Add(MakeUnusedAsset(100));

            Assert.DoesNotThrow(() => { int _ = m_Result.HealthScore; });
            Assert.GreaterOrEqual(m_Result.HealthScore, 0);
            Assert.LessOrEqual(m_Result.HealthScore, 100);
        }

        [Test]
        public void HealthScore_PackageUnused_PenalizedMoreThanUsed()
        {
            var unused = new PCPScanResult { totalAssetsScanned = 100 };
            var used = new PCPScanResult { totalAssetsScanned = 100 };

            for (int i = 0; i < 10; i++)
            {
                unused.packageAuditEntries.Add(MakePackageEntry(PCPPackageStatus.Unused));
                used.packageAuditEntries.Add(MakePackageEntry(PCPPackageStatus.Used));
            }

            Assert.Less(unused.HealthScore, used.HealthScore,
                "Unused packages should penalize more than used packages");
        }

        [Test]
        public void HealthScore_ShaderErrors_PenalizeMoreThanInfo()
        {
            var errors = new PCPScanResult { totalAssetsScanned = 100 };
            var info = new PCPScanResult { totalAssetsScanned = 100 };

            for (int i = 0; i < 5; i++)
            {
                errors.shaderEntries.Add(MakeShaderEntry(true, false, 100));
                info.shaderEntries.Add(MakeShaderEntry(false, false, 100));
            }

            Assert.Less(errors.HealthScore, info.HealthScore);
        }

        // ================================================================
        // 6. CLEAR
        // ================================================================

        [Test]
        public void Clear_EmptiesAllLists()
        {
            m_Result.unusedAssets.Add(MakeUnusedAsset(100));
            m_Result.missingReferences.Add(MakeMissingRef(PCPSeverity.Error));
            m_Result.duplicateGroups.Add(MakeDuplicateGroup(2, 100));
            m_Result.packageAuditEntries.Add(MakePackageEntry(PCPPackageStatus.Used));
            m_Result.shaderEntries.Add(MakeShaderEntry(false, false, 100));
            m_Result.sizeEntries.Add(MakeSizeEntry(100, false));
            m_Result.scanTimestampUtc = "2025-01-01T00:00:00Z";
            m_Result.scanDurationSeconds = 5.0f;

            m_Result.Clear();

            Assert.AreEqual(0, m_Result.unusedAssets.Count);
            Assert.AreEqual(0, m_Result.missingReferences.Count);
            Assert.AreEqual(0, m_Result.duplicateGroups.Count);
            Assert.AreEqual(0, m_Result.packageAuditEntries.Count);
            Assert.AreEqual(0, m_Result.shaderEntries.Count);
            Assert.AreEqual(0, m_Result.sizeEntries.Count);
            Assert.AreEqual(0, m_Result.TotalFindingCount);
            Assert.IsTrue(m_Result.IsClean);

            // Timestamp and duration are NOT cleared by Clear().
            Assert.IsNotNull(m_Result.scanTimestampUtc);
        }

        // ================================================================
        // 7. SUMMARY / TOSTRING
        // ================================================================

        [Test]
        public void GetSummary_ContainsAllModuleCounts()
        {
            m_Result.unusedAssets.Add(MakeUnusedAsset(100));
            m_Result.missingReferences.Add(MakeMissingRef(PCPSeverity.Warning));
            m_Result.scanTimestampUtc = "2025-01-01T00:00:00Z";
            m_Result.scanDurationSeconds = 2.5f;

            string summary = m_Result.GetSummary();

            Assert.IsTrue(summary.Contains("Unused assets"));
            Assert.IsTrue(summary.Contains("Missing references"));
            Assert.IsTrue(summary.Contains("Duplicate groups"));
            Assert.IsTrue(summary.Contains("Total findings"));
        }

        [Test]
        public void GetSummary_NullTimestamp_ShowsNA()
        {
            m_Result.scanTimestampUtc = null;
            string summary = m_Result.GetSummary();
            Assert.IsTrue(summary.Contains("N/A"));
        }

        [Test]
        public void ToString_EqualsGetSummary()
        {
            m_Result.scanTimestampUtc = "2025-01-01";
            Assert.AreEqual(m_Result.GetSummary(), m_Result.ToString());
        }

        // ================================================================
        // 8. PACKAGE AUDIT ALIAS
        // ================================================================

        // ================================================================
        // 9. FORMATTED WASTED BYTES
        // ================================================================

        [Test]
        public void FormattedWastedBytes_EmptyResult_ShowsZero()
        {
            Assert.AreEqual("0 B", m_Result.FormattedWastedBytes);
        }

        [Test]
        public void FormattedWastedBytes_LargeValue_ShowsMB()
        {
            // Add 2MB unused asset
            m_Result.unusedAssets.Add(MakeUnusedAsset(2 * 1024 * 1024));
            string formatted = m_Result.FormattedWastedBytes;
            Assert.IsTrue(formatted.Contains("MB"), $"Expected MB format, got: {formatted}");
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static PCPUnusedAsset MakeUnusedAsset(long sizeBytes)
        {
            return new PCPUnusedAsset
            {
                assetInfo = new PCPAssetInfo
                {
                    path = "Assets/test_" + sizeBytes + ".png",
                    sizeBytes = sizeBytes,
                    name = "test",
                    extension = ".png",
                    assetTypeName = "Texture2D"
                },
                suggestedAction = "Safe to delete"
            };
        }

        private static PCPMissingReference MakeMissingRef(PCPSeverity severity)
        {
            return new PCPMissingReference
            {
                sourceAssetPath = "Assets/test.prefab",
                sourceAssetName = "test",
                componentType = "MeshRenderer",
                propertyPath = "m_Materials.Array.data[0]",
                severity = severity,
                gameObjectPath = "Root/Child"
            };
        }

        private static PCPDuplicateGroup MakeDuplicateGroup(int count, long sizePerEntry)
        {
            var group = new PCPDuplicateGroup
            {
                hash = "testhash",
                entries = new List<PCPDuplicateEntry>()
            };
            for (int i = 0; i < count; i++)
            {
                group.entries.Add(new PCPDuplicateEntry
                {
                    path = $"Assets/dup_{i}.png",
                    sizeBytes = sizePerEntry,
                    referenceCount = 0
                });
            }
            return group;
        }

        private static PCPPackageAuditEntry MakePackageEntry(PCPPackageStatus status)
        {
            return new PCPPackageAuditEntry
            {
                packageName = "com.unity.test",
                version = "1.0.0",
                displayName = "Test Package",
                status = status
            };
        }

        private static PCPShaderEntry MakeShaderEntry(bool pipelineMismatch, bool isUnused, int variants)
        {
            return new PCPShaderEntry
            {
                shaderName = "Test/Shader",
                assetPath = "Assets/shader.shader",
                pipelineMismatch = pipelineMismatch,
                isUnused = isUnused,
                estimatedVariants = variants,
                passCount = 1,
                keywordCount = 2,
                materialCount = 1,
                sizeBytes = 1024
            };
        }

        private static PCPSizeEntry MakeSizeEntry(long sizeBytes, bool hasOptimization)
        {
            return new PCPSizeEntry
            {
                path = "Assets/test.png",
                name = "test",
                assetTypeName = "Texture2D",
                sizeBytes = sizeBytes,
                hasOptimizationSuggestion = hasOptimization,
                optimizationSuggestion = hasOptimization ? "Enable compression" : ""
            };
        }
    }
}
