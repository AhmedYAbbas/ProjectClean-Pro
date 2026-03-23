using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Abstract base class for all PCP scanner modules.
    /// Provides the async scan lifecycle, progress reporting, cancellation,
    /// ignore-rule evaluation, and editor yielding so concrete modules only
    /// need to implement <see cref="DoScanAsync"/>, result properties,
    /// incremental declarations, and binary serialization.
    /// </summary>
    public abstract class PCPModuleBase : IPCPModule
    {
        // ----------------------------------------------------------------
        // Identity (set by subclass)
        // ----------------------------------------------------------------

        public abstract PCPModuleId Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Icon { get; }
        public abstract Color AccentColor { get; }

        // ----------------------------------------------------------------
        // Scan state
        // ----------------------------------------------------------------

        protected bool _isScanning;
        protected float _progress;
        protected string _progressLabel = string.Empty;

        public bool IsScanning => _isScanning;
        public float Progress => _progress;
        public string ProgressLabel => _progressLabel;

        // ----------------------------------------------------------------
        // Results (subclass must implement)
        // ----------------------------------------------------------------

        public abstract int FindingCount { get; }
        public abstract long TotalSizeBytes { get; }
        public virtual bool HasResults => FindingCount > 0;

        // ----------------------------------------------------------------
        // Incremental support (subclass must implement)
        // ----------------------------------------------------------------

        /// <inheritdoc/>
        public abstract IReadOnlyCollection<string> RelevantExtensions { get; }

        /// <inheritdoc/>
        public abstract bool RequiresDependencyGraph { get; }

        // ----------------------------------------------------------------
        // Async scan lifecycle
        // ----------------------------------------------------------------

        /// <summary>
        /// Entry point called by the orchestrator. Sets up state, delegates
        /// to the subclass, and tears down on completion.
        /// </summary>
        public async Task ScanAsync(PCPScanContext context, CancellationToken ct)
        {
            if (_isScanning)
                return;

            _isScanning = true;
            _progress = 0f;
            _progressLabel = "Starting...";

            try
            {
                await DoScanAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                // Clean cancel — partial results may remain.
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectCleanPro] {DisplayName} scan failed: {ex}");
            }
            finally
            {
                _isScanning = false;
                _progress = 1f;
                _progressLabel = "Complete";
            }
        }

        /// <summary>
        /// Subclasses implement their full scan logic here.
        /// Use <see cref="YieldIfNeeded"/>, <see cref="ReportProgress"/>,
        /// and <see cref="IsIgnored"/> as helpers.
        /// </summary>
        protected abstract Task DoScanAsync(PCPScanContext context, CancellationToken ct);

        public void Cancel()
        {
            // Cancellation is handled via CancellationToken now.
            // This remains for backward compatibility.
        }

        public virtual void Clear()
        {
            _isScanning = false;
            _progress = 0f;
            _progressLabel = string.Empty;
        }

        // ----------------------------------------------------------------
        // Binary persistence (subclass should override)
        // ----------------------------------------------------------------

        public virtual void WriteResults(BinaryWriter writer)
        {
            // Default no-op. Subclasses override to serialize their results.
        }

        public virtual void ReadResults(BinaryReader reader)
        {
            // Default no-op. Subclasses override to deserialize their results.
        }

        // ----------------------------------------------------------------
        // Helpers for subclasses
        // ----------------------------------------------------------------

        /// <summary>
        /// Update the normalised progress and label visible to the UI.
        /// </summary>
        protected void ReportProgress(float progress, string label)
        {
            _progress = Mathf.Clamp01(progress);
            _progressLabel = label ?? string.Empty;
        }

        /// <summary>
        /// Call inside a tight asset-processing loop. Every <paramref name="interval"/>
        /// iterations it reports progress, checks for cancellation, and yields
        /// one editor frame so the UI stays responsive.
        /// </summary>
        protected async Task YieldIfNeeded(int index, int total, string label,
            CancellationToken ct, int interval = 64)
        {
            if (interval <= 0 || index % interval != 0)
                return;

            ReportProgress(total > 0 ? index / (float)total : 0f, label);
            ct.ThrowIfCancellationRequested();
            await PCPEditorAsync.YieldToEditor();
        }

        /// <summary>
        /// Returns true when <paramref name="path"/> matches any active ignore rule.
        /// </summary>
        protected bool IsIgnored(string path, PCPScanContext context)
        {
            if (context?.IgnoreRules == null)
                return false;
            return context.IgnoreRules.IsIgnored(path);
        }
    }
}
