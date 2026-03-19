using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Contract for every ProjectCleanPro scanner / analysis module.
    /// Each module owns its own scan logic, results, and progress state.
    /// </summary>
    public interface IPCPModule
    {
        /// <summary>Unique machine-readable identifier (e.g. "unused", "duplicates").</summary>
        string ModuleId { get; }

        /// <summary>Human-readable name shown in tab headers.</summary>
        string DisplayName { get; }

        /// <summary>Unicode character used as the tab icon.</summary>
        string Icon { get; }

        /// <summary>Accent colour used for charts, badges, and highlights.</summary>
        Color AccentColor { get; }

        /// <summary>True while a scan is in progress.</summary>
        bool IsScanning { get; }

        /// <summary>Normalised progress (0-1) of the current scan.</summary>
        float Progress { get; }

        /// <summary>Short label describing what the scanner is currently doing.</summary>
        string ProgressLabel { get; }

        /// <summary>Number of findings produced by the last completed scan.</summary>
        int FindingCount { get; }

        /// <summary>Aggregate disk size of all findings (where applicable).</summary>
        long TotalSizeBytes { get; }

        /// <summary>Run the scan using the supplied context.</summary>
        void Scan(PCPScanContext context);

        /// <summary>Request a cooperative cancellation of the current scan.</summary>
        void Cancel();

        /// <summary>Clear all results and reset state.</summary>
        void Clear();
    }
}
