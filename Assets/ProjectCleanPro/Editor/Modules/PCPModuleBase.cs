using System;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Abstract base class for all PCP scanner modules.
    /// Provides shared scan lifecycle, progress reporting, cancellation,
    /// and ignore-rule evaluation so that concrete modules only need to
    /// implement <see cref="DoScan"/>, <see cref="FindingCount"/>,
    /// and <see cref="TotalSizeBytes"/>.
    /// </summary>
    public abstract class PCPModuleBase : IPCPModule
    {
        // ----------------------------------------------------------------
        // Identity (set by subclass constructor)
        // ----------------------------------------------------------------

        public abstract string ModuleId { get; }
        public abstract string DisplayName { get; }
        public abstract string Icon { get; }
        public abstract Color AccentColor { get; }

        // ----------------------------------------------------------------
        // Scan state
        // ----------------------------------------------------------------

        protected bool _isScanning;
        protected float _progress;
        protected string _progressLabel = string.Empty;
        protected bool _cancelled;

        public bool IsScanning => _isScanning;
        public float Progress => _progress;
        public string ProgressLabel => _progressLabel;

        public abstract int FindingCount { get; }
        public abstract long TotalSizeBytes { get; }

        // ----------------------------------------------------------------
        // Scan lifecycle
        // ----------------------------------------------------------------

        /// <summary>
        /// Entry point called by the orchestrator.  Sets up state,
        /// delegates to the subclass, and tears down state on completion.
        /// </summary>
        public void Scan(PCPScanContext context)
        {
            if (_isScanning)
                return;

            _isScanning = true;
            _cancelled = false;
            _progress = 0f;
            _progressLabel = "Starting...";

            try
            {
                DoScan(context);
            }
            catch (OperationCanceledException)
            {
                // Scan was cancelled; results may be partial.
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectCleanPro] {DisplayName} scan failed: {ex}");
            }
            finally
            {
                _isScanning = false;
                _progress = 1f;
                _progressLabel = _cancelled ? "Cancelled" : "Complete";
            }
        }

        /// <summary>
        /// Subclasses implement their full scan logic here.
        /// Use <see cref="ReportProgress"/>, <see cref="ShouldCancel"/>,
        /// and <see cref="IsIgnored"/> as helpers.
        /// </summary>
        protected abstract void DoScan(PCPScanContext context);

        /// <summary>
        /// Request cooperative cancellation.
        /// </summary>
        public void Cancel()
        {
            _cancelled = true;
        }

        /// <summary>
        /// Clear all results and reset progress state.
        /// </summary>
        public virtual void Clear()
        {
            _isScanning = false;
            _cancelled = false;
            _progress = 0f;
            _progressLabel = string.Empty;
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
        /// Returns true when the user has requested cancellation.
        /// Subclasses should check this periodically inside tight loops.
        /// </summary>
        protected bool ShouldCancel()
        {
            return _cancelled;
        }

        /// <summary>
        /// Returns true when <paramref name="path"/> matches any of the
        /// active ignore rules in the current context.
        /// </summary>
        protected bool IsIgnored(string path, PCPScanContext context)
        {
            if (context == null || context.IgnoreRules == null)
                return false;
            return context.IgnoreRules.IsIgnored(path);
        }
    }
}
