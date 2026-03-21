using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPAssetUtils"/> covering path validation, classification,
    /// scene detection, and size formatting.
    /// </summary>
    [TestFixture]
    public sealed class PCPAssetUtilsTests
    {
        // ================================================================
        // 1. IS SCENE PATH
        // ================================================================

        [Test]
        public void IsScenePath_UnityExtension_ReturnsTrue()
        {
            Assert.IsTrue(PCPAssetUtils.IsScenePath("Assets/Scenes/Main.unity"));
        }

        [Test]
        public void IsScenePath_OtherExtension_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsScenePath("Assets/Textures/bg.png"));
        }

        [Test]
        public void IsScenePath_CaseInsensitive()
        {
            Assert.IsTrue(PCPAssetUtils.IsScenePath("Assets/Scenes/Main.UNITY"));
        }

        [Test]
        public void IsScenePath_NullPath_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsScenePath(null));
        }

        [Test]
        public void IsScenePath_EmptyPath_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsScenePath(""));
        }

        // ================================================================
        // 2. IS EDITOR ONLY PATH
        // ================================================================

        [Test]
        public void IsEditorOnlyPath_TopLevelEditor_ReturnsTrue()
        {
            Assert.IsTrue(PCPAssetUtils.IsEditorOnlyPath("Assets/Editor/script.cs"));
        }

        [Test]
        public void IsEditorOnlyPath_NestedEditor_ReturnsTrue()
        {
            Assert.IsTrue(PCPAssetUtils.IsEditorOnlyPath("Assets/MyPlugin/Editor/script.cs"));
        }

        [Test]
        public void IsEditorOnlyPath_EditorInMiddle_ReturnsTrue()
        {
            Assert.IsTrue(PCPAssetUtils.IsEditorOnlyPath("Assets/Foo/Editor/Bar/script.cs"));
        }

        [Test]
        public void IsEditorOnlyPath_NoEditor_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsEditorOnlyPath("Assets/Scripts/MyScript.cs"));
        }

        [Test]
        public void IsEditorOnlyPath_EditorInFileName_ReturnsFalse()
        {
            // "EditorHelper.cs" is not under an Editor/ folder
            Assert.IsFalse(PCPAssetUtils.IsEditorOnlyPath("Assets/Scripts/EditorHelper.cs"));
        }

        [Test]
        public void IsEditorOnlyPath_NullPath_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsEditorOnlyPath(null));
        }

        [Test]
        public void IsEditorOnlyPath_EmptyPath_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsEditorOnlyPath(""));
        }

        [Test]
        public void IsEditorOnlyPath_BackslashPath_StillWorks()
        {
            Assert.IsTrue(PCPAssetUtils.IsEditorOnlyPath("Assets\\Editor\\script.cs"));
        }

        // ================================================================
        // 3. IS RESOURCES PATH
        // ================================================================

        [Test]
        public void IsResourcesPath_TopLevelResources_ReturnsTrue()
        {
            Assert.IsTrue(PCPAssetUtils.IsResourcesPath("Assets/Resources/prefab.prefab"));
        }

        [Test]
        public void IsResourcesPath_NestedResources_ReturnsTrue()
        {
            Assert.IsTrue(PCPAssetUtils.IsResourcesPath("Assets/Modules/Resources/data.json"));
        }

        [Test]
        public void IsResourcesPath_NoResources_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsResourcesPath("Assets/Textures/bg.png"));
        }

        [Test]
        public void IsResourcesPath_ResourcesInFileName_ReturnsFalse()
        {
            // "ResourcesManager.cs" is NOT under a Resources/ folder
            Assert.IsFalse(PCPAssetUtils.IsResourcesPath("Assets/Scripts/ResourcesManager.cs"));
        }

        [Test]
        public void IsResourcesPath_NullPath_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsResourcesPath(null));
        }

        [Test]
        public void IsResourcesPath_EmptyPath_ReturnsFalse()
        {
            Assert.IsFalse(PCPAssetUtils.IsResourcesPath(""));
        }

        // ================================================================
        // 4. FORMAT SIZE
        // ================================================================

        [Test]
        public void FormatSize_ZeroBytes()
        {
            Assert.AreEqual("0 B", PCPAssetUtils.FormatSize(0));
        }

        [Test]
        public void FormatSize_Bytes()
        {
            Assert.AreEqual("512 B", PCPAssetUtils.FormatSize(512));
        }

        [Test]
        public void FormatSize_Kilobytes()
        {
            string result = PCPAssetUtils.FormatSize(1024);
            Assert.IsTrue(result.Contains("KB"), $"Expected KB, got: {result}");
        }

        [Test]
        public void FormatSize_Megabytes()
        {
            string result = PCPAssetUtils.FormatSize(1024L * 1024L);
            Assert.IsTrue(result.Contains("MB"), $"Expected MB, got: {result}");
        }

        [Test]
        public void FormatSize_Gigabytes()
        {
            string result = PCPAssetUtils.FormatSize(1024L * 1024L * 1024L);
            Assert.IsTrue(result.Contains("GB"), $"Expected GB, got: {result}");
        }

        [Test]
        public void FormatSize_NegativeBytes_TreatedAsZero()
        {
            Assert.AreEqual("0 B", PCPAssetUtils.FormatSize(-100));
        }

        [Test]
        public void FormatSize_1023Bytes_StillBytes()
        {
            Assert.AreEqual("1023 B", PCPAssetUtils.FormatSize(1023));
        }

        [Test]
        public void FormatSize_SpecificKilobytes()
        {
            // 1536 bytes = 1.5 KB
            string result = PCPAssetUtils.FormatSize(1536);
            Assert.AreEqual("1.5 KB", result);
        }

        [Test]
        public void FormatSize_SpecificMegabytes()
        {
            // 2.5 MB
            string result = PCPAssetUtils.FormatSize((long)(2.5 * 1024 * 1024));
            Assert.AreEqual("2.5 MB", result);
        }

        // ================================================================
        // 5. GET ASSET TYPE
        // ================================================================

        [Test]
        public void GetAssetType_NullPath_ReturnsNull()
        {
            Assert.IsNull(PCPAssetUtils.GetAssetType(null));
        }

        [Test]
        public void GetAssetType_EmptyPath_ReturnsNull()
        {
            Assert.IsNull(PCPAssetUtils.GetAssetType(""));
        }
    }
}
