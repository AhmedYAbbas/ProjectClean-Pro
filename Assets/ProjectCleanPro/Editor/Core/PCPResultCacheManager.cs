using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Manages per-module binary result persistence and the scan manifest.
    /// Replaces the old monolithic <c>PCPResultCache</c> (single JSON file)
    /// with individual .bin files per module plus a manifest header.
    /// <para>
    /// Each module's results are written/read via its own
    /// <see cref="IPCPModule.WriteResults"/>/<see cref="IPCPModule.ReadResults"/>
    /// methods, keeping serialization logic co-located with the module.
    /// </para>
    /// </summary>
    public sealed class PCPResultCacheManager
    {
        private static readonly string s_ResultDir =
            Path.Combine(PCPScanCache.CacheDirectory, "Results");

        private static readonly string s_ManifestPath =
            Path.Combine(s_ResultDir, "manifest.bin");

        // ----------------------------------------------------------------
        // Module results
        // ----------------------------------------------------------------

        /// <summary>
        /// Saves a single module's results to its own binary file.
        /// Uses atomic writes via <see cref="PCPCacheIO"/>.
        /// </summary>
        public void SaveModule(IPCPModule module)
        {
            if (module == null) return;

            string path = GetModulePath(module.Id);
            try
            {
                PCPCacheIO.AtomicWrite(path, GetModuleVersion(module.Id), writer =>
                {
                    module.WriteResults(writer);
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to save {module.DisplayName} results: {ex.Message}");
            }
        }

        public async Task SaveModuleAsync(IPCPModule module, CancellationToken ct)
        {
            await Task.Run(() => SaveModule(module), ct);
        }

        public async Task SaveManifestAsync(PCPScanManifest manifest, CancellationToken ct)
        {
            await Task.Run(() => SaveManifest(manifest), ct);
        }

        /// <summary>
        /// Loads a single module's results from disk. Returns true if successful.
        /// On failure, the module retains its current (possibly empty) state.
        /// </summary>
        public bool LoadModule(IPCPModule module)
        {
            if (module == null) return false;

            string path = GetModulePath(module.Id);
            return PCPCacheIO.SafeRead(path, GetModuleVersion(module.Id), reader =>
            {
                module.ReadResults(reader);
                return true;
            }, out _);
        }

        /// <summary>
        /// Loads all modules' results from disk.
        /// </summary>
        public void LoadAll(System.Collections.Generic.IReadOnlyList<IPCPModule> modules)
        {
            if (modules == null) return;
            for (int i = 0; i < modules.Count; i++)
                LoadModule(modules[i]);
        }

        /// <summary>
        /// Deletes the cached results for a single module.
        /// </summary>
        public void InvalidateModule(PCPModuleId id)
        {
            string path = GetModulePath(id);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to invalidate {id} cache: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Manifest
        // ----------------------------------------------------------------

        /// <summary>
        /// Saves the scan manifest (summary data for the dashboard).
        /// </summary>
        public void SaveManifest(PCPScanManifest manifest)
        {
            if (manifest == null) return;

            try
            {
                PCPCacheIO.AtomicWrite(s_ManifestPath, PCPScanManifest.FormatVersion, writer =>
                {
                    writer.Write(manifest.scanTimestampUtc ?? string.Empty);
                    writer.Write(manifest.scanDurationSeconds);
                    writer.Write(manifest.projectName ?? string.Empty);
                    writer.Write(manifest.unityVersion ?? string.Empty);
                    writer.Write(manifest.totalAssetsScanned);
                    writer.Write(manifest.healthScore);
                    writer.Write(manifest.totalWastedBytes);
                    writer.Write(manifest.totalFindingCount);

                    int count = manifest.moduleSummaries?.Length ?? 0;
                    writer.Write(count);
                    for (int i = 0; i < count; i++)
                    {
                        var s = manifest.moduleSummaries[i];
                        writer.Write((byte)s.id);
                        writer.Write(s.scanTimestampTicks);
                        writer.Write(s.findingCount);
                        writer.Write(s.totalSizeBytes);
                        writer.Write(s.hasResults);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to save manifest: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the scan manifest from disk. Returns null if missing or invalid.
        /// </summary>
        public PCPScanManifest LoadManifest()
        {
            bool loaded = PCPCacheIO.SafeRead(s_ManifestPath, PCPScanManifest.FormatVersion, reader =>
            {
                var m = new PCPScanManifest
                {
                    scanTimestampUtc = reader.ReadString(),
                    scanDurationSeconds = reader.ReadSingle(),
                    projectName = reader.ReadString(),
                    unityVersion = reader.ReadString(),
                    totalAssetsScanned = reader.ReadInt32(),
                    healthScore = reader.ReadInt32(),
                    totalWastedBytes = reader.ReadInt64(),
                    totalFindingCount = reader.ReadInt32()
                };

                int count = reader.ReadInt32();
                m.moduleSummaries = new PCPModuleSummary[count];
                for (int i = 0; i < count; i++)
                {
                    m.moduleSummaries[i] = new PCPModuleSummary
                    {
                        id = (PCPModuleId)reader.ReadByte(),
                        scanTimestampTicks = reader.ReadInt64(),
                        findingCount = reader.ReadInt32(),
                        totalSizeBytes = reader.ReadInt64(),
                        hasResults = reader.ReadBoolean()
                    };
                }

                return m;
            }, out PCPScanManifest manifest);

            return loaded ? manifest : null;
        }

        /// <summary>
        /// True if a manifest file exists on disk.
        /// </summary>
        public bool HasCachedManifest => File.Exists(s_ManifestPath);

        // ----------------------------------------------------------------
        // Bulk invalidation
        // ----------------------------------------------------------------

        /// <summary>
        /// Deletes all cached result files and the manifest.
        /// </summary>
        public void InvalidateAll()
        {
            try
            {
                if (Directory.Exists(s_ResultDir))
                    Directory.Delete(s_ResultDir, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to invalidate result cache: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static string GetModulePath(PCPModuleId id)
        {
            return Path.Combine(s_ResultDir, id.ToString().ToLowerInvariant() + ".bin");
        }

        /// <summary>
        /// Per-module format version. All start at 1. Bump individually when
        /// a module's serialization format changes.
        /// </summary>
        private static int GetModuleVersion(PCPModuleId id)
        {
            // All modules start at version 1.
            return 1;
        }
    }
}
