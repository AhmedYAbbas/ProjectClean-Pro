using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectCleanPro.Editor.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Central scan orchestrator — the single source of truth for all scan logic.
    /// Replaces duplicated orchestration in PCPWindow, PCPAPI, and individual views.
    /// <para>
    /// Supports full scans (all modules), single-module scans, async execution
    /// with editor yielding, cancellation, and synchronous wrappers for CI.
    /// </para>
    /// </summary>
    public sealed class PCPScanOrchestrator
    {
        private readonly IPCPModule[] m_Modules;
        private readonly PCPResultCacheManager m_ResultCache;

        /// <summary>
        /// Module execution order. Dependency-graph consumers run first so the
        /// graph is built once and reused.
        /// </summary>
        private static readonly PCPModuleId[] s_ExecutionOrder =
        {
            PCPModuleId.Unused,       // builds graph
            PCPModuleId.Dependencies,  // reuses graph
            PCPModuleId.Duplicates,    // uses graph for ref counts
            PCPModuleId.Missing,       // independent
            PCPModuleId.Shaders,       // independent
            PCPModuleId.Size,          // independent
            PCPModuleId.Packages,      // independent (UPM data)
        };

        // ----------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------

        public PCPScanOrchestrator(IReadOnlyList<IPCPModule> modules,
            PCPResultCacheManager resultCache)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            if (resultCache == null) throw new ArgumentNullException(nameof(resultCache));

            // Index modules by PCPModuleId ordinal for O(1) lookup.
            int maxId = 0;
            for (int i = 0; i < modules.Count; i++)
            {
                int id = (int)modules[i].Id;
                if (id > maxId) maxId = id;
            }

            m_Modules = new IPCPModule[maxId + 1];
            for (int i = 0; i < modules.Count; i++)
                m_Modules[(int)modules[i].Id] = modules[i];

            m_ResultCache = resultCache;
        }

        // ----------------------------------------------------------------
        // Module access
        // ----------------------------------------------------------------

        public IPCPModule GetModule(PCPModuleId id)
        {
            int idx = (int)id;
            return idx >= 0 && idx < m_Modules.Length ? m_Modules[idx] : null;
        }

        public IReadOnlyList<IPCPModule> AllModules => m_Modules;

        // ----------------------------------------------------------------
        // Full scan (async)
        // ----------------------------------------------------------------

        /// <summary>
        /// Runs all dirty modules asynchronously. Yields to the editor between
        /// modules and within each module so the UI stays responsive.
        /// </summary>
        public async Task<PCPScanManifest> ScanAllAsync(
            PCPScanContext context,
            Action<float, string> onProgress = null,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            // Step 1: Handle mode switch invalidation
            var settings = context.Settings;
            if (settings.scanMode != settings.lastScanMode)
            {
                InvalidateCachesForModeSwitch(context);
                settings.lastScanMode = settings.scanMode;
                settings.Save();
            }

            // Step 2: Create scheduler with user's frame budget
            using var scheduler = new PCPAsyncScheduler(settings.mainThreadBudgetMs);
            context.Scheduler = scheduler;

            // Step 3: Create the right dependency resolver
            context.DependencyResolver = PCPDependencyResolverFactory.Create(settings.scanMode);

            // Step 4: Async staleness computation (background)
            onProgress?.Invoke(0f, "Checking for changes...");
            await context.Cache.RefreshStalenessAsync(context, ct);
            context.Cache.ComputeModuleDirtiness(GetActiveModuleList());
            ApplyExternalDirtiness(context);

            // Step 5: Skip check
            if (!context.Cache.HasAnyChanges && AllModulesHaveResults())
            {
                var cached = m_ResultCache.LoadManifest();
                if (cached != null)
                {
                    onProgress?.Invoke(1f, "No changes detected");
                    return cached;
                }
            }

            // Step 6: Build dependency graph (if needed)
            var dirtyModules = GetDirtyModules(context);
            bool needsGraph = dirtyModules.Any(m => m.RequiresDependencyGraph);

            if (needsGraph)
            {
                onProgress?.Invoke(0.05f, "Building dependency graph...");
                await context.DependencyResolver.BuildGraphAsync(context, ct);
            }

            // Step 7: Run modules
            onProgress?.Invoke(0.30f, "Scanning modules...");
            float progressBase = 0.30f;
            float progressPerModule = 0.65f / Math.Max(1, dirtyModules.Count);

            foreach (var module in dirtyModules)
            {
                ct.ThrowIfCancellationRequested();
                onProgress?.Invoke(progressBase, $"Scanning: {module.DisplayName}...");

                module.Clear();
                await module.ScanAsync(context, ct);
                m_ResultCache.SaveModule(module);

                progressBase += progressPerModule;
            }

            // Step 8: Finalize (background)
            onProgress?.Invoke(0.95f, "Saving results...");
            await context.FinalizeScanAsync(ct);
            PCPSettingsTracker.Reset();

            // Step 9: Manifest
            sw.Stop();
            var manifest = ComputeManifest(context, (float)sw.Elapsed.TotalSeconds);
            m_ResultCache.SaveManifest(manifest);

            onProgress?.Invoke(1f, "Complete");
            return manifest;
        }

        private List<IPCPModule> GetDirtyModules(PCPScanContext context)
        {
            var result = new List<IPCPModule>();
            foreach (var id in s_ExecutionOrder)
            {
                var module = GetModule(id);
                if (module != null && IsDirty(id, context))
                    result.Add(module);
            }
            return result;
        }

        // ----------------------------------------------------------------
        // Single module scan (async)
        // ----------------------------------------------------------------

        /// <summary>
        /// Runs a single module asynchronously.
        /// </summary>
        public async Task<PCPScanManifest> ScanModuleAsync(
            PCPModuleId moduleId,
            PCPScanContext context,
            Action<float, string> onProgress = null,
            CancellationToken ct = default)
        {
            var module = GetModule(moduleId);
            if (module == null)
                throw new ArgumentException($"Module {moduleId} not registered.");

            var sw = Stopwatch.StartNew();
            var settings = context.Settings;

            // Handle mode switch invalidation
            if (settings.scanMode != settings.lastScanMode)
            {
                InvalidateCachesForModeSwitch(context);
                settings.lastScanMode = settings.scanMode;
                settings.Save();
            }

            // Create scheduler and resolver
            using var scheduler = new PCPAsyncScheduler(settings.mainThreadBudgetMs);
            context.Scheduler = scheduler;
            context.DependencyResolver = PCPDependencyResolverFactory.Create(settings.scanMode);

            // Async staleness
            onProgress?.Invoke(0f, "Checking for changes...");
            await context.Cache.RefreshStalenessAsync(context, ct);
            context.Cache.ComputeModuleDirtiness(GetActiveModuleList());
            ApplyExternalDirtiness(context);

            // Skip check
            if (!IsDirty(moduleId, context) && module.HasResults)
            {
                onProgress?.Invoke(1f, "No changes detected.");
                return m_ResultCache.LoadManifest();
            }

            // Build graph if needed
            if (module.RequiresDependencyGraph)
            {
                onProgress?.Invoke(0.05f, "Building dependency graph...");
                await context.DependencyResolver.BuildGraphAsync(context, ct);
            }

            // Run module
            onProgress?.Invoke(0.35f, $"Scanning: {module.DisplayName}...");
            module.Clear();
            await module.ScanAsync(context, ct);
            m_ResultCache.SaveModule(module);

            // Finalize
            onProgress?.Invoke(0.95f, "Saving results...");
            await context.FinalizeScanAsync(ct);
            PCPSettingsTracker.Reset();

            // Manifest
            sw.Stop();
            var manifest = ComputeManifest(context, (float)sw.Elapsed.TotalSeconds);
            m_ResultCache.SaveManifest(manifest);

            onProgress?.Invoke(1f, "Scan complete.");
            return manifest;
        }

        // ----------------------------------------------------------------
        // Synchronous wrappers (for PCPAPI / CI)
        // ----------------------------------------------------------------

        public PCPScanManifest ScanAllSync(PCPScanContext context,
            Action<float, string> onProgress = null)
        {
            var task = ScanAllAsync(context, onProgress, CancellationToken.None);
            task.GetAwaiter().GetResult();
            return task.Result;
        }

        public PCPScanManifest ScanModuleSync(PCPModuleId moduleId,
            PCPScanContext context,
            Action<float, string> onProgress = null)
        {
            var task = ScanModuleAsync(moduleId, context, onProgress, CancellationToken.None);
            task.GetAwaiter().GetResult();
            return task.Result;
        }

        // ----------------------------------------------------------------
        // Queries
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns true if a scan would do work (something changed or missing results).
        /// </summary>
        public bool HasChanges(PCPScanContext context)
        {
            context.EnsureStaleness(GetActiveModuleList());
            return context.Cache.HasAnyChanges ||
                   PCPSettingsTracker.HasDirtyModules ||
                   PCPAssetChangeTracker.PackagesChanged;
        }

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        private void InvalidateCachesForModeSwitch(PCPScanContext context)
        {
            // Clear dependency graph cache
            var graphPath = Path.Combine(PCPScanCache.CacheDirectory, "DepGraph.bin");
            if (File.Exists(graphPath))
                File.Delete(graphPath);

            // Clear all module results
            m_ResultCache.InvalidateAll();

            // Mark all modules dirty
            foreach (var module in m_Modules)
            {
                if (module != null)
                    context.Cache.MarkModuleDirty(module.Id);
            }

            // Keep timestamp/hash data — mode-independent
            // Only clear staleness flags
            context.Cache.ResetStaleness();
        }

        private bool IsDirty(PCPModuleId id, PCPScanContext context)
        {
            // A module with no results must always run (first scan, or cache cleared).
            var module = GetModule(id);
            if (module != null && !module.HasResults)
                return true;

            return context.Cache.IsModuleDirty(id) ||
                   PCPSettingsTracker.IsModuleDirty(id);
        }

        /// <summary>
        /// Applies external dirtiness sources that aren't file-change based.
        /// </summary>
        private void ApplyExternalDirtiness(PCPScanContext context)
        {
            // Package module dirty when manifest.json changed OR after domain
            // reload (we can't know if packages changed while domain was unloaded).
            if (PCPAssetChangeTracker.PackagesChanged || PCPAssetChangeTracker.FullCheckNeeded)
                context.Cache.MarkModuleDirty(PCPModuleId.Packages);

            // Settings-based dirtiness.
            foreach (var moduleId in s_ExecutionOrder)
            {
                if (PCPSettingsTracker.IsModuleDirty(moduleId))
                    context.Cache.MarkModuleDirty(moduleId);
            }
        }

        private bool AllModulesHaveResults()
        {
            for (int i = 0; i < m_Modules.Length; i++)
            {
                if (m_Modules[i] != null && !m_Modules[i].HasResults)
                    return false;
            }
            return true;
        }

        private IReadOnlyList<IPCPModule> GetActiveModuleList()
        {
            var list = new List<IPCPModule>();
            for (int i = 0; i < m_Modules.Length; i++)
            {
                if (m_Modules[i] != null)
                    list.Add(m_Modules[i]);
            }
            return list;
        }

        /// <summary>
        /// Collects root assets for BFS reachability (scenes, Resources, etc.).
        /// </summary>
        internal static HashSet<string> CollectRoots(PCPScanContext context)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);

            // Scenes.
            if (context.Settings.includeAllScenes)
            {
                string[] allScenes = PCPAssetUtils.GetAllScenePaths(context.AllProjectAssets);
                for (int i = 0; i < allScenes.Length; i++)
                    roots.Add(allScenes[i]);
            }
            else
            {
                string[] buildScenes = PCPAssetUtils.GetBuildScenePaths();
                for (int i = 0; i < buildScenes.Length; i++)
                    roots.Add(buildScenes[i]);
            }

            // Resources.
            string[] resources = PCPAssetUtils.GetResourcesPaths(context.AllProjectAssets);
            for (int i = 0; i < resources.Length; i++)
                roots.Add(resources[i]);

            // AssetBundles.
            if (context.Settings.includeAssetBundles)
            {
                string[] bundles = PCPAssetUtils.GetAssetBundleRoots();
                for (int i = 0; i < bundles.Length; i++)
                    roots.Add(bundles[i]);
            }

            // Addressables.
            if (context.Settings.includeAddressables && PCPAddressablesBridge.HasAddressables)
            {
                var addressables = PCPAddressablesBridge.GetRoots();
                for (int i = 0; i < addressables.Count; i++)
                    roots.Add(addressables[i]);
            }

            // Custom roots.
            if (context.AlwaysUsedRoots != null)
            {
                for (int i = 0; i < context.AlwaysUsedRoots.Count; i++)
                {
                    string root = context.AlwaysUsedRoots[i];
                    if (string.IsNullOrEmpty(root)) continue;

                    if (AssetDatabase.IsValidFolder(root))
                    {
                        string[] guids = AssetDatabase.FindAssets("", new[] { root });
                        for (int j = 0; j < guids.Length; j++)
                        {
                            string path = AssetDatabase.GUIDToAssetPath(guids[j]);
                            if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path))
                                roots.Add(path);
                        }
                    }
                    else
                    {
                        roots.Add(root);
                    }
                }
            }

            // Always-included shaders.
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings != null)
            {
                var so = new SerializedObject(graphicsSettings);
                var prop = so.FindProperty("m_AlwaysIncludedShaders");
                if (prop != null && prop.isArray)
                {
                    for (int i = 0; i < prop.arraySize; i++)
                    {
                        var elem = prop.GetArrayElementAtIndex(i);
                        if (elem.objectReferenceValue != null)
                        {
                            string path = AssetDatabase.GetAssetPath(elem.objectReferenceValue);
                            if (!string.IsNullOrEmpty(path))
                                roots.Add(path);
                        }
                    }
                }
            }

            return roots;
        }

        /// <summary>
        /// Builds a <see cref="PCPScanManifest"/> from the current module states.
        /// </summary>
        private PCPScanManifest ComputeManifest(PCPScanContext context, float durationSeconds)
        {
            int moduleCount = Enum.GetValues(typeof(PCPModuleId)).Length;
            var summaries = new PCPModuleSummary[moduleCount];

            int totalFindings = 0;
            long totalWasted = 0;

            for (int i = 0; i < moduleCount; i++)
            {
                var module = i < m_Modules.Length ? m_Modules[i] : null;
                if (module != null)
                {
                    summaries[i] = new PCPModuleSummary
                    {
                        id = module.Id,
                        scanTimestampTicks = DateTime.UtcNow.Ticks,
                        findingCount = module.FindingCount,
                        totalSizeBytes = module.TotalSizeBytes,
                        hasResults = module.HasResults
                    };
                    totalFindings += module.FindingCount;
                    totalWasted += module.TotalSizeBytes;
                }
            }

            var allWarnings = new List<PCPScanManifest.ScanWarning>();
            for (int i = 0; i < m_Modules.Length; i++)
            {
                var module = m_Modules[i];
                if (module == null) continue;
                foreach (var w in module.Warnings)
                    allWarnings.Add(new PCPScanManifest.ScanWarning(module.Id, w));
            }

            return new PCPScanManifest
            {
                scanTimestampUtc = DateTime.UtcNow.ToString("o"),
                scanDurationSeconds = durationSeconds,
                projectName = Application.productName,
                unityVersion = Application.unityVersion,
                totalAssetsScanned = context.AllProjectAssets?.Length ?? 0,
                healthScore = ComputeHealthScore(totalFindings, context.AllProjectAssets?.Length ?? 1),
                totalWastedBytes = totalWasted,
                totalFindingCount = totalFindings,
                moduleSummaries = summaries,
                warnings = allWarnings
            };
        }

        private static int ComputeHealthScore(int totalFindings, int totalAssets)
        {
            if (totalFindings == 0) return 100;
            double scaleFactor = 100.0 / Math.Sqrt(Math.Max(totalAssets, 1));
            double k = 0.02 * scaleFactor / 100.0 * Math.Sqrt(100.0);
            double score = 100.0 * Math.Exp(-k * totalFindings);
            return Math.Max(0, Math.Min(100, (int)Math.Round(score)));
        }
    }
}
