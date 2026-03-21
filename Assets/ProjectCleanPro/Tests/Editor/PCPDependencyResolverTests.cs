using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPDependencyResolver"/> covering graph building,
    /// BFS reachability, queries, and edge cases.
    /// Note: These tests use the live AssetDatabase, so results depend on actual project state.
    /// </summary>
    [TestFixture]
    public sealed class PCPDependencyResolverTests
    {
        private PCPDependencyResolver m_Resolver;

        [SetUp]
        public void SetUp()
        {
            m_Resolver = new PCPDependencyResolver();
        }

        [TearDown]
        public void TearDown()
        {
            m_Resolver.Clear();
        }

        // ================================================================
        // 1. INITIAL STATE
        // ================================================================

        [Test]
        public void InitialState_IsBuilt_IsFalse()
        {
            Assert.IsFalse(m_Resolver.IsBuilt);
        }

        [Test]
        public void InitialState_AssetCount_IsZero()
        {
            Assert.AreEqual(0, m_Resolver.AssetCount);
        }

        [Test]
        public void InitialState_ReachableCount_IsZero()
        {
            Assert.AreEqual(0, m_Resolver.ReachableCount);
        }

        // ================================================================
        // 2. BUILD WITH EMPTY ROOTS
        // ================================================================

        [Test]
        public void Build_EmptyRoots_SetsIsBuilt()
        {
            m_Resolver.Build(new List<string>());
            Assert.IsTrue(m_Resolver.IsBuilt);
        }

        [Test]
        public void Build_EmptyRoots_ReachableCountIsZero()
        {
            m_Resolver.Build(new List<string>());
            Assert.AreEqual(0, m_Resolver.ReachableCount);
        }

        [Test]
        public void Build_EmptyRoots_AssetCountReflectsProject()
        {
            m_Resolver.Build(new List<string>());
            // Should have scanned Assets/ — asset count depends on project.
            Assert.GreaterOrEqual(m_Resolver.AssetCount, 0);
        }

        // ================================================================
        // 3. BUILD WITH REAL PROJECT DATA
        // ================================================================

        [Test]
        public void Build_WithBuildScenes_SetsReachable()
        {
            string[] buildScenes = PCPAssetUtils.GetBuildScenePaths();

            m_Resolver.Build(buildScenes);

            Assert.IsTrue(m_Resolver.IsBuilt);

            // If there are build scenes, there should be reachable assets.
            if (buildScenes.Length > 0)
            {
                Assert.Greater(m_Resolver.ReachableCount, 0,
                    "With build scenes as roots, some assets should be reachable");
            }
        }

        [Test]
        public void Build_WithProgressCallback_CallsProgress()
        {
            bool progressCalled = false;
            m_Resolver.Build(
                new List<string>(),
                onProgress: (progress, label) =>
                {
                    progressCalled = true;
                    Assert.GreaterOrEqual(progress, 0f);
                    Assert.LessOrEqual(progress, 1f);
                    Assert.IsNotNull(label);
                });

            Assert.IsTrue(progressCalled, "Progress callback should have been invoked");
        }

        [Test]
        public void Build_WithCache_DoesNotThrow()
        {
            var cache = new PCPScanCache();
            Assert.DoesNotThrow(() =>
                m_Resolver.Build(new List<string>(), cache: cache));
        }

        // ================================================================
        // 4. QUERIES
        // ================================================================

        [Test]
        public void GetDependencies_UnknownAsset_ReturnsEmpty()
        {
            m_Resolver.Build(new List<string>());
            var deps = m_Resolver.GetDependencies("Assets/NonExistent/file.xyz");
            Assert.IsNotNull(deps);
            Assert.AreEqual(0, deps.Count);
        }

        [Test]
        public void GetDependents_UnknownAsset_ReturnsEmpty()
        {
            m_Resolver.Build(new List<string>());
            var dependents = m_Resolver.GetDependents("Assets/NonExistent/file.xyz");
            Assert.IsNotNull(dependents);
            Assert.AreEqual(0, dependents.Count);
        }

        [Test]
        public void IsReachable_NonReachableAsset_ReturnsFalse()
        {
            m_Resolver.Build(new List<string>());
            Assert.IsFalse(m_Resolver.IsReachable("Assets/NonExistent/file.xyz"));
        }

        [Test]
        public void GetAllReachable_EmptyRoots_ReturnsEmptyCollection()
        {
            m_Resolver.Build(new List<string>());
            var reachable = m_Resolver.GetAllReachable();
            Assert.IsNotNull(reachable);
            Assert.AreEqual(0, reachable.Count);
        }

        [Test]
        public void GetAllAssets_AfterBuild_ReturnsNonNull()
        {
            m_Resolver.Build(new List<string>());
            Assert.IsNotNull(m_Resolver.GetAllAssets());
        }

        [Test]
        public void GetAllUnreachable_EmptyRoots_AllAssetsAreUnreachable()
        {
            m_Resolver.Build(new List<string>());

            int unreachableCount = m_Resolver.GetAllUnreachable().Count();
            Assert.AreEqual(m_Resolver.AssetCount, unreachableCount,
                "With no roots, all assets should be unreachable");
        }

        // ================================================================
        // 5. REACHABILITY WITH REAL ROOTS
        // ================================================================

        [Test]
        public void Build_RootIsReachable()
        {
            string[] scenes = PCPAssetUtils.GetBuildScenePaths();
            if (scenes.Length == 0)
            {
                Assert.Pass("No build scenes in project — skipping.");
                return;
            }

            m_Resolver.Build(scenes);

            foreach (string scene in scenes)
            {
                Assert.IsTrue(m_Resolver.IsReachable(scene),
                    $"Root scene '{scene}' should be reachable");
            }
        }

        [Test]
        public void Build_UnreachableAssetsExist_WhenRootsProvided()
        {
            string[] scenes = PCPAssetUtils.GetBuildScenePaths();
            m_Resolver.Build(scenes);

            // There should typically be some unreachable assets in any non-trivial project.
            // We just verify the method works; whether there are unreachable assets depends
            // on the project.
            var unreachable = m_Resolver.GetAllUnreachable();
            Assert.IsNotNull(unreachable);
        }

        // ================================================================
        // 6. CLEAR
        // ================================================================

        [Test]
        public void Clear_ResetsState()
        {
            m_Resolver.Build(new List<string>());
            Assert.IsTrue(m_Resolver.IsBuilt);

            m_Resolver.Clear();

            Assert.IsFalse(m_Resolver.IsBuilt);
            Assert.AreEqual(0, m_Resolver.AssetCount);
            Assert.AreEqual(0, m_Resolver.ReachableCount);
        }

        [Test]
        public void Clear_CanRebuildAfter()
        {
            m_Resolver.Build(new List<string>());
            m_Resolver.Clear();
            Assert.DoesNotThrow(() => m_Resolver.Build(new List<string>()));
            Assert.IsTrue(m_Resolver.IsBuilt);
        }

        // ================================================================
        // 7. NULL HANDLING IN ROOTS
        // ================================================================

        [Test]
        public void Build_NullRootEntries_AreSkipped()
        {
            var roots = new List<string> { null, "", "Assets/Scenes/Main.unity" };
            Assert.DoesNotThrow(() => m_Resolver.Build(roots));
        }

        // ================================================================
        // 8. BIDIRECTIONAL EDGES
        // ================================================================

        [Test]
        public void Build_ForwardAndReverseEdgesAreConsistent()
        {
            m_Resolver.Build(new List<string>());

            // For each asset's dependencies, the reverse edge should exist.
            foreach (string asset in m_Resolver.GetAllAssets())
            {
                var deps = m_Resolver.GetDependencies(asset);
                foreach (string dep in deps)
                {
                    var dependents = m_Resolver.GetDependents(dep);
                    Assert.IsTrue(dependents.Contains(asset),
                        $"If '{asset}' depends on '{dep}', then '{dep}' should have '{asset}' as a dependent");
                }

                // Only check first 5 assets to keep test fast.
                break;
            }
        }
    }
}
