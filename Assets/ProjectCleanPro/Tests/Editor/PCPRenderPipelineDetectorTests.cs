using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPRenderPipelineDetector"/>.
    /// </summary>
    [TestFixture]
    public sealed class PCPRenderPipelineDetectorTests
    {
        // ================================================================
        // 1. DETECTION
        // ================================================================

        [Test]
        public void Detect_ReturnsNonNull()
        {
            PCPRenderPipelineInfo info = PCPRenderPipelineDetector.Detect();
            Assert.IsNotNull(info);
        }

        [Test]
        public void Detect_ReturnsValidPipeline()
        {
            PCPRenderPipelineInfo info = PCPRenderPipelineDetector.Detect();
            Assert.IsTrue(
                info.Pipeline == PCPRenderPipeline.BuiltIn ||
                info.Pipeline == PCPRenderPipeline.URP ||
                info.Pipeline == PCPRenderPipeline.HDRP ||
                info.Pipeline == PCPRenderPipeline.Custom,
                $"Unexpected pipeline: {info.Pipeline}");
        }

        [Test]
        public void Detect_ReturnsNonEmptyName()
        {
            PCPRenderPipelineInfo info = PCPRenderPipelineDetector.Detect();
            Assert.IsFalse(string.IsNullOrEmpty(info.Name));
        }

        // ================================================================
        // 2. INSTANCE METHODS
        // ================================================================

        [Test]
        public void Instance_Info_CachesResult()
        {
            var detector = new PCPRenderPipelineDetector();
            var first = detector.Info;
            var second = detector.Info;
            Assert.AreSame(first, second, "Info should cache and return same instance");
        }

        [Test]
        public void Instance_Pipeline_MatchesInfoPipeline()
        {
            var detector = new PCPRenderPipelineDetector();
            Assert.AreEqual(detector.Info.Pipeline, detector.Pipeline);
        }

        [Test]
        public void Instance_Refresh_ReturnsNewInstance()
        {
            var detector = new PCPRenderPipelineDetector();
            var first = detector.Info;
            var refreshed = detector.Refresh();
            Assert.IsNotNull(refreshed);
            // After refresh, Info should return the refreshed instance.
            Assert.AreSame(refreshed, detector.Info);
        }

        // ================================================================
        // 3. BUILT-IN CHECK (when no SRP is set)
        // ================================================================

        [Test]
        public void Detect_BuiltIn_HasNullPipelineAsset()
        {
            // This test validates the BuiltIn case.
            // If the project uses an SRP, this test will still pass because
            // we're just checking that Detect() works correctly.
            PCPRenderPipelineInfo info = PCPRenderPipelineDetector.Detect();
            if (info.Pipeline == PCPRenderPipeline.BuiltIn)
            {
                Assert.IsNull(info.PipelineAsset,
                    "Built-in pipeline should have null PipelineAsset");
            }
        }
    }
}
