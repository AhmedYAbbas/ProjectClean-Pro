using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

        public override PCPModuleId Id => PCPModuleId.Shaders;
        public override string DisplayName => "Shaders";
        public override string Icon => "\u2726"; // ✦
        public override Color AccentColor => new Color(0.910f, 0.263f, 0.576f, 1f); // #E84393

        private static readonly HashSet<string> s_RelevantExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".shader", ".mat", ".shadergraph", ".shadersubgraph", ".compute"
        };

        public override IReadOnlyCollection<string> RelevantExtensions => s_RelevantExtensions;
        public override bool RequiresDependencyGraph => false;

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

        protected override async Task DoScanAsync(PCPScanContext context, CancellationToken ct)
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

            ct.ThrowIfCancellationRequested();

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
            int loadedCount = 0;

            for (int i = 0; i < totalMats; i++)
            {
                ct.ThrowIfCancellationRequested();

                if ((i & 63) == 0)
                {
                    float pct = 0.05f + 0.25f * ((float)i / Math.Max(totalMats, 1));
                    ReportProgress(pct, $"Scanning material {i}/{totalMats}...");
                }

                string matPath = AssetDatabase.GUIDToAssetPath(matGuids[i]);
                if (string.IsNullOrEmpty(matPath))
                    continue;

                // Try cached shader name for non-stale materials.
                string shaderName = null;
                if (!context.Cache.IsStale(matPath))
                {
                    shaderName = context.Cache.GetMetadata(matPath, "mat.shader");
                }

                if (shaderName == null)
                {
                    // Must load the material to get its shader name.
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null || mat.shader == null)
                        continue;

                    shaderName = mat.shader.name;
                    context.Cache.SetMetadata(matPath, "mat.shader", shaderName);
                    loadedCount++;
                }

                if (!materialCountByShader.ContainsKey(shaderName))
                {
                    materialCountByShader[shaderName] = 0;
                    materialsByShader[shaderName] = new List<string>();
                }
                materialCountByShader[shaderName]++;
                materialsByShader[shaderName].Add(matPath);
            }

            ct.ThrowIfCancellationRequested();

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
                ct.ThrowIfCancellationRequested();

                shaderIndex++;
                float pct = 0.3f + 0.65f * ((float)shaderIndex / Math.Max(totalShaders, 1));
                ReportProgress(pct, $"Analysing shader {shaderIndex}/{totalShaders}...");

                var entry = await AnalyseShader(shaderPath, materialCountByShader, projectPipeline,
                    context.Cache);
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
        private async Task<PCPShaderEntry> AnalyseShader(
            string shaderPath,
            Dictionary<string, int> materialCountByShader,
            PCPRenderPipeline projectPipeline,
            PCPScanCache cache)
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

            // Calculate file size — try cache first.
            long cachedSize = cache.GetFileSize(shaderPath);
            if (cachedSize > 0 && !cache.IsStale(shaderPath))
            {
                entry.sizeBytes = cachedSize;
            }
            else
            {
                string fullSizePath = Path.GetFullPath(shaderPath);
                if (File.Exists(fullSizePath))
                {
                    try
                    {
                        entry.sizeBytes = new FileInfo(fullSizePath).Length;
                        cache.SetFileSize(shaderPath, entry.sizeBytes);
                    }
                    catch (Exception)
                    {
                        entry.sizeBytes = 0;
                    }
                }
            }

            if (isShaderFile)
            {
                // Try cached shader parse results for unchanged .shader files.
                bool usedCache = false;
                if (!cache.IsStale(shaderPath))
                {
                    string cachedKeywords = cache.GetMetadata(shaderPath, "shader.keywords");
                    string cachedPassCount = cache.GetMetadata(shaderPath, "shader.passCount");
                    string cachedVariants = cache.GetMetadata(shaderPath, "shader.variants");

                    if (cachedKeywords != null && cachedPassCount != null && cachedVariants != null)
                    {
                        entry.keywords = cachedKeywords.Length > 0
                            ? new List<string>(cachedKeywords.Split(','))
                            : new List<string>();
                        entry.keywordCount = entry.keywords.Count;
                        int.TryParse(cachedPassCount, out entry.passCount);
                        int.TryParse(cachedVariants, out entry.estimatedVariants);
                        usedCache = true;
                    }
                }

                if (!usedCache)
                {
                    string fullPath = Path.GetFullPath(shaderPath);
                    if (File.Exists(fullPath))
                    {
                        string sourceText;
                        try
                        {
                            // Read shader source on a background thread to avoid blocking the main thread.
                            sourceText = await Task.Run(() => File.ReadAllText(fullPath));
                        }
                        catch (Exception)
                        {
                            sourceText = null;
                        }

                        if (!string.IsNullOrEmpty(sourceText))
                        {
                            ParseShaderSource(sourceText, entry);

                            // Store parse results in cache.
                            cache.SetMetadata(shaderPath, "shader.keywords",
                                entry.keywords != null ? string.Join(",", entry.keywords) : "");
                            cache.SetMetadata(shaderPath, "shader.passCount",
                                entry.passCount.ToString());
                            cache.SetMetadata(shaderPath, "shader.variants",
                                entry.estimatedVariants.ToString());
                        }
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
        // Binary serialization
        // ----------------------------------------------------------------

        public override void WriteResults(BinaryWriter writer)
        {
            writer.Write(_results.Count);
            for (int i = 0; i < _results.Count; i++)
            {
                var s = _results[i];
                writer.Write(s.shaderName ?? string.Empty);
                writer.Write(s.assetPath ?? string.Empty);
                writer.Write(s.estimatedVariants);
                writer.Write(s.passCount);
                writer.Write(s.keywordCount);
                writer.Write(s.materialCount);
                writer.Write(s.sizeBytes);
                writer.Write((byte)s.targetPipeline);
                writer.Write(s.pipelineMismatch);
                writer.Write(s.isUnused);
                writer.Write(s.keywords?.Count ?? 0);
                if (s.keywords != null)
                    for (int j = 0; j < s.keywords.Count; j++)
                        writer.Write(s.keywords[j] ?? string.Empty);
            }
        }

        public override void ReadResults(BinaryReader reader)
        {
            _results.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var s = new PCPShaderEntry
                {
                    shaderName = reader.ReadString(),
                    assetPath = reader.ReadString(),
                    estimatedVariants = reader.ReadInt32(),
                    passCount = reader.ReadInt32(),
                    keywordCount = reader.ReadInt32(),
                    materialCount = reader.ReadInt32(),
                    sizeBytes = reader.ReadInt64(),
                    targetPipeline = (PCPRenderPipeline)reader.ReadByte(),
                    pipelineMismatch = reader.ReadBoolean(),
                    isUnused = reader.ReadBoolean()
                };
                int kwCount = reader.ReadInt32();
                s.keywords = new System.Collections.Generic.List<string>(kwCount);
                for (int j = 0; j < kwCount; j++)
                    s.keywords.Add(reader.ReadString());
                _results.Add(s);
            }
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
