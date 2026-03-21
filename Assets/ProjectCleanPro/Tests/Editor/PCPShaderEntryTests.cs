using System.Collections.Generic;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPShaderEntry"/> covering severity classification,
    /// summary generation, and edge cases.
    /// </summary>
    [TestFixture]
    public sealed class PCPShaderEntryTests
    {
        // ================================================================
        // 1. GET SEVERITY
        // ================================================================

        [Test]
        public void GetSeverity_PipelineMismatch_ReturnsError()
        {
            var entry = MakeEntry(pipelineMismatch: true, isUnused: false, variants: 100);
            Assert.AreEqual(PCPSeverity.Error, entry.GetSeverity());
        }

        [Test]
        public void GetSeverity_PipelineMismatchAndUnused_ErrorTakesPrecedence()
        {
            var entry = MakeEntry(pipelineMismatch: true, isUnused: true, variants: 100);
            Assert.AreEqual(PCPSeverity.Error, entry.GetSeverity());
        }

        [Test]
        public void GetSeverity_Unused_ReturnsWarning()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: true, variants: 100);
            Assert.AreEqual(PCPSeverity.Warning, entry.GetSeverity());
        }

        [Test]
        public void GetSeverity_HighVariants_ReturnsWarning()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 10001);
            Assert.AreEqual(PCPSeverity.Warning, entry.GetSeverity());
        }

        [Test]
        public void GetSeverity_ExactlyAt10000_ReturnsInfo()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 10000);
            Assert.AreEqual(PCPSeverity.Info, entry.GetSeverity());
        }

        [Test]
        public void GetSeverity_LowVariants_NoIssues_ReturnsInfo()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 50);
            Assert.AreEqual(PCPSeverity.Info, entry.GetSeverity());
        }

        [Test]
        public void GetSeverity_ZeroVariants_ReturnsInfo()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 0);
            Assert.AreEqual(PCPSeverity.Info, entry.GetSeverity());
        }

        // ================================================================
        // 2. GET SUMMARY
        // ================================================================

        [Test]
        public void GetSummary_ContainsShaderName()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 100);
            entry.shaderName = "Universal Render Pipeline/Lit";
            Assert.IsTrue(entry.GetSummary().Contains("Universal Render Pipeline/Lit"));
        }

        [Test]
        public void GetSummary_PipelineMismatch_ContainsMismatchTag()
        {
            var entry = MakeEntry(pipelineMismatch: true, isUnused: false, variants: 100);
            Assert.IsTrue(entry.GetSummary().Contains("[PIPELINE MISMATCH]"));
        }

        [Test]
        public void GetSummary_Unused_ContainsUnusedTag()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: true, variants: 100);
            Assert.IsTrue(entry.GetSummary().Contains("[UNUSED]"));
        }

        [Test]
        public void GetSummary_NoIssues_NoTags()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 100);
            string summary = entry.GetSummary();
            Assert.IsFalse(summary.Contains("[PIPELINE MISMATCH]"));
            Assert.IsFalse(summary.Contains("[UNUSED]"));
        }

        [Test]
        public void GetSummary_ContainsVariantCount()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 256);
            Assert.IsTrue(entry.GetSummary().Contains("256 variants"));
        }

        [Test]
        public void GetSummary_ContainsPassCount()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 100);
            entry.passCount = 3;
            Assert.IsTrue(entry.GetSummary().Contains("3 passes"));
        }

        [Test]
        public void GetSummary_ContainsKeywordCount()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 100);
            entry.keywordCount = 7;
            Assert.IsTrue(entry.GetSummary().Contains("7 keywords"));
        }

        [Test]
        public void GetSummary_ContainsMaterialCount()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 100);
            entry.materialCount = 12;
            Assert.IsTrue(entry.GetSummary().Contains("12 materials"));
        }

        // ================================================================
        // 3. TOSTRING
        // ================================================================

        [Test]
        public void ToString_EqualsGetSummary()
        {
            var entry = MakeEntry(pipelineMismatch: false, isUnused: false, variants: 100);
            Assert.AreEqual(entry.GetSummary(), entry.ToString());
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static PCPShaderEntry MakeEntry(bool pipelineMismatch, bool isUnused, int variants)
        {
            return new PCPShaderEntry
            {
                shaderName = "Test/Shader",
                assetPath = "Assets/Shaders/test.shader",
                estimatedVariants = variants,
                passCount = 1,
                keywordCount = 2,
                materialCount = 1,
                sizeBytes = 1024,
                targetPipeline = PCPRenderPipeline.BuiltIn,
                pipelineMismatch = pipelineMismatch,
                isUnused = isUnused,
                keywords = new List<string> { "FOG", "SHADOWS" }
            };
        }
    }
}
