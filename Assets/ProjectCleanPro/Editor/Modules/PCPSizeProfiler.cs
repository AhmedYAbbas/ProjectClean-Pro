using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module 7 - Size Profiler.
    /// Profiles every asset in the project by disk size, classifies by type,
    /// and suggests optimization opportunities for textures, audio, and meshes.
    /// </summary>
    public sealed class PCPSizeProfiler : PCPModuleBase
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        public override string ModuleId => "size";
        public override string DisplayName => "Size Profiler";
        public override string Icon => "\u2261"; // ≡
        public override Color AccentColor => new Color(0.153f, 0.682f, 0.376f, 1f); // #27AE60

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        private readonly List<PCPSizeEntry> _results = new List<PCPSizeEntry>();

        /// <summary>Read-only access to the scan results.</summary>
        public IReadOnlyList<PCPSizeEntry> Results => _results;

        /// <summary>Total project asset size in bytes.</summary>
        public long TotalProjectSize { get; private set; }

        public override int FindingCount => _results.Count;

        public override long TotalSizeBytes => TotalProjectSize;

        // ----------------------------------------------------------------
        // Size thresholds for optimization suggestions
        // ----------------------------------------------------------------

        private const long TextureCompressionThreshold = 1L * 1024L * 1024L; // 1 MB
        private const long AudioCompressionThreshold = 1L * 1024L * 1024L;   // 1 MB

        // ----------------------------------------------------------------
        // Scan implementation
        // ----------------------------------------------------------------

        protected override void DoScan(PCPScanContext context)
        {
            _results.Clear();
            TotalProjectSize = 0L;

            // ----------------------------------------------------------
            // Phase 1: Get all project asset paths
            // ----------------------------------------------------------
            ReportProgress(0f, "Collecting project assets...");

            string[] allPaths = PCPAssetUtils.GetAllProjectAssets();

            if (allPaths == null || allPaths.Length == 0)
            {
                ReportProgress(1f, "No assets found.");
                return;
            }

            int total = allPaths.Length;

            // ----------------------------------------------------------
            // Phase 2: Gather size and type info for each asset
            // ----------------------------------------------------------
            for (int i = 0; i < total; i++)
            {
                if (ShouldCancel()) return;

                string assetPath = allPaths[i];

                // Report progress every 128 assets.
                if ((i & 127) == 0)
                {
                    float pct = 0.7f * ((float)i / total);
                    ReportProgress(pct, $"Profiling asset {i}/{total}...");
                }

                // Skip ignored paths.
                if (IsIgnored(assetPath, context))
                    continue;

                // Get file info.
                string fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                    continue;

                long sizeBytes;
                try
                {
                    sizeBytes = new FileInfo(fullPath).Length;
                }
                catch (Exception)
                {
                    continue;
                }

                if (sizeBytes <= 0)
                    continue;

                // Get asset type.
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                string typeName = assetType != null ? assetType.Name : ClassifyByExtension(assetPath);

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string folderPath = Path.GetDirectoryName(assetPath)?
                    .Replace('\\', '/') ?? string.Empty;

                var entry = new PCPSizeEntry
                {
                    path = assetPath,
                    name = fileName,
                    assetTypeName = typeName,
                    folderPath = folderPath,
                    sizeBytes = sizeBytes,
                    compressionInfo = string.Empty,
                    optimizationSuggestion = string.Empty,
                    hasOptimizationSuggestion = false
                };

                // ----------------------------------------------------------
                // Check for optimization opportunities by asset type
                // ----------------------------------------------------------
                if (assetType != null)
                {
                    if (IsTextureType(assetType))
                    {
                        AnalyseTexture(assetPath, sizeBytes, entry);
                    }
                    else if (IsAudioType(assetType))
                    {
                        AnalyseAudio(assetPath, sizeBytes, entry);
                    }
                    else if (IsMeshType(assetType))
                    {
                        AnalyseMesh(assetPath, entry);
                    }
                }

                _results.Add(entry);
                TotalProjectSize += sizeBytes;
            }

            if (ShouldCancel()) return;

            // ----------------------------------------------------------
            // Phase 3: Calculate percentage of total for each entry
            // ----------------------------------------------------------
            ReportProgress(0.85f, "Calculating size distribution...");

            if (TotalProjectSize > 0)
            {
                for (int i = 0; i < _results.Count; i++)
                {
                    _results[i].percentOfTotal =
                        (float)((double)_results[i].sizeBytes / TotalProjectSize * 100.0);
                }
            }

            // ----------------------------------------------------------
            // Phase 4: Sort by size descending
            // ----------------------------------------------------------
            ReportProgress(0.95f, "Sorting results...");
            _results.Sort((a, b) => b.sizeBytes.CompareTo(a.sizeBytes));

            ReportProgress(1f,
                $"Profiled {_results.Count} assets, total {PCPAssetInfo.FormatBytes(TotalProjectSize)}.");
        }

        // ----------------------------------------------------------------
        // Texture analysis
        // ----------------------------------------------------------------

        /// <summary>
        /// Checks texture import settings for optimization opportunities.
        /// </summary>
        private void AnalyseTexture(string assetPath, long sizeBytes, PCPSizeEntry entry)
        {
            TextureImporter importer;
            try
            {
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            }
            catch (Exception)
            {
                return;
            }

            if (importer == null)
                return;

            int maxSize = importer.maxTextureSize;
            var compression = importer.textureCompression;
            bool crunched = importer.crunchedCompression;

            // Build compression info string.
            entry.compressionInfo = $"{compression}, max {maxSize}px" +
                                    (crunched ? ", crunched" : "");

            // Suggestion: enable compression for large uncompressed textures.
            if (compression == TextureImporterCompression.Uncompressed &&
                sizeBytes > TextureCompressionThreshold)
            {
                entry.hasOptimizationSuggestion = true;
                entry.optimizationSuggestion =
                    $"Texture is uncompressed ({PCPAssetInfo.FormatBytes(sizeBytes)}) - " +
                    "consider enabling compression to reduce build size.";
                return;
            }

            // Suggestion: reduce max texture size if it seems excessive.
            if (maxSize >= 4096 && sizeBytes > TextureCompressionThreshold)
            {
                entry.hasOptimizationSuggestion = true;
                entry.optimizationSuggestion =
                    $"Max texture size is {maxSize}px - consider reducing if full " +
                    "resolution is not needed.";
                return;
            }

            // Suggestion: enable crunch compression for non-crunched compressed textures.
            if (!crunched &&
                compression != TextureImporterCompression.Uncompressed &&
                sizeBytes > TextureCompressionThreshold)
            {
                entry.hasOptimizationSuggestion = true;
                entry.optimizationSuggestion =
                    "Consider enabling crunch compression for additional size savings.";
            }
        }

        // ----------------------------------------------------------------
        // Audio analysis
        // ----------------------------------------------------------------

        /// <summary>
        /// Checks audio import settings for optimization opportunities.
        /// </summary>
        private void AnalyseAudio(string assetPath, long sizeBytes, PCPSizeEntry entry)
        {
            AudioImporter importer;
            try
            {
                importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            }
            catch (Exception)
            {
                return;
            }

            if (importer == null)
                return;

            var defaultSettings = importer.defaultSampleSettings;
            var compressionFormat = defaultSettings.compressionFormat;
            bool loadInBackground = defaultSettings.loadType ==
                                    AudioClipLoadType.Streaming;

            entry.compressionInfo = $"{compressionFormat}" +
                                    (loadInBackground ? ", streaming" : "");

            // Suggestion: use compressed format for large uncompressed audio.
            if ((compressionFormat == AudioCompressionFormat.PCM ||
                 compressionFormat == AudioCompressionFormat.ADPCM) &&
                sizeBytes > AudioCompressionThreshold)
            {
                entry.hasOptimizationSuggestion = true;
                entry.optimizationSuggestion =
                    $"Audio uses {compressionFormat} format ({PCPAssetInfo.FormatBytes(sizeBytes)}) - " +
                    "consider using Vorbis or AAC compression for non-critical audio.";
                return;
            }

            // Suggestion: enable streaming for very large audio files.
            if (!loadInBackground && sizeBytes > AudioCompressionThreshold * 5)
            {
                entry.hasOptimizationSuggestion = true;
                entry.optimizationSuggestion =
                    "Large audio file - consider enabling streaming to reduce memory usage.";
            }
        }

        // ----------------------------------------------------------------
        // Mesh / Model analysis
        // ----------------------------------------------------------------

        /// <summary>
        /// Checks model import settings for optimization opportunities.
        /// </summary>
        private void AnalyseMesh(string assetPath, PCPSizeEntry entry)
        {
            ModelImporter importer;
            try
            {
                importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            }
            catch (Exception)
            {
                return;
            }

            if (importer == null)
                return;

            bool isReadable = importer.isReadable;
            var meshCompression = importer.meshCompression;

            entry.compressionInfo = $"Compression: {meshCompression}" +
                                    (isReadable ? ", Read/Write enabled" : "");

            // Suggestion: disable Read/Write if not needed.
            if (isReadable)
            {
                entry.hasOptimizationSuggestion = true;
                entry.optimizationSuggestion =
                    "Mesh has Read/Write enabled - disable it if runtime mesh " +
                    "access is not needed to halve memory usage.";
                return;
            }

            // Suggestion: enable mesh compression.
            if (meshCompression == ModelImporterMeshCompression.Off)
            {
                entry.hasOptimizationSuggestion = true;
                entry.optimizationSuggestion =
                    "Mesh compression is off - consider enabling it to reduce build size.";
            }
        }

        public override void Clear()
        {
            base.Clear();
            _results.Clear();
            TotalProjectSize = 0L;
        }

        // ----------------------------------------------------------------
        // Type classification helpers
        // ----------------------------------------------------------------

        private static bool IsTextureType(Type type)
        {
            return type == typeof(Texture2D) ||
                   type == typeof(Texture3D) ||
                   type == typeof(Cubemap) ||
                   type == typeof(RenderTexture) ||
                   (type != null && type.Name.Contains("Texture"));
        }

        private static bool IsAudioType(Type type)
        {
            return type == typeof(AudioClip) ||
                   (type != null && type.Name.Contains("AudioClip"));
        }

        private static bool IsMeshType(Type type)
        {
            // ModelImporter is used for .fbx, .obj, .dae, etc.
            // The main asset type for model files is typically GameObject.
            if (type == typeof(Mesh))
                return true;
            if (type == typeof(GameObject))
                return true; // Could be a model - we'll check for ModelImporter downstream.
            return false;
        }

        /// <summary>
        /// Fallback type classification based on file extension when
        /// AssetDatabase.GetMainAssetTypeAtPath returns null.
        /// </summary>
        private static string ClassifyByExtension(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return "Unknown";

            ext = ext.ToLowerInvariant();

            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".bmp":
                case ".psd":
                case ".tif":
                case ".tiff":
                case ".exr":
                case ".hdr":
                    return "Texture2D";

                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".aiff":
                case ".aif":
                case ".flac":
                    return "AudioClip";

                case ".fbx":
                case ".obj":
                case ".dae":
                case ".blend":
                case ".3ds":
                case ".max":
                    return "Model";

                case ".mat":
                    return "Material";

                case ".shader":
                case ".cginc":
                case ".hlsl":
                    return "Shader";

                case ".prefab":
                    return "Prefab";

                case ".unity":
                    return "Scene";

                case ".asset":
                    return "ScriptableObject";

                case ".anim":
                    return "AnimationClip";

                case ".controller":
                    return "AnimatorController";

                case ".ttf":
                case ".otf":
                    return "Font";

                case ".mp4":
                case ".mov":
                case ".avi":
                case ".webm":
                    return "VideoClip";

                default:
                    return "Unknown";
            }
        }
    }
}
