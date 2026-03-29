using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for the dependency resolver system.
    /// Verifies that scan context properly exposes the resolver interface
    /// and that the context factory methods work correctly.
    /// </summary>
    [TestFixture]
    public sealed class PCPDependencyResolverTests
    {
        // ================================================================
        // 1. SCAN CONTEXT - RESOLVER LIFECYCLE
        // ================================================================

        [Test]
        public void ScanContext_FromGlobalContext_DependencyResolver_StartsNull()
        {
            // DependencyResolver is set by the orchestrator during a scan,
            // not by the constructor. Before any scan it should be null.
            var ctx = PCPScanContext.FromGlobalContext();
            // This is expected — the resolver is created per-scan by the orchestrator.
            Assert.IsNull(ctx.DependencyResolver,
                "DependencyResolver should be null before a scan starts");
        }

        [Test]
        public void ScanContext_FromGlobalContext_Cache_IsNotNull()
        {
            var ctx = PCPScanContext.FromGlobalContext();
            Assert.IsNotNull(ctx.Cache, "Cache should always be initialized");
        }

        [Test]
        public void ScanContext_FromGlobalContext_Settings_IsNotNull()
        {
            var ctx = PCPScanContext.FromGlobalContext();
            Assert.IsNotNull(ctx.Settings, "Settings should always be initialized");
        }

        [Test]
        public void ScanContext_FromGlobalContext_IgnoreRules_IsNotNull()
        {
            var ctx = PCPScanContext.FromGlobalContext();
            Assert.IsNotNull(ctx.IgnoreRules, "IgnoreRules should always be initialized");
        }

        // ================================================================
        // 2. SCAN CONTEXT CONSTRUCTOR - NO RESOLVER PARAMETER
        // ================================================================

        [Test]
        public void ScanContext_Constructor_DoesNotRequireResolver()
        {
            // The new constructor no longer takes a PCPDependencyResolver parameter.
            // Verify it can be constructed without one.
            PCPContext.Initialize();
            var ctx = new PCPScanContext(
                PCPContext.Settings,
                PCPContext.ScanCache,
                PCPContext.IgnoreRules,
                PCPContext.RenderPipelineDetector);

            Assert.IsNotNull(ctx);
            Assert.IsNotNull(ctx.Settings);
            Assert.IsNotNull(ctx.Cache);
            Assert.IsNull(ctx.DependencyResolver,
                "DependencyResolver starts null and is set by the orchestrator");
        }

        // ================================================================
        // 3. IGNORE RULES INTEGRATION
        // ================================================================

        [Test]
        public void ScanContext_IgnoreRules_WorksAfterConstruction()
        {
            var ctx = PCPScanContext.FromGlobalContext();
            Assert.IsNotNull(ctx.IgnoreRules);

            // IgnoreRules should not throw on usage.
            bool result = ctx.IgnoreRules.IsIgnored("Assets/SomeFile.png");
            Assert.IsNotNull(result.ToString()); // just verifying no throw
        }

        // ================================================================
        // 4. STALENESS
        // ================================================================

        [Test]
        public void ScanContext_AllProjectAssets_ReturnsNonNull()
        {
            var ctx = PCPScanContext.FromGlobalContext();
            var assets = ctx.AllProjectAssets;
            Assert.IsNotNull(assets);
        }
    }
}
