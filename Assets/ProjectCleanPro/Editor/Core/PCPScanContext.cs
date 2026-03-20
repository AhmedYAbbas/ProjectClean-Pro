using System;
using System.Collections.Generic;
using System.Threading;

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
        /// The resolved asset dependency graph.
        /// </summary>
        public PCPDependencyResolver DependencyResolver { get; }

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
        /// <param name="dependencyResolver">The built dependency graph.</param>
        /// <param name="cache">Incremental scan cache.</param>
        /// <param name="renderPipeline">Detected render pipeline info.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public PCPScanContext(
            PCPSettings settings,
            PCPIgnoreRules ignoreRules,
            PCPDependencyResolver dependencyResolver,
            PCPScanCache cache,
            PCPRenderPipelineInfo renderPipeline,
            Action<float, string> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            IgnoreRules = ignoreRules ?? throw new ArgumentNullException(nameof(ignoreRules));
            DependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            RenderPipeline = renderPipeline ?? throw new ArgumentNullException(nameof(renderPipeline));
            OnProgress = onProgress;
            CancellationToken = cancellationToken;
            AlwaysUsedRoots = new List<string>();
        }

        /// <summary>
        /// Creates a new scan context with all required dependencies including
        /// backward-compatible properties.
        /// </summary>
        public PCPScanContext(
            PCPSettings settings,
            PCPDependencyResolver dependencyResolver,
            PCPScanCache scanCache,
            PCPIgnoreRules ignoreRules,
            PCPRenderPipelineDetector renderPipelineDetector,
            IReadOnlyList<string> alwaysUsedRoots = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            IgnoreRules = ignoreRules ?? throw new ArgumentNullException(nameof(ignoreRules));
            DependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
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

        /// <summary>
        /// Convenience factory that pulls everything from <see cref="PCPContext"/>.
        /// </summary>
        public static PCPScanContext FromGlobalContext(IReadOnlyList<string> alwaysUsedRoots = null)
        {
            PCPContext.Initialize();
            return new PCPScanContext(
                PCPContext.Settings,
                PCPContext.DependencyResolver,
                PCPContext.ScanCache,
                PCPContext.IgnoreRules,
                PCPContext.RenderPipelineDetector,
                alwaysUsedRoots);
        }
    }
}
