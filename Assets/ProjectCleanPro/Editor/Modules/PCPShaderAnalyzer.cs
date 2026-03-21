using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 6 - Shader &amp; Variant Analyzer.
    /// Analyses all shaders in the project for variant counts, keyword usage,
    /// material assignments, and pipeline compatibility.
    /// </summary>
    public sealed class PCPShaderAnalyzer : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override string ModuleId => "shaders";
        public override string DisplayName => "Shaders";
        public override string Icon => "\u2726"; // ✦
        public override Color AccentColor => new Color(0.910f, 0.263f, 0.576f, 1f); // #E84393

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        private readonly List<PCPShaderEntry> _results = new List<PCPShaderEntry>();

        /// <summary>Read-only access to the scan results.</summary>
        public IReadOnlyList<PCPShaderEntry> Results => _results;

        public override int FindingCount => _results.Count;

        private long _totalSizeBytes;
        public override long TotalSizeBytes => _totalSizeBytes;

        // ----------------------------------------------------------------
        // Regex patterns for shader source parsing
        // ----------------------------------------------------------------

        /// <summary>Matches "Shader "ShaderName"" at the top of a shader file.</summary>
        private static readonly Regex s_ShaderNameRegex =
            new Regex(@"Shader\s+""([^""]+)""", RegexOptions.Compiled);

        /// <summary>Matches #pragma multi_compile or shader_feature directives.</summary>
        private static readonly Regex s_PragmaKeywordRegex =
            new Regex(@"#pragma\s+(multi_compile|multi_compile_local|shader_feature|shader_feature_local)\s+(.+)",
                RegexOptions.Compiled);

        /// <summary>Matches Pass { ... } blocks.</summary>
        private static readonly Regex s_PassBlockRegex =
            new Regex(@"\bPass\s*\{", RegexOptions.Compiled);

        /// <summary>Matches pipeline tags to detect URP or HDRP.</summary>
        private static readonly Regex s_PipelineTagRegex =
            new Regex(@"""(LightMode|RenderPipeline)""\s*=\s*""([^""]+)""",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override void DoScan(PCPScanContext context)
        {
            _results.Clear();
            _totalSizeBytes = 0;

            // ----------------------------------------------------------
            // Phase 1: Find all shader assets
            // ----------------------------------------------------------
            ReportProgress(0f, "Finding shaders...");

            string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");

            // Deduplicate and resolve paths.
            var shaderPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < shaderGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(shaderGuids[i]);
                if (!string.IsNullOrEmpty(path))
                    shaderPaths.Add(path);
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 2: Build material-to-shader mapping
            // ----------------------------------------------------------
            ReportProgress(0.05f, "Mapping materials to shaders...");

            // Map: shader name -> count of materials using it.
            var materialCountByShader = new Dictionary<string, int>(StringComparer.Ordinal);
            // Map: shader name -> list of material paths.
            var materialsByShader = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            int totalMats = matGuids.Length;

            for (int i = 0; i < totalMats; i++)
            {
                if (ShouldCancel()) return;

                if ((i & 63) == 0)
                {
                    float pct = 0.05f + 0.25f * ((float)i / Math.Max(totalMats, 1));
                    ReportProgress(pct, $"Scanning material {i}/{totalMats}...");
                }

                string matPath = AssetDatabase.GUIDToAssetPath(matGuids[i]);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null || mat.shader == null)
                    continue;

                string shaderName = mat.shader.name;

                if (!materialCountByShader.ContainsKey(shaderName))
                {
                    materialCountByShader[shaderName] = 0;
                    materialsByShader[shaderName] = new List<string>();
                }
                materialCountByShader[shaderName]++;
                materialsByShader[shaderName].Add(matPath);
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 3: Determine the project's active render pipeline
            // ----------------------------------------------------------
            PCPRenderPipeline projectPipeline = PCPRenderPipeline.BuiltIn;
            if (context.RenderPipeline != null)
            {
                projectPipeline = context.RenderPipeline.Pipeline;
            }

            // ----------------------------------------------------------
            // Phase 4: Analyse each shader
            // ----------------------------------------------------------
            int shaderIndex = 0;
            int totalShaders = shaderPaths.Count;

            foreach (string shaderPath in shaderPaths)
            {
                if (ShouldCancel()) return;

                shaderIndex++;
                float pct = 0.3f + 0.65f * ((float)shaderIndex / Math.Max(totalShaders, 1));
                ReportProgress(pct, $"Analysing shader {shaderIndex}/{totalShaders}...");

                var entry = AnalyseShader(shaderPath, materialCountByShader, projectPipeline);
                if (entry != null)
                {
                    _results.Add(entry);
                    _totalSizeBytes += entry.sizeBytes;
                }
            }

            // ----------------------------------------------------------
            // Phase 5: Sort by severity (errors first, then warnings, then info)
            // ----------------------------------------------------------
            ReportProgress(0.95f, "Sorting results...");
            _results.Sort((a, b) =>
            {
                int sevCmp = GetSeverityPriority(a.GetSeverity()).CompareTo(
                    GetSeverityPriority(b.GetSeverity()));
                if (sevCmp != 0)
                    return sevCmp;
                return b.estimatedVariants.CompareTo(a.estimatedVariants);
            });

            ReportProgress(1f, $"Analysed {_results.Count} shaders.");
        }

        /// <summary>
        /// Analyses a single shader asset and returns a populated <see cref="PCPShaderEntry"/>.
        /// </summary>
        private PCPShaderEntry AnalyseShader(
            string shaderPath,
            Dictionary<string, int> materialCountByShader,
            PCPRenderPipeline projectPipeline)
        {
            // Load the shader object.
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
                return null;

            var entry = new PCPShaderEntry
            {
                shaderName = shader.name,
                assetPath = shaderPath,
                materialCount = 0,
                targetPipeline = PCPRenderPipeline.Custom,
                pipelineMismatch = false,
                isUnused = false
            };

            // Material count.
            if (materialCountByShader.TryGetValue(shader.name, out int matCount))
            {
                entry.materialCount = matCount;
            }

            // ----------------------------------------------------------
            // Parse shader source file (only for .shader files on disk)
            // ----------------------------------------------------------
            string ext = Path.GetExtension(shaderPath);
            bool isShaderFile = string.Equals(ext, ".shader", StringComparison.OrdinalIgnoreCase);

            // Calculate file size for any shader with an on-disk file.
            {
                string fullSizePath = Path.GetFullPath(shaderPath);
                if (File.Exists(fullSizePath))
                {
                    try
                    {
                        entry.sizeBytes = new FileInfo(fullSizePath).Length;
                    }
                    catch (Exception)
                    {
                        entry.sizeBytes = 0;
                    }
                }
            }

            if (isShaderFile)
            {
                string fullPath = Path.GetFullPath(shaderPath);
                if (File.Exists(fullPath))
                {
                    string sourceText;
                    try
                    {
                        sourceText = File.ReadAllText(fullPath);
                    }
                    catch (Exception)
                    {
                        sourceText = null;
                    }

                    if (!string.IsNullOrEmpty(sourceText))
                    {
                        ParseShaderSource(sourceText, entry);
                    }
                }
            }
            else
            {
                // For built-in or compiled shaders, we can still get the keyword count
                // from the Shader object via reflection or ShaderUtil if available.
                // Use a minimal fallback: count shader keywords from the Shader API.
                entry.passCount = shader.passCount;
                entry.estimatedVariants = 0;
                entry.keywordCount = 0;
            }

            // Detect pipeline from shader name if source parsing did not determine it.
            if (entry.targetPipeline == PCPRenderPipeline.Custom)
            {
                entry.targetPipeline = DetectPipelineFromName(shader.name);
            }

            // Flag unused shaders.
            entry.isUnused = entry.materialCount == 0;

            // Flag pipeline mismatch (only when the setting is enabled).
            if (PCPSettings.instance.shaderAnalyzerCheckPipeline &&
                entry.targetPipeline != PCPRenderPipeline.Custom &&
                projectPipeline != PCPRenderPipeline.Custom &&
                entry.targetPipeline != projectPipeline)
            {
                entry.pipelineMismatch = true;
            }

            return entry;
        }

        /// <summary>
        /// Parses shader source text to extract keywords, pass count, variant estimate,
        /// and pipeline target.
        /// </summary>
        private void ParseShaderSource(string sourceText, PCPShaderEntry entry)
        {
            // ----------------------------------------------------------
            // Count Pass blocks
            // ----------------------------------------------------------
            MatchCollection passMatches = s_PassBlockRegex.Matches(sourceText);
            entry.passCount = Math.Max(passMatches.Count, 1);

            // ----------------------------------------------------------
            // Parse keyword pragmas
            // ----------------------------------------------------------
            var allKeywords = new List<string>();
            var keywordSetSizes = new List<int>();

            MatchCollection pragmaMatches = s_PragmaKeywordRegex.Matches(sourceText);
            foreach (Match match in pragmaMatches)
            {
                string keywordLine = match.Groups[2].Value.Trim();

                // Remove inline comments.
                int commentIdx = keywordLine.IndexOf("//");
                if (commentIdx >= 0)
                    keywordLine = keywordLine.Substring(0, commentIdx).Trim();

                string[] keywords = keywordLine.Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (keywords.Length > 0)
                {
                    keywordSetSizes.Add(keywords.Length);
                    for (int k = 0; k < keywords.Length; k++)
                    {
                        string kw = keywords[k].Trim();
                        if (kw.Length > 0 && kw != "_")
                            allKeywords.Add(kw);
                    }
                }
            }

            entry.keywords = allKeywords;
            entry.keywordCount = allKeywords.Count;

            // Estimated variant count = product of keyword counts per directive * pass count.
            long variantEstimate = 1;
            for (int i = 0; i < keywordSetSizes.Count; i++)
            {
                variantEstimate *= keywordSetSizes[i];
                // Cap to avoid overflow.
                if (variantEstimate > int.MaxValue)
                {
                    variantEstimate = int.MaxValue;
                    break;
                }
            }
            variantEstimate *= entry.passCount;
            if (variantEstimate > int.MaxValue)
                variantEstimate = int.MaxValue;

            entry.estimatedVariants = (int)variantEstimate;

            // ----------------------------------------------------------
            // Detect pipeline from source tags
            // ----------------------------------------------------------
            MatchCollection tagMatches = s_PipelineTagRegex.Matches(sourceText);
            foreach (Match match in tagMatches)
            {
                string tagValue = match.Groups[2].Value;

                if (tagValue.IndexOf("UniversalPipeline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tagValue.IndexOf("UniversalForward", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tagValue.IndexOf("SRPDefaultUnlit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    entry.targetPipeline = PCPRenderPipeline.URP;
                    return;
                }

                if (tagValue.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tagValue.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    entry.targetPipeline = PCPRenderPipeline.HDRP;
                    return;
                }
            }

            // Check for URP/HDRP include directives as a secondary heuristic.
            if (sourceText.IndexOf("Packages/com.unity.render-pipelines.universal",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                entry.targetPipeline = PCPRenderPipeline.URP;
            }
            else if (sourceText.IndexOf("Packages/com.unity.render-pipelines.high-definition",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                entry.targetPipeline = PCPRenderPipeline.HDRP;
            }
            else if (sourceText.IndexOf("UnityStandardBRDF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     sourceText.IndexOf("UNITY_PASS_FORWARDBASE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                entry.targetPipeline = PCPRenderPipeline.BuiltIn;
            }
        }

        /// <summary>
        /// Infers the target pipeline from the shader's display name.
        /// </summary>
        private static PCPRenderPipeline DetectPipelineFromName(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName))
                return PCPRenderPipeline.Custom;

            if (shaderName.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("Shader Graphs/", StringComparison.OrdinalIgnoreCase))
                return PCPRenderPipeline.URP;

            if (shaderName.StartsWith("HDRP/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("High Definition/", StringComparison.OrdinalIgnoreCase))
                return PCPRenderPipeline.HDRP;

            if (shaderName.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("Legacy Shaders/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("Mobile/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("Particles/", StringComparison.OrdinalIgnoreCase))
                return PCPRenderPipeline.BuiltIn;

            return PCPRenderPipeline.Custom;
        }

        public override void Clear()
        {
            base.Clear();
            _results.Clear();
            _totalSizeBytes = 0;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static int GetSeverityPriority(PCPSeverity severity)
        {
            switch (severity)
            {
                case PCPSeverity.Error: return 0;
                case PCPSeverity.Warning: return 1;
                case PCPSeverity.Info: return 2;
                default: return 3;
            }
        }
    }
}
