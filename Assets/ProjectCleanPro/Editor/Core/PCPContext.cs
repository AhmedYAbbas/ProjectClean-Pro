using System;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Central service registry for ProjectCleanPro.
    /// Provides lazy-initialized access to all core services.
    /// </summary>
    public static class PCPContext
    {
        private static PCPSettings s_Settings;
        private static PCPDependencyResolver s_DependencyResolver;
        private static PCPScanCache s_ScanCache;
        private static PCPIgnoreRules s_IgnoreRules;
        private static PCPRenderPipelineDetector s_RenderPipelineDetector;

        private static bool s_Initialized;

        /// <summary>
        /// The project-wide settings instance (ScriptableSingleton).
        /// </summary>
        public static PCPSettings Settings
        {
            get
            {
                if (s_Settings == null)
                    s_Settings = PCPSettings.instance;
                return s_Settings;
            }
        }

        /// <summary>
        /// Builds and queries the full asset dependency graph.
        /// </summary>
        public static PCPDependencyResolver DependencyResolver
        {
            get
            {
                if (s_DependencyResolver == null)
                    s_DependencyResolver = new PCPDependencyResolver();
                return s_DependencyResolver;
            }
        }

        /// <summary>
        /// Incremental scan cache stored in Library/ProjectCleanPro/.
        /// </summary>
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

        /// <summary>
        /// Ignore rule evaluation engine.
        /// </summary>
        public static PCPIgnoreRules IgnoreRules
        {
            get
            {
                if (s_IgnoreRules == null)
                    s_IgnoreRules = new PCPIgnoreRules();
                return s_IgnoreRules;
            }
        }

        /// <summary>
        /// Render pipeline detection utility.
        /// </summary>
        public static PCPRenderPipelineDetector RenderPipelineDetector
        {
            get
            {
                if (s_RenderPipelineDetector == null)
                    s_RenderPipelineDetector = new PCPRenderPipelineDetector();
                return s_RenderPipelineDetector;
            }
        }

        /// <summary>
        /// Whether the context has been explicitly initialized.
        /// </summary>
        public static bool IsInitialized => s_Initialized;

        /// <summary>
        /// Explicitly initializes all services. Safe to call multiple times;
        /// subsequent calls are no-ops unless <see cref="Dispose"/> was called first.
        /// </summary>
        public static void Initialize()
        {
            if (s_Initialized)
                return;

            // Touch each property to force lazy initialization.
            _ = Settings;
            _ = DependencyResolver;
            _ = ScanCache;
            _ = IgnoreRules;
            _ = RenderPipelineDetector;

            s_Initialized = true;
        }

        /// <summary>
        /// Releases all service instances. Call this when the tool window is closed
        /// or when a full re-scan is requested.
        /// </summary>
        public static void Dispose()
        {
            if (s_ScanCache != null)
            {
                s_ScanCache.Save();
                s_ScanCache = null;
            }

            s_DependencyResolver = null;
            s_IgnoreRules = null;
            s_RenderPipelineDetector = null;
            // Settings is a ScriptableSingleton; we don't destroy it, just release our reference.
            s_Settings = null;

            s_Initialized = false;
        }
    }
}
