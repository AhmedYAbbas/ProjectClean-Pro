using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPMissingReference"/> and <see cref="PCPSeverity"/>.
    /// </summary>
    [TestFixture]
    public sealed class PCPMissingReferenceTests
    {
        // ================================================================
        // 1. GET SUMMARY
        // ================================================================

        [Test]
        public void GetSummary_WithGameObjectPath_IncludesArrow()
        {
            var mr = new PCPMissingReference
            {
                sourceAssetPath = "Assets/Prefabs/Player.prefab",
                sourceAssetName = "Player",
                componentType = "MeshRenderer",
                propertyPath = "m_Materials.Array.data[0]",
                severity = PCPSeverity.Warning,
                gameObjectPath = "Root/Mesh"
            };

            string summary = mr.GetSummary();
            Assert.IsTrue(summary.Contains("->"), "Should contain arrow for gameObject path");
            Assert.IsTrue(summary.Contains("Root/Mesh"));
            Assert.IsTrue(summary.Contains("[Warning]"));
            Assert.IsTrue(summary.Contains("MeshRenderer"));
        }

        [Test]
        public void GetSummary_WithoutGameObjectPath_UsesAssetPathOnly()
        {
            var mr = new PCPMissingReference
            {
                sourceAssetPath = "Assets/Data/config.asset",
                sourceAssetName = "config",
                componentType = "MyScriptableObject",
                propertyPath = "m_Reference",
                severity = PCPSeverity.Error,
                gameObjectPath = ""
            };

            string summary = mr.GetSummary();
            Assert.IsFalse(summary.Contains("->"), "Should not contain arrow without gameObject path");
            Assert.IsTrue(summary.Contains("Assets/Data/config.asset"));
            Assert.IsTrue(summary.Contains("[Error]"));
        }

        [Test]
        public void GetSummary_NullGameObjectPath_UsesAssetPathOnly()
        {
            var mr = new PCPMissingReference
            {
                sourceAssetPath = "Assets/test.asset",
                componentType = "Component",
                propertyPath = "m_Field",
                severity = PCPSeverity.Info,
                gameObjectPath = null
            };

            string summary = mr.GetSummary();
            Assert.IsFalse(summary.Contains("->"));
            Assert.IsTrue(summary.Contains("[Info]"));
        }

        [Test]
        public void GetSummary_ContainsPropertyPath()
        {
            var mr = new PCPMissingReference
            {
                sourceAssetPath = "Assets/test.prefab",
                componentType = "BoxCollider",
                propertyPath = "m_Material",
                severity = PCPSeverity.Warning,
                gameObjectPath = ""
            };

            Assert.IsTrue(mr.GetSummary().Contains("m_Material"));
        }

        // ================================================================
        // 2. TOSTRING
        // ================================================================

        [Test]
        public void ToString_EqualsGetSummary()
        {
            var mr = new PCPMissingReference
            {
                sourceAssetPath = "Assets/test.prefab",
                componentType = "Renderer",
                propertyPath = "m_Mesh",
                severity = PCPSeverity.Error,
                gameObjectPath = "Root"
            };

            Assert.AreEqual(mr.GetSummary(), mr.ToString());
        }

        // ================================================================
        // 3. SEVERITY ENUM
        // ================================================================

        [Test]
        public void Severity_Info_HasExpectedValue()
        {
            Assert.AreEqual(0, (int)PCPSeverity.Info);
        }

        [Test]
        public void Severity_Warning_HasExpectedValue()
        {
            Assert.AreEqual(1, (int)PCPSeverity.Warning);
        }

        [Test]
        public void Severity_Error_HasExpectedValue()
        {
            Assert.AreEqual(2, (int)PCPSeverity.Error);
        }

        // ================================================================
        // 4. ALL SEVERITY LEVELS IN SUMMARY
        // ================================================================

        [TestCase(PCPSeverity.Info, "[Info]")]
        [TestCase(PCPSeverity.Warning, "[Warning]")]
        [TestCase(PCPSeverity.Error, "[Error]")]
        public void GetSummary_AllSeverities_ShowCorrectTag(PCPSeverity severity, string expectedTag)
        {
            var mr = new PCPMissingReference
            {
                sourceAssetPath = "Assets/test.prefab",
                componentType = "Component",
                propertyPath = "m_Field",
                severity = severity,
                gameObjectPath = ""
            };

            Assert.IsTrue(mr.GetSummary().Contains(expectedTag));
        }
    }
}
