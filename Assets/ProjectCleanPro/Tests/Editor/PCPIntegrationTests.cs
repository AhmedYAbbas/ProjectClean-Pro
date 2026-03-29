using System.Collections.Generic;
using NUnit.Framework;
using ProjectCleanPro.Editor;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Integration tests that exercise multiple systems together,
    /// verifying end-to-end behavior and cross-module consistency.
    /// </summary>
    [TestFixture]
    public sealed class PCPIntegrationTests
    {
        // ================================================================
        // 1. SCAN CONTEXT CREATION
        // ================================================================

        [Test]
        public void ScanContext_FromGlobalContext_AllServicesPresent()
        {
            var ctx = PCPScanContext.FromGlobalContext();

            Assert.IsNotNull(ctx, "ScanContext should not be null");
            Assert.IsNotNull(ctx.Settings, "Settings should not be null");
            Assert.IsNotNull(ctx.IgnoreRules, "IgnoreRules should not be null");
            Assert.IsNotNull(ctx.Cache, "Cache should not be null");
            Assert.IsNotNull(ctx.RenderPipeline, "RenderPipeline should not be null");
        }

        [Test]
        public void ScanContext_FromGlobalContext_SettingsMatchContext()
        {
            var ctx = PCPScanContext.FromGlobalContext();
            Assert.AreSame(PCPContext.Settings, ctx.Settings);
        }

        [Test]
        public void ScanContext_FromGlobalContext_CacheMatchesContext()
        {
            var ctx = PCPScanContext.FromGlobalContext();
            Assert.AreSame(PCPContext.ScanCache, ctx.Cache);
        }

        // ================================================================
        // 2. IGNORE RULES + SETTINGS INTEGRATION
        // ================================================================

        [Test]
        public void IgnoreRules_WithSettingsRules_MatchesExpected()
        {
            var settings = PCPContext.Settings;
            var rules = PCPContext.IgnoreRules;
            int originalCount = settings.ignoreRules.Count;

            // Add a temporary rule.
            settings.ignoreRules.Add(new PCPIgnoreRule
            {
                type = PCPIgnoreType.PathPrefix,
                pattern = "Assets/TestIntegration_Temp/",
                enabled = true
            });

            try
            {
                // The ignore rules should now match the temporary path.
                Assert.IsTrue(rules.IsIgnored(
                    "Assets/TestIntegration_Temp/file.png",
                    settings.ignoreRules));
            }
            finally
            {
                // Restore.
                settings.ignoreRules.RemoveAt(settings.ignoreRules.Count - 1);
                Assert.AreEqual(originalCount, settings.ignoreRules.Count);
            }
        }

        // ================================================================
        // 3. SCAN RESULT + DATA MODELS INTEGRATION
        // ================================================================

        [Test]
        public void ScanResult_PopulatedWithAllModules_ComputesCorrectTotals()
        {
            var result = new PCPScanResult
            {
                totalAssetsScanned = 500,
                scanTimestampUtc = "2025-06-01T12:00:00Z",
                scanDurationSeconds = 10.5f
            };

            // Unused assets: 3 items, total 3000 bytes
            result.unusedAssets.Add(MakeUnused(1000));
            result.unusedAssets.Add(MakeUnused(1000));
            result.unusedAssets.Add(MakeUnused(1000));

            // Missing refs: 2 items
            result.missingReferences.Add(new PCPMissingReference
            {
                severity = PCPSeverity.Error,
                sourceAssetPath = "Assets/a.prefab",
                componentType = "Renderer",
                propertyPath = "m_Material"
            });
            result.missingReferences.Add(new PCPMissingReference
            {
                severity = PCPSeverity.Warning,
                sourceAssetPath = "Assets/b.prefab",
                componentType = "Collider",
                propertyPath = "m_Mesh"
            });

            // Duplicates: 1 group of 3 entries (500 bytes each), wasted = 1000
            var dupGroup = new PCPDuplicateGroup
            {
                hash = "abc123",
                entries = new List<PCPDuplicateEntry>
                {
                    new PCPDuplicateEntry { path = "Assets/d1.png", sizeBytes = 500 },
                    new PCPDuplicateEntry { path = "Assets/d2.png", sizeBytes = 500 },
                    new PCPDuplicateEntry { path = "Assets/d3.png", sizeBytes = 500 },
                }
            };
            result.duplicateGroups.Add(dupGroup);

            // Packages: 1
            result.packageAuditEntries.Add(new PCPPackageAuditEntry
            {
                packageName = "com.unity.test",
                status = PCPPackageStatus.Unused
            });

            // Shaders: 1 (pipelineMismatch = true so GetSeverity() returns Error, counted by TotalFindingCount)
            result.shaderEntries.Add(new PCPShaderEntry
            {
                shaderName = "Test/Shader",
                estimatedVariants = 100,
                pipelineMismatch = true,
                isUnused = false
            });

            // Size: 1
            result.sizeEntries.Add(new PCPSizeEntry
            {
                name = "BigTex",
                sizeBytes = 5000,
                hasOptimizationSuggestion = true,
                optimizationSuggestion = "Compress"
            });

            // Verify computed properties.
            Assert.AreEqual(9, result.TotalFindingCount); // 3+2+1+1+1+1
            Assert.AreEqual(3, result.UnusedAssetCount);
            Assert.AreEqual(2, result.MissingReferenceCount);
            Assert.AreEqual(1, result.DuplicateGroupCount);
            Assert.AreEqual(3000L, result.UnusedAssetsTotalSize);
            Assert.AreEqual(1000L, result.DuplicateWastedSize);
            Assert.AreEqual(4000L, result.TotalWastedBytes);
            Assert.IsFalse(result.IsClean);

            int score = result.HealthScore;
            Assert.GreaterOrEqual(score, 0);
            Assert.LessOrEqual(score, 100);
            Assert.Less(score, 100, "Should not be perfect with findings");

            string summary = result.GetSummary();
            Assert.IsTrue(summary.Contains("3"), "Summary should contain unused count");
            Assert.IsTrue(summary.Contains("2"), "Summary should contain missing ref count");
        }

        // ================================================================
        // 4. DUPLICATE GROUP CANONICAL ELECTION + WASTED BYTES
        // ================================================================

        [Test]
        public void DuplicateGroup_ElectCanonical_ThenWastedBytes_Consistent()
        {
            var group = new PCPDuplicateGroup
            {
                hash = "xyz",
                entries = new List<PCPDuplicateEntry>
                {
                    new PCPDuplicateEntry { path = "Assets/a.png", sizeBytes = 2048, referenceCount = 1 },
                    new PCPDuplicateEntry { path = "Assets/b.png", sizeBytes = 2048, referenceCount = 5 },
                    new PCPDuplicateEntry { path = "Assets/c.png", sizeBytes = 2048, referenceCount = 3 },
                }
            };

            group.ElectCanonical();

            // b should be canonical (highest refs).
            Assert.IsTrue(group.entries[1].isCanonical);

            // Wasted = (3-1) * 2048 = 4096
            Assert.AreEqual(4096L, group.WastedBytes);
            Assert.AreEqual(2, group.DuplicateCount);
        }

        // ================================================================
        // 5. CACHE + DEPENDENCY RESOLVER INTEGRATION
        // ================================================================

        [Test]
        public void ScanContext_DependencyResolver_IsNullBeforeScan()
        {
            // DependencyResolver is set by the orchestrator during scanning.
            // Before any scan, it should be null.
            var ctx = PCPScanContext.FromGlobalContext();
            Assert.IsNull(ctx.DependencyResolver,
                "DependencyResolver is created per-scan by the orchestrator");
        }

        // ================================================================
        // 6. HEALTH SCORE MONOTONICITY
        // ================================================================

        [Test]
        public void HealthScore_IncreasingFindings_DecreasingScore()
        {
            int prevScore = 100;

            for (int n = 0; n <= 50; n += 10)
            {
                var result = new PCPScanResult { totalAssetsScanned = 500 };
                for (int i = 0; i < n; i++)
                    result.missingReferences.Add(new PCPMissingReference
                    {
                        severity = PCPSeverity.Error,
                        sourceAssetPath = $"Assets/{i}.prefab",
                        componentType = "C",
                        propertyPath = "p"
                    });

                int score = result.HealthScore;
                Assert.LessOrEqual(score, prevScore,
                    $"Score should decrease as findings increase ({n} findings gave {score}, prev was {prevScore})");
                prevScore = score;
            }
        }

        // ================================================================
        // 7. FORMAT SIZE CONSISTENCY
        // ================================================================


        // ================================================================
        // 8. DEPENDENCY RESOLVER FACTORY
        // ================================================================

        [Test]
        public void DependencyResolverFactory_CreatesAccurateResolver()
        {
            var resolver = PCPDependencyResolverFactory.Create();
            Assert.IsInstanceOf<PCPAccurateDependencyResolver>(resolver);
        }

        // ================================================================
        // 10. ASYNC SCHEDULER LIFECYCLE
        // ================================================================

        [Test]
        public void AsyncScheduler_CreatesAndDisposes()
        {
            using var scheduler = new PCPAsyncScheduler(8f);
            Assert.AreEqual(8f, scheduler.BudgetMs);
            // Should not throw on dispose
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static PCPUnusedAsset MakeUnused(long size)
        {
            return new PCPUnusedAsset
            {
                assetInfo = new PCPAssetInfo { path = $"Assets/unused_{size}.png", sizeBytes = size },
                suggestedAction = "Safe to delete"
            };
        }
    }
}
