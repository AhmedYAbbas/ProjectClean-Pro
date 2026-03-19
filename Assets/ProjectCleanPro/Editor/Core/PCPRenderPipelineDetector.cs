using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Identifies the active render pipeline in the current project.
    /// </summary>
    public enum PCPRenderPipeline
    {
        /// <summary>The legacy Built-in Render Pipeline.</summary>
        BuiltIn,

        /// <summary>Universal Render Pipeline (URP).</summary>
        URP,

        /// <summary>High Definition Render Pipeline (HDRP).</summary>
        HDRP,

        /// <summary>A custom or unrecognized Scriptable Render Pipeline.</summary>
        Custom,
    }

    /// <summary>
    /// Read-only snapshot of the detected render pipeline information.
    /// </summary>
    public sealed class PCPRenderPipelineInfo
    {
        /// <summary>
        /// The detected pipeline enum value.
        /// </summary>
        public PCPRenderPipeline Pipeline { get; }

        /// <summary>
        /// A human-readable name for the detected pipeline.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The <see cref="RenderPipelineAsset"/> assigned in Graphics Settings,
        /// or <c>null</c> for the Built-in pipeline.
        /// </summary>
        public RenderPipelineAsset PipelineAsset { get; }

        public PCPRenderPipelineInfo(PCPRenderPipeline pipeline, string name, RenderPipelineAsset asset)
        {
            Pipeline = pipeline;
            Name = name;
            PipelineAsset = asset;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Detects whether the project is using Built-in, URP, HDRP, or a custom SRP
    /// by inspecting <see cref="GraphicsSettings"/>.
    /// </summary>
    public sealed class PCPRenderPipelineDetector
    {
        private PCPRenderPipelineInfo m_CachedInfo;

        /// <summary>
        /// The most recently detected pipeline info. Calls <see cref="Detect"/>
        /// on first access.
        /// </summary>
        public PCPRenderPipelineInfo Info
        {
            get
            {
                if (m_CachedInfo == null)
                    m_CachedInfo = Detect();
                return m_CachedInfo;
            }
        }

        /// <summary>
        /// Shortcut to the detected pipeline enum value.
        /// </summary>
        public PCPRenderPipeline Pipeline => Info.Pipeline;

        /// <summary>
        /// Forces a fresh detection and updates the cached info.
        /// </summary>
        public PCPRenderPipelineInfo Refresh()
        {
            m_CachedInfo = Detect();
            return m_CachedInfo;
        }

        /// <summary>
        /// Detects the current render pipeline by examining Graphics Settings.
        /// </summary>
        /// <returns>A <see cref="PCPRenderPipelineInfo"/> describing the active pipeline.</returns>
        public static PCPRenderPipelineInfo Detect()
        {
            RenderPipelineAsset pipelineAsset = GetCurrentPipelineAsset();

            if (pipelineAsset == null)
                return new PCPRenderPipelineInfo(PCPRenderPipeline.BuiltIn, "Built-in Render Pipeline", null);

            // Use the fully-qualified type name to identify URP vs HDRP.
            string typeName = pipelineAsset.GetType().FullName ?? string.Empty;

            if (IsURP(typeName))
                return new PCPRenderPipelineInfo(PCPRenderPipeline.URP, "Universal Render Pipeline (URP)", pipelineAsset);

            if (IsHDRP(typeName))
                return new PCPRenderPipelineInfo(PCPRenderPipeline.HDRP, "High Definition Render Pipeline (HDRP)", pipelineAsset);

            // Some other custom SRP.
            string customName = pipelineAsset.GetType().Name;
            return new PCPRenderPipelineInfo(PCPRenderPipeline.Custom, $"Custom SRP ({customName})", pipelineAsset);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Retrieves the current <see cref="RenderPipelineAsset"/> from Graphics Settings,
        /// handling API differences between Unity versions.
        /// </summary>
        private static RenderPipelineAsset GetCurrentPipelineAsset()
        {
            // Unity 2022.1+ exposes GraphicsSettings.defaultRenderPipeline.
            // Earlier versions use GraphicsSettings.renderPipelineAsset.
            // We check via property to remain source-compatible across versions.
#if UNITY_2022_1_OR_NEWER
            RenderPipelineAsset asset = GraphicsSettings.defaultRenderPipeline;
            if (asset != null)
                return asset;
#endif
            // Fallback / pre-2022.
#pragma warning disable CS0618 // Suppress obsolete warning for older Unity compat.
            return GraphicsSettings.renderPipelineAsset;
#pragma warning restore CS0618
        }

        private static bool IsURP(string typeName)
        {
            // Known URP type names across Unity versions.
            return typeName.IndexOf("UniversalRenderPipelineAsset", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("LightweightRenderPipelineAsset", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHDRP(string typeName)
        {
            return typeName.IndexOf("HDRenderPipelineAsset", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
