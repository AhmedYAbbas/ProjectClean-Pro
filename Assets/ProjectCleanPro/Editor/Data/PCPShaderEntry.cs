using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Analysis result for a single shader in the project, including variant
    /// estimation, keyword usage, and pipeline compatibility.
    /// </summary>
    [Serializable]
    public class PCPShaderEntry
    {
        /// <summary>Display name of the shader (e.g. "Universal Render Pipeline/Lit").</summary>
        public string shaderName;

        /// <summary>
        /// Project-relative path to the shader asset. May be empty for built-in shaders
        /// that have no on-disk file.
        /// </summary>
        public string assetPath;

        /// <summary>
        /// Estimated total variant count based on keyword combinations and passes.
        /// This is an upper-bound estimate; actual compiled variants depend on stripping.
        /// </summary>
        public int estimatedVariants;

        /// <summary>Number of passes defined in the shader.</summary>
        public int passCount;

        /// <summary>Total number of shader keywords (local + global) declared.</summary>
        public int keywordCount;

        /// <summary>Number of materials in the project that reference this shader.</summary>
        public int materialCount;

        /// <summary>The render pipeline this shader is designed for.</summary>
        public PCPRenderPipeline targetPipeline;

        /// <summary>
        /// True if the shader's <see cref="targetPipeline"/> does not match the
        /// project's active render pipeline, indicating a likely misconfiguration.
        /// </summary>
        public bool pipelineMismatch;

        /// <summary>All shader keywords declared by this shader.</summary>
        public List<string> keywords = new List<string>();

        /// <summary>
        /// True if no material in the project references this shader and it is not
        /// included via the Always Included Shaders list.
        /// </summary>
        public bool isUnused;

        /// <summary>
        /// Returns a severity-like classification for UI display.
        /// </summary>
        public PCPSeverity GetSeverity()
        {
            if (pipelineMismatch)
                return PCPSeverity.Error;
            if (isUnused)
                return PCPSeverity.Warning;
            if (estimatedVariants > 10000)
                return PCPSeverity.Warning;
            return PCPSeverity.Info;
        }

        /// <summary>
        /// Returns a one-line summary suitable for list views and log output.
        /// </summary>
        public string GetSummary()
        {
            var mismatch = pipelineMismatch ? " [PIPELINE MISMATCH]" : "";
            var unused = isUnused ? " [UNUSED]" : "";
            return $"{shaderName} | {estimatedVariants} variants, {passCount} passes, " +
                   $"{keywordCount} keywords, {materialCount} materials | " +
                   $"{targetPipeline}{mismatch}{unused}";
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }
}
