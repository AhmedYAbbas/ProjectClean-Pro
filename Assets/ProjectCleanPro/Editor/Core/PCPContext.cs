using System;
using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Central service registry for ProjectCleanPro.
    /// Provides lazy-initialized access to all core services including the
    /// scan orchestrator and result cache manager.
    /// </summary>
    public static class PCPContext
    {
        private static PCPSettings s_Settings;
        private static PCPScanCache s_ScanCache;
        private static PCPIgnoreRules s_IgnoreRules;
        private static PCPRenderPipelineDetector s_RenderPipelineDetector;
        private static PCPScanOrchestrator s_Orchestrator;
        private static PCPResultCacheManager s_ResultCacheManager;

        private static bool s_Initialized;
        private static bool s_SettingsHooked;

        // ----------------------------------------------------------------
        // Core services
        // ----------------------------------------------------------------

        public static PCPSettings Settings
        {
            get
            {
                if (s_Settings == null)
                    s_Settings = PCPSettings.instance;
                return s_Settings;
            }
        }

        public static PCPScanCache ScanCache
        {
            get
            {
                if (s_ScanCache == null)
                {
                    s_ScanCache = new PCPScanCache();
                    s_ScanCache.Load();
                }
                return s_ScanCache;
            }
        }

        public static PCPIgnoreRules IgnoreRules
        {
            get
            {
                if (s_IgnoreRules == null)
                    s_IgnoreRules = new PCPIgnoreRules();
                return s_IgnoreRules;
            }
        }

        public static PCPRenderPipelineDetector RenderPipelineDetector
        {
            get
            {
                if (s_RenderPipelineDetector == null)
                    s_RenderPipelineDetector = new PCPRenderPipelineDetector();
                return s_RenderPipelineDetector;
            }
        }

        // ----------------------------------------------------------------
        // New services (orchestrator, result cache)
        // ----------------------------------------------------------------

        public static PCPResultCacheManager ResultCacheManager
        {
            get
            {
                if (s_ResultCacheManager == null)
                    s_ResultCacheManager = new PCPResultCacheManager();
                return s_ResultCacheManager;
            }
        }

        /// <summary>
        /// Central scan orchestrator. Creates all module instances and wires
        /// them to the result cache manager on first access.
        /// </summary>
        public static PCPScanOrchestrator Orchestrator
        {
            get
            {
                if (s_Orchestrator == null)
                {
                    var modules = CreateModules();
                    s_Orchestrator = new PCPScanOrchestrator(modules, ResultCacheManager);
                }
                return s_Orchestrator;
            }
        }

        // ----------------------------------------------------------------
        // Scan result (backward compat + in-memory cache)
        // ----------------------------------------------------------------

        public static bool IsInitialized => s_Initialized;

        /// <summary>
        /// Last scan result for backward compatibility with report exporter
        /// and API consumers.
        /// </summary>
        public static PCPScanResult LastScanResult { get; set; }

        /// <summary>
        /// Last scan manifest for the dashboard. Survives window close/reopen
        /// within the same editor session.
        /// </summary>
        public static PCPScanManifest LastScanManifest { get; set; }

        // ----------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------

        public static void Initialize()
        {
            if (s_Initialized)
                return;

            _ = Settings;
            _ = ScanCache;
            _ = IgnoreRules;
            _ = RenderPipelineDetector;
            _ = ResultCacheManager;
            _ = Orchestrator;

            // Hook settings change tracking.
            if (!s_SettingsHooked)
            {
                PCPSettings.OnSettingsSaved += () => PCPSettingsTracker.OnSettingsChanged(Settings);
                PCPSettingsTracker.TakeSnapshot(Settings);
                s_SettingsHooked = true;
            }

            s_Initialized = true;
        }

        public static void SaveCache()
        {
            s_ScanCache?.Save();
        }

        public static void Dispose()
        {
            if (s_ScanCache != null)
            {
                s_ScanCache.Save();
                s_ScanCache = null;
            }

            s_IgnoreRules = null;
            s_RenderPipelineDetector = null;
            s_Settings = null;
            s_Orchestrator = null;
            s_ResultCacheManager = null;
            LastScanResult = null;
            LastScanManifest = null;

            s_Initialized = false;
        }

        // ----------------------------------------------------------------
        // Module factory
        // ----------------------------------------------------------------

        private static IReadOnlyList<IPCPModule> CreateModules()
        {
            return new IPCPModule[]
            {
                new PCPUnusedScanner(),
                new PCPMissingRefScanner(),
                new PCPDuplicateDetector(),
                new PCPDependencyModule(),
                new PCPPackageAuditor(),
                new PCPShaderAnalyzer(),
                new PCPSizeProfiler(),
            };
        }
    }
}
