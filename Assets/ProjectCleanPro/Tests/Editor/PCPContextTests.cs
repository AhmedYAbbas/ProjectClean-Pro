using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPContext"/> service registry.
    /// </summary>
    [TestFixture]
    public sealed class PCPContextTests
    {
        // ================================================================
        // 1. SERVICE AVAILABILITY
        // ================================================================

        [Test]
        public void Context_Settings_IsNotNull()
        {
            Assert.IsNotNull(PCPContext.Settings);
        }

        [Test]
        public void Context_DependencyResolver_IsNotNull()
        {
            Assert.IsNotNull(PCPContext.DependencyResolver);
        }

        [Test]
        public void Context_ScanCache_IsNotNull()
        {
            Assert.IsNotNull(PCPContext.ScanCache);
        }

        [Test]
        public void Context_IgnoreRules_IsNotNull()
        {
            Assert.IsNotNull(PCPContext.IgnoreRules);
        }

        [Test]
        public void Context_RenderPipelineDetector_IsNotNull()
        {
            Assert.IsNotNull(PCPContext.RenderPipelineDetector);
        }

        // ================================================================
        // 2. SERVICE CONSISTENCY
        // ================================================================

        [Test]
        public void Context_Settings_ReturnsSameInstanceOnMultipleCalls()
        {
            var a = PCPContext.Settings;
            var b = PCPContext.Settings;
            Assert.AreSame(a, b);
        }

        [Test]
        public void Context_DependencyResolver_ReturnsSameInstance()
        {
            var a = PCPContext.DependencyResolver;
            var b = PCPContext.DependencyResolver;
            Assert.AreSame(a, b);
        }

        [Test]
        public void Context_ScanCache_ReturnsSameInstance()
        {
            var a = PCPContext.ScanCache;
            var b = PCPContext.ScanCache;
            Assert.AreSame(a, b);
        }

        [Test]
        public void Context_IgnoreRules_ReturnsSameInstance()
        {
            var a = PCPContext.IgnoreRules;
            var b = PCPContext.IgnoreRules;
            Assert.AreSame(a, b);
        }

        [Test]
        public void Context_RenderPipelineDetector_ReturnsSameInstance()
        {
            var a = PCPContext.RenderPipelineDetector;
            var b = PCPContext.RenderPipelineDetector;
            Assert.AreSame(a, b);
        }

        // ================================================================
        // 3. INITIALIZE / DISPOSE
        // ================================================================

        [Test]
        public void Context_Initialize_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => PCPContext.Initialize());
        }

        [Test]
        public void Context_Dispose_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => PCPContext.Dispose());
        }

        [Test]
        public void Context_AfterDispose_ServicesStillAccessible()
        {
            PCPContext.Dispose();

            // Services should be re-created on next access (lazy init).
            Assert.IsNotNull(PCPContext.Settings);
            Assert.IsNotNull(PCPContext.DependencyResolver);
            Assert.IsNotNull(PCPContext.ScanCache);
            Assert.IsNotNull(PCPContext.IgnoreRules);
        }

        [Test]
        public void Context_InitializeAfterDispose_DoesNotThrow()
        {
            PCPContext.Dispose();
            Assert.DoesNotThrow(() => PCPContext.Initialize());
        }
    }
}
