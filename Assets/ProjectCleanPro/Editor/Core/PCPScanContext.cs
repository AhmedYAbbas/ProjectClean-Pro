using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectCleanPro.Editor.Core;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Encapsulates all dependencies needed by a single scan session.
    /// Passed to scan modules so they have consistent access to settings,
    /// caching, dependency data, and cancellation support.
    /// </summary>
    public sealed class PCPScanContext
    {
        /// <summary>
        /// Project-wide settings for the current scan.
        /// </summary>
        public PCPSettings Settings { get; }

        /// <summary>
        /// Ignore-rule evaluator.
        /// </summary>
        public PCPIgnoreRules IgnoreRules { get; }

        /// <summary>
        /// Dependency resolver — interface, implementation varies by scan mode.
        /// </summary>
        public IPCPDependencyResolver DependencyResolver { get; set; }

        /// <summary>
        /// Incremental scan cache for skipping unchanged assets.
        /// </summary>
        public PCPScanCache Cache { get; }

        /// <summary>
        /// Alias for <see cref="Cache"/> for backward compatibility.
        /// </summary>
        public PCPScanCache ScanCache => Cache;

        /// <summary>
        /// Information about the project's active render pipeline.
        /// </summary>
        public PCPRenderPipelineInfo RenderPipeline { get; }

        /// <summary>
        /// The render pipeline detector instance for backward compatibility.
        /// </summary>
        public PCPRenderPipelineDetector RenderPipelineDetector { get; }

        /// <summary>
        /// Additional asset or folder paths whose contents (and dependencies)
        /// are treated as used — they will never be flagged as unused.
        /// </summary>
        public IReadOnlyList<string> AlwaysUsedRoots { get; }

        /// <summary>The scan's work coordinator. Created per-scan, null before scan starts.</summary>
        public PCPAsyncScheduler Scheduler { get; set; }

        /// <summary>Shared GUID index for Fast/Balanced modes. Null in Accurate mode.</summary>
        public PCPGuidIndex GuidIndex { get; set; }

        // ----------------------------------------------------------------
        // Cached asset paths (computed once per scan session)
        // ----------------------------------------------------------------

        private string[] m_AllProjectAssets;
        private List<string> m_AllProjectAssetsAsync;
        private List<string> m_AllMetaFiles;
        private bool m_StalenessComputed;

        /// <summary>
        /// All project asset paths under Assets/, cached for the duration of this
        /// scan session. Modules should use this instead of calling
        /// <see cref="PCPAssetUtils.GetAllProjectAssets"/> directly.
        /// </summary>
        public string[] AllProjectAssets
        {
            get
            {
                if (m_AllProjectAssets == null)
                    m_AllProjectAssets = PCPAssetUtils.GetAllProjectAssets();
                return m_AllProjectAssets;
            }
        }

        /// <summary>
        /// Callback invoked by scan modules to report progress.
        /// Parameters: (progress 0-1, description string).
        /// </summary>
        public Action<float, string> OnProgress { get; set; }

        /// <summary>
        /// Token that scan modules should check periodically to support cancellation.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Creates a new scan context with all required dependencies.
        /// </summary>
        /// <param name="settings">Project-wide settings.</param>
        /// <param name="ignoreRules">Ignore-rule evaluator.</param>
        /// <param name="cache">Incremental scan cache.</param>
        /// <param name="renderPipeline">Detected render pipeline info.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public PCPScanContext(
            PCPSettings settings,
            PCPIgnoreRules ignoreRules,
            PCPScanCache cache,
            PCPRenderPipelineInfo renderPipeline,
            Action<float, string> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            IgnoreRules = ignoreRules ?? throw new ArgumentNullException(nameof(ignoreRules));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            RenderPipeline = renderPipeline ?? throw new ArgumentNullException(nameof(renderPipeline));
            OnProgress = onProgress;
            CancellationToken = cancellationToken;
            AlwaysUsedRoots = new List<string>();
        }

        /// <summary>
        /// Creates a new scan context with all required dependencies.
        /// </summary>
        public PCPScanContext(
            PCPSettings settings,
            PCPScanCache scanCache,
            PCPIgnoreRules ignoreRules,
            PCPRenderPipelineDetector renderPipelineDetector,
            IReadOnlyList<string> alwaysUsedRoots = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            IgnoreRules = ignoreRules ?? throw new ArgumentNullException(nameof(ignoreRules));
            Cache = scanCache ?? throw new ArgumentNullException(nameof(scanCache));
            RenderPipelineDetector = renderPipelineDetector;
            RenderPipeline = renderPipelineDetector?.Info;
            AlwaysUsedRoots = alwaysUsedRoots ?? new List<string>();
            CancellationToken = default;
        }

        /// <summary>
        /// Reports progress if a callback is registered.
        /// </summary>
        /// <param name="progress">Normalized progress value (0 to 1).</param>
        /// <param name="description">Human-readable description of the current step.</param>
        public void ReportProgress(float progress, string description)
        {
            OnProgress?.Invoke(progress, description);
        }

        /// <summary>
        /// Throws <see cref="OperationCanceledException"/> if cancellation has been requested.
        /// </summary>
        public void ThrowIfCancelled()
        {
            CancellationToken.ThrowIfCancellationRequested();
        }

        // ----------------------------------------------------------------
        // Scan lifecycle helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Pre-computes asset staleness at most once per context lifetime.
        /// All code paths that need staleness information should call this
        /// instead of <c>Cache.RefreshStaleness</c> directly.
        /// </summary>
        public void EnsureStaleness()
        {
            if (m_StalenessComputed)
                return;

            Cache.RefreshStaleness(AllProjectAssets);
            m_StalenessComputed = true;
        }

        /// <summary>
        /// Pre-computes staleness and module dirtiness in one pass.
        /// Modules declare their relevant extensions via
        /// <see cref="IPCPModule.RelevantExtensions"/>.
        /// </summary>
        public void EnsureStaleness(IReadOnlyList<IPCPModule> modules)
        {
            if (m_StalenessComputed)
                return;

            Cache.RefreshStaleness(AllProjectAssets);
            if (modules != null)
                Cache.ComputeModuleDirtiness(modules);
            m_StalenessComputed = true;
        }

        /// <summary>
        /// Stamps modified assets, persists the cache, and resets the change
        /// tracker. Call once at the end of any scan operation.
        /// </summary>
        public void FinalizeScan()
        {
            Cache.StampStaleAssets(AllProjectAssets);
            Cache.Save();
            PCPAssetChangeTracker.Reset();
        }

        // ----------------------------------------------------------------
        // Async asset listing (background I/O)
        // ----------------------------------------------------------------

        /// <summary>
        /// Get all project asset paths asynchronously via System.IO (background thread).
        /// Cached for the scan session.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetAllProjectAssetsAsync(CancellationToken ct)
        {
            if (m_AllProjectAssetsAsync != null) return m_AllProjectAssetsAsync;

            var assetsDir = Path.GetFullPath("Assets");
            var projectRoot = Path.GetDirectoryName(assetsDir);

            m_AllProjectAssetsAsync = await Task.Run(() =>
            {
                return Directory.EnumerateFiles(assetsDir, "*.*", SearchOption.AllDirectories)
                    .Where(p => !p.EndsWith(".meta"))
                    .Select(p => p.Substring(projectRoot.Length + 1).Replace('\\', '/'))
                    .ToList();
            }, ct);

            return m_AllProjectAssetsAsync;
        }

        /// <summary>
        /// Get all .meta file paths asynchronously. Needed by PCPGuidIndex.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetAllMetaFilesAsync(CancellationToken ct)
        {
            if (m_AllMetaFiles != null) return m_AllMetaFiles;

            var assetsDir = Path.GetFullPath("Assets");
            var projectRoot = Path.GetDirectoryName(assetsDir);

            m_AllMetaFiles = await Task.Run(() =>
            {
                return Directory.EnumerateFiles(assetsDir, "*.meta", SearchOption.AllDirectories)
                    .Select(p => p.Substring(projectRoot.Length + 1).Replace('\\', '/'))
                    .ToList();
            }, ct);

            return m_AllMetaFiles;
        }

        /// <summary>
        /// Async version of FinalizeScan. Stamps and saves on background threads.
        /// </summary>
        public async Task FinalizeScanAsync(CancellationToken ct)
        {
            await Cache.StampProcessedAssetsAsync(ct);
            await Cache.SaveAsync(ct);
            PCPAssetChangeTracker.Reset();
        }

        // ----------------------------------------------------------------
        // Thread-safe progress reporting
        // ----------------------------------------------------------------

        private float m_ProgressFraction;
        private string m_ProgressLabel = string.Empty;

        /// <summary>Thread-safe progress reporting. Written from any thread, read by UI on main.</summary>
        public void ReportProgressThreadSafe(float fraction, string label)
        {
            Interlocked.Exchange(ref m_ProgressFraction, fraction);
            Volatile.Write(ref m_ProgressLabel, label);
        }

        public float ProgressFraction => Volatile.Read(ref m_ProgressFraction);
        public string ProgressLabelThreadSafe => Volatile.Read(ref m_ProgressLabel);

        /// <summary>
        /// Convenience factory that pulls everything from <see cref="PCPContext"/>.
        /// </summary>
        public static PCPScanContext FromGlobalContext(IReadOnlyList<string> alwaysUsedRoots = null)
        {
            PCPContext.Initialize();
            return new PCPScanContext(
                PCPContext.Settings,
                PCPContext.ScanCache,
                PCPContext.IgnoreRules,
                PCPContext.RenderPipelineDetector,
                alwaysUsedRoots);
        }
    }
}
