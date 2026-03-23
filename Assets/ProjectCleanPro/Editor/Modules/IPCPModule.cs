using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Contract for every ProjectCleanPro scanner / analysis module.
    /// Each module owns its scan logic, results, progress state, incremental
    /// support declarations, and binary serialization of its results.
    /// </summary>
    public interface IPCPModule
    {
        // ----------------------------------------------------------------
        // Identity
        // ----------------------------------------------------------------

        /// <summary>Typed module identifier.</summary>
        PCPModuleId Id { get; }

        /// <summary>Human-readable name shown in tab headers.</summary>
        string DisplayName { get; }

        /// <summary>Unicode character used as the tab icon.</summary>
        string Icon { get; }

        /// <summary>Accent colour for charts, badges, and highlights.</summary>
        Color AccentColor { get; }

        // ----------------------------------------------------------------
        // Scan state
        // ----------------------------------------------------------------

        /// <summary>True while a scan is in progress.</summary>
        bool IsScanning { get; }

        /// <summary>Normalised progress (0-1) of the current scan.</summary>
        float Progress { get; }

        /// <summary>Short label describing what the scanner is doing.</summary>
        string ProgressLabel { get; }

        // ----------------------------------------------------------------
        // Results
        // ----------------------------------------------------------------

        /// <summary>Number of findings produced by the last scan.</summary>
        int FindingCount { get; }

        /// <summary>Aggregate disk size of all findings (where applicable).</summary>
        long TotalSizeBytes { get; }

        /// <summary>True if the module has results from a previous scan.</summary>
        bool HasResults { get; }

        // ----------------------------------------------------------------
        // Lifecycle (async — yields to editor between chunks)
        // ----------------------------------------------------------------

        /// <summary>
        /// Run the scan asynchronously, yielding to the editor every N assets
        /// so the UI stays responsive. Check <paramref name="ct"/> periodically.
        /// </summary>
        Task ScanAsync(PCPScanContext context, CancellationToken ct);

        /// <summary>Request a cooperative cancellation of the current scan.</summary>
        void Cancel();

        /// <summary>Clear all results and reset state.</summary>
        void Clear();

        // ----------------------------------------------------------------
        // Incremental support
        // ----------------------------------------------------------------

        /// <summary>
        /// File extensions that affect this module's results.
        /// Return <c>null</c> to mean "all extensions" (module is dirty on any change).
        /// Return an empty set to mean "never dirty from file changes" (e.g. packages).
        /// Extensions should be lowercase with leading dot (e.g. ".prefab").
        /// </summary>
        IReadOnlyCollection<string> RelevantExtensions { get; }

        /// <summary>
        /// True if this module requires the dependency graph to be built
        /// before it can run. The orchestrator ensures the graph is ready
        /// before calling <see cref="ScanAsync"/> on such modules.
        /// </summary>
        bool RequiresDependencyGraph { get; }

        // ----------------------------------------------------------------
        // Binary persistence
        // ----------------------------------------------------------------

        /// <summary>
        /// Serialize this module's results to a binary stream.
        /// Called by <see cref="PCPResultCacheManager.SaveModule"/>.
        /// </summary>
        void WriteResults(BinaryWriter writer);

        /// <summary>
        /// Deserialize this module's results from a binary stream.
        /// Called by <see cref="PCPResultCacheManager.LoadModule"/>.
        /// </summary>
        void ReadResults(BinaryReader reader);
    }
}
