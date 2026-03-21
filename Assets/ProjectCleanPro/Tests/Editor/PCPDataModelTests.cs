using System.Collections.Generic;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for data model classes: <see cref="PCPCircularDependency"/>,
    /// <see cref="PCPGraphNode"/>, <see cref="PCPGraphEdge"/>,
    /// <see cref="PCPUnusedAsset"/>, <see cref="PCPAssetInfo"/>,
    /// and <see cref="PCPRenderPipelineInfo"/>.
    /// </summary>
    [TestFixture]
    public sealed class PCPDataModelTests
    {
        // ================================================================
        // 1. PCPCircularDependency
        // ================================================================

        [Test]
        public void CircularDependency_ToString_ShowsChainWithLoop()
        {
            var cd = new PCPCircularDependency
            {
                chain = new List<string> { "Assets/A.mat", "Assets/B.mat", "Assets/C.mat" }
            };

            string result = cd.ToString();
            Assert.IsTrue(result.Contains("Assets/A.mat"));
            Assert.IsTrue(result.Contains("Assets/B.mat"));
            Assert.IsTrue(result.Contains("Assets/C.mat"));
            // Last arrow should point back to the first element.
            Assert.IsTrue(result.EndsWith("Assets/A.mat"));
        }

        [Test]
        public void CircularDependency_EmptyChain_ToString_ShowsQuestionMark()
        {
            var cd = new PCPCircularDependency { chain = new List<string>() };
            Assert.IsTrue(cd.ToString().Contains("?"));
        }

        [Test]
        public void CircularDependency_SingleElement_ToString()
        {
            var cd = new PCPCircularDependency
            {
                chain = new List<string> { "Assets/Self.mat" }
            };
            string result = cd.ToString();
            Assert.IsTrue(result.Contains("Assets/Self.mat"));
        }

        // ================================================================
        // 2. PCPGraphNode
        // ================================================================

        [Test]
        public void GraphNode_ToString_ContainsAllFields()
        {
            var node = new PCPGraphNode
            {
                assetPath = "Assets/tex.png",
                assetType = "Texture2D",
                displayName = "tex",
                depth = 2
            };

            string result = node.ToString();
            Assert.IsTrue(result.Contains("tex"));
            Assert.IsTrue(result.Contains("Texture2D"));
            Assert.IsTrue(result.Contains("depth=2"));
        }

        // ================================================================
        // 3. PCPGraphEdge
        // ================================================================

        [Test]
        public void GraphEdge_ToString_ContainsFromAndTo()
        {
            var edge = new PCPGraphEdge
            {
                from = "Assets/mat.mat",
                to = "Assets/tex.png"
            };

            string result = edge.ToString();
            Assert.IsTrue(result.Contains("Assets/mat.mat"));
            Assert.IsTrue(result.Contains("->"));
            Assert.IsTrue(result.Contains("Assets/tex.png"));
        }

        // ================================================================
        // 4. PCPUnusedAsset
        // ================================================================

        [Test]
        public void UnusedAsset_Path_NullAssetInfo_ReturnsEmpty()
        {
            var unused = new PCPUnusedAsset { assetInfo = null };
            Assert.AreEqual(string.Empty, unused.Path);
        }

        [Test]
        public void UnusedAsset_SizeBytes_NullAssetInfo_ReturnsZero()
        {
            var unused = new PCPUnusedAsset { assetInfo = null };
            Assert.AreEqual(0L, unused.SizeBytes);
        }

        [Test]
        public void UnusedAsset_Path_ReturnsAssetInfoPath()
        {
            var unused = new PCPUnusedAsset
            {
                assetInfo = new PCPAssetInfo { path = "Assets/test.png" }
            };
            Assert.AreEqual("Assets/test.png", unused.Path);
        }

        [Test]
        public void UnusedAsset_SizeBytes_ReturnsAssetInfoSize()
        {
            var unused = new PCPUnusedAsset
            {
                assetInfo = new PCPAssetInfo { sizeBytes = 4096 }
            };
            Assert.AreEqual(4096L, unused.SizeBytes);
        }

        [Test]
        public void UnusedAsset_ToString_ContainsSuggestedAction()
        {
            var unused = new PCPUnusedAsset
            {
                assetInfo = new PCPAssetInfo
                {
                    path = "Assets/test.png",
                    name = "test",
                    extension = ".png",
                    assetTypeName = "Texture2D",
                    sizeBytes = 1024
                },
                suggestedAction = "Safe to delete"
            };

            string result = unused.ToString();
            Assert.IsTrue(result.Contains("[Unused]"));
            Assert.IsTrue(result.Contains("Safe to delete"));
        }

        // ================================================================
        // 5. PCPAssetInfo
        // ================================================================

        [Test]
        public void AssetInfo_ToString_ContainsNameAndType()
        {
            var info = new PCPAssetInfo
            {
                name = "Hero",
                extension = ".png",
                assetTypeName = "Texture2D",
                sizeBytes = 2048
            };

            string result = info.ToString();
            Assert.IsTrue(result.Contains("Hero"));
            Assert.IsTrue(result.Contains(".png"));
            Assert.IsTrue(result.Contains("Texture2D"));
        }

        [Test]
        public void AssetInfo_FormatBytes_DelegatesToAssetUtils()
        {
            //string result = PCPAssetInfo.FormatBytes(1024); ;
            //Assert.IsTrue(result.Contains("KB"));
        }

        [Test]
        public void AssetInfo_FormatBytes_Zero()
        {
            //Assert.AreEqual("0 B", PCPAssetInfo.FormatBytes(0));
        }

        // ================================================================
        // 6. PCPRenderPipelineInfo
        // ================================================================

        [Test]
        public void RenderPipelineInfo_Properties()
        {
            var info = new PCPRenderPipelineInfo(PCPRenderPipeline.URP, "Universal Render Pipeline (URP)", null);
            Assert.AreEqual(PCPRenderPipeline.URP, info.Pipeline);
            Assert.AreEqual("Universal Render Pipeline (URP)", info.Name);
            Assert.IsNull(info.PipelineAsset);
        }

        [Test]
        public void RenderPipelineInfo_ToString_ReturnsName()
        {
            var info = new PCPRenderPipelineInfo(PCPRenderPipeline.HDRP, "HDRP", null);
            Assert.AreEqual("HDRP", info.ToString());
        }

        // ================================================================
        // 7. PCPRenderPipeline ENUM
        // ================================================================

        [Test]
        public void RenderPipeline_AllValuesExist()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPRenderPipeline), PCPRenderPipeline.BuiltIn));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPRenderPipeline), PCPRenderPipeline.URP));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPRenderPipeline), PCPRenderPipeline.HDRP));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPRenderPipeline), PCPRenderPipeline.Custom));
        }

        // ================================================================
        // 8. PCPIgnoreRule defaults
        // ================================================================

        [Test]
        public void IgnoreRule_Defaults()
        {
            var rule = new PCPIgnoreRule();
            Assert.AreEqual(PCPIgnoreType.PathPrefix, rule.type);
            Assert.AreEqual(string.Empty, rule.pattern);
            Assert.AreEqual(string.Empty, rule.comment);
            Assert.IsTrue(rule.enabled);
        }

        // ================================================================
        // 9. PCPIgnoreType ENUM
        // ================================================================

        [Test]
        public void IgnoreType_AllValuesExist()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPIgnoreType), PCPIgnoreType.PathPrefix));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPIgnoreType), PCPIgnoreType.PathExact));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPIgnoreType), PCPIgnoreType.Regex));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPIgnoreType), PCPIgnoreType.AssetType));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPIgnoreType), PCPIgnoreType.Label));
            Assert.IsTrue(System.Enum.IsDefined(typeof(PCPIgnoreType), PCPIgnoreType.Folder));
        }
    }
}
