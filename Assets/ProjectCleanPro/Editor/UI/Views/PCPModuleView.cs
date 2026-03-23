using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Abstract base class for all module views. Provides:
    /// <list type="bullet">
    ///   <item><see cref="PCPModuleHeader"/> with scan button wiring</item>
    ///   <item>Scan orchestration (<see cref="OnScanClicked"/>, <see cref="RescanAfterChange"/>)</item>
    ///   <item>Auto-built action bar from capability interfaces
    ///         (<see cref="IPCPDeletableView"/>, <see cref="IPCPIgnorableView"/>,
    ///          <see cref="IPCPExportableView"/>, <see cref="IPCPMergeableView"/>)</item>
    ///   <item>Protected helpers for optional standard components
    ///         (<see cref="CreateFilterBar"/>, <see cref="CreateResultList"/>)</item>
    /// </list>
    /// Subclasses override <see cref="BuildContent"/> to lay out their custom UI
    /// between the header and action bar, and <see cref="RefreshContent"/> to
    /// update their display when scan results change.
    /// </summary>
    public abstract class PCPModuleView : VisualElement, IPCPRefreshable
    {
        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        protected readonly PCPScanResult m_ScanResult;
        protected readonly Func<PCPScanContext> m_CreateContext;
        protected readonly PCPModuleHeader m_Header;
        private readonly int m_ModuleColorIndex;
        private VisualElement m_ActionBar;

        /// <summary>
        /// Container for the subclass content area, inserted between
        /// the header and the action bar.
        /// </summary>
        private readonly VisualElement m_ContentContainer;

        /// <summary>
        /// Invoked after a single-module scan completes so the window can
        /// refresh other views (e.g. the dashboard).
        /// </summary>
        public Action onScanComplete;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        /// <summary>
        /// Creates the module view layout: header → content → action bar.
        /// </summary>
        /// <param name="scanResult">Shared scan result reference.</param>
        /// <param name="createContext">
        /// Factory delegate that creates a <see cref="PCPScanContext"/> for scanning.
        /// </param>
        /// <param name="moduleName">Display name for the module header.</param>
        /// <param name="moduleIcon">Unicode icon for the module header.</param>
        /// <param name="moduleColorIndex">Index into <see cref="PCPSettings.moduleColors"/>.</param>
        protected PCPModuleView(
            PCPScanResult scanResult,
            Func<PCPScanContext> createContext,
            string moduleName,
            string moduleIcon,
            int moduleColorIndex)
        {
            m_ScanResult = scanResult;
            m_CreateContext = createContext;
            m_ModuleColorIndex = moduleColorIndex;

            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // ---- Module header ----
            m_Header = new PCPModuleHeader(moduleName, moduleIcon, PCPContext.Settings.GetModuleColor(m_ModuleColorIndex));
            m_Header.onScan += OnScanClicked;
            m_Header.style.flexShrink = 0;
            Add(m_Header);

            // ---- Content area (subclass fills this) ----
            m_ContentContainer = new VisualElement();
            m_ContentContainer.style.flexGrow = 1;
            m_ContentContainer.style.flexDirection = FlexDirection.Column;
            Add(m_ContentContainer);

            BuildContent(m_ContentContainer);

            // ---- Action bar (auto-built from interfaces) ----
            BuildActionBar();

            // Initial population
            RefreshContent();
        }

        // --------------------------------------------------------------------
        // Abstract
        // --------------------------------------------------------------------

        /// <summary>
        /// Returns the <see cref="PCPModuleId"/> that identifies this view's
        /// scanner module. Used by the base class to delegate scans to the
        /// <see cref="PCPScanOrchestrator"/>.
        /// </summary>
        protected abstract PCPModuleId GetModuleId();

        /// <summary>
        /// Called once during construction. Subclasses add their custom UI
        /// elements to the provided container. Use <see cref="CreateFilterBar"/>
        /// and <see cref="CreateResultList"/> helpers for standard components.
        /// </summary>
        /// <param name="content">The container to add content elements to.</param>
        protected abstract void BuildContent(VisualElement content);

        /// <summary>
        /// Called after a scan completes or on initial display.
        /// Subclasses update their display from the current scan result data.
        /// </summary>
        protected abstract void RefreshContent();

        // --------------------------------------------------------------------
        // Protected helpers – optional standard components
        // --------------------------------------------------------------------

        /// <summary>
        /// Creates a standard <see cref="PCPFilterBar"/> wired to a
        /// <see cref="PCPResultListView"/>. The caller is responsible for
        /// adding the returned element to their layout.
        /// </summary>
        /// <param name="resultList">The result list to apply filters to.</param>
        /// <returns>A configured filter bar.</returns>
        protected PCPFilterBar CreateFilterBar(PCPResultListView resultList)
        {
            var filterBar = new PCPFilterBar();
            filterBar.onFilterChanged += filterState =>
            {
                if (filterState == null || resultList == null)
                    return;

                resultList.ApplyFilters(
                    filterState.searchText,
                    filterState.activeTypes.Count > 0 ? filterState.activeTypes : null,
                    filterState.statusFilter);
            };
            filterBar.style.flexShrink = 0;
            return filterBar;
        }

        /// <summary>
        /// Creates a standard <see cref="PCPResultListView"/>. The caller
        /// is responsible for adding the returned element to their layout.
        /// </summary>
        /// <returns>A new result list view.</returns>
        protected PCPResultListView CreateResultList()
        {
            return new PCPResultListView();
        }

        // --------------------------------------------------------------------
        // Public API
        // --------------------------------------------------------------------

        /// <summary>
        /// Reads current module data and refreshes the view.
        /// </summary>
        public void Refresh()
        {
            m_Header.AccentColor = PCPContext.Settings.GetModuleColor(m_ModuleColorIndex);
            RefreshContent();
        }

        /// <summary>
        /// Called when a scan completes. Refreshes the view with new data.
        /// </summary>
        public void OnScanComplete()
        {
            m_Header.IsScanning = false;
            RefreshContent();
        }

        // --------------------------------------------------------------------
        // Scan orchestration
        // --------------------------------------------------------------------

        private async void OnScanClicked()
        {
            m_Header.IsScanning = true;

            // Yield one frame so the UI shows the scanning state.
            await PCPEditorAsync.YieldToEditor();

            try
            {
                var context = m_CreateContext?.Invoke();
                if (context != null)
                {
                    var cts = new CancellationTokenSource();
                    await PCPContext.Orchestrator.ScanModuleAsync(
                        GetModuleId(), context, null, cts.Token);

                    SyncModuleToScanResult(GetModuleId(), m_ScanResult);

                    m_ScanResult.totalAssetsScanned = context.AllProjectAssets.Length;
                    m_ScanResult.scanTimestampUtc = DateTime.UtcNow.ToString("o");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectCleanPro] Scan failed: {ex}");
            }
            finally
            {
                OnScanComplete();
                onScanComplete?.Invoke();
            }
        }

        /// <summary>
        /// Runs a fresh module scan and refreshes the view.
        /// Used after deletions or ignore-list changes to ensure
        /// the displayed results reflect the current project state.
        /// </summary>
        protected async void RescanAfterChange()
        {
            m_Header.IsScanning = true;

            await PCPEditorAsync.YieldToEditor();

            try
            {
                var context = m_CreateContext?.Invoke();
                if (context != null)
                {
                    context.Cache.MarkModuleDirty(GetModuleId());

                    var cts = new CancellationTokenSource();
                    await PCPContext.Orchestrator.ScanModuleAsync(
                        GetModuleId(), context, null, cts.Token);

                    SyncModuleToScanResult(GetModuleId(), m_ScanResult);
                    m_ScanResult.totalAssetsScanned = context.AllProjectAssets.Length;
                    m_ScanResult.scanTimestampUtc = DateTime.UtcNow.ToString("o");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectCleanPro] Re-scan after change failed: {ex}");
            }
            finally
            {
                OnScanComplete();
                onScanComplete?.Invoke();
            }
        }

        // --------------------------------------------------------------------
        // Action bar – auto-built from capability interfaces
        // --------------------------------------------------------------------

        private void BuildActionBar()
        {
            bool isDeletable = this is IPCPDeletableView;
            bool isIgnorable = this is IPCPIgnorableView;
            bool isExportable = this is IPCPExportableView;
            bool isMergeable = this is IPCPMergeableView;

            // Only create the bar if at least one capability is present
            if (!isDeletable && !isIgnorable && !isExportable && !isMergeable)
                return;

            m_ActionBar = new VisualElement();
            m_ActionBar.style.flexDirection = FlexDirection.Row;
            m_ActionBar.style.alignItems = Align.Center;
            m_ActionBar.style.paddingLeft = 8;
            m_ActionBar.style.paddingRight = 8;
            m_ActionBar.style.paddingTop = 6;
            m_ActionBar.style.paddingBottom = 6;
            m_ActionBar.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            m_ActionBar.style.borderTopWidth = 1;
            m_ActionBar.style.borderTopColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            m_ActionBar.style.flexShrink = 0;

            if (isMergeable)
            {
                var mergeBtn = new Button(OnMergeAllClicked)
                {
                    text = "Merge All"
                };
                StyleActionButton(mergeBtn, new Color(0.557f, 0.267f, 0.678f, 1f));
                mergeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                m_ActionBar.Add(mergeBtn);
            }

            if (isDeletable)
            {
                var deleteBtn = new Button(OnDeleteSelected)
                {
                    text = "Delete Selected"
                };
                StyleActionButton(deleteBtn, new Color(0.753f, 0.224f, 0.169f, 1f));
                deleteBtn.style.color = Color.white;
                m_ActionBar.Add(deleteBtn);
            }

            if (isIgnorable)
            {
                var ignoreBtn = new Button(OnIgnoreSelected)
                {
                    text = "Ignore Selected"
                };
                ignoreBtn.AddToClassList("pcp-button-secondary");
                StyleActionButton(ignoreBtn, Color.clear);
                m_ActionBar.Add(ignoreBtn);
            }

            if (isExportable)
            {
                var exportBtn = new Button(OnExport)
                {
                    text = "Export"
                };
                exportBtn.AddToClassList("pcp-button-secondary");
                StyleActionButton(exportBtn, Color.clear);
                m_ActionBar.Add(exportBtn);
            }

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            m_ActionBar.Add(spacer);

            Add(m_ActionBar);
        }

        private static void StyleActionButton(Button btn, Color bgColor)
        {
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            btn.style.paddingTop = 4;
            btn.style.paddingBottom = 4;
            btn.style.marginRight = 4;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;

            if (bgColor != Color.clear)
                btn.style.backgroundColor = bgColor;
        }

        // --------------------------------------------------------------------
        // Action handlers – delegate to interfaces
        // --------------------------------------------------------------------

        private void OnDeleteSelected()
        {
            var deletable = this as IPCPDeletableView;
            if (deletable == null) return;

            var paths = deletable.GetSelectedPaths();
            if (paths == null || paths.Count == 0)
            {
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    "No items selected. Use the checkboxes to select items.", "OK");
                return;
            }

            var pathList = new List<string>();
            foreach (var p in paths)
            {
                if (!string.IsNullOrEmpty(p))
                    pathList.Add(p);
            }

            if (pathList.Count == 0)
                return;

            var context = m_CreateContext?.Invoke();
            var resolver = context?.DependencyResolver;
            var preview = PCPSafeDelete.Preview(pathList, resolver);

            if (PCPDeletePreviewDialog.Show(preview, out bool archive))
            {
                var settings = PCPContext.Settings;
                bool originalArchive = settings.archiveBeforeDelete;
                settings.archiveBeforeDelete = archive;

                try
                {
                    PCPSafeDelete.ArchiveAndDelete(preview, settings, resolver);

                    bool anyHadDependents = preview.items.Any(item => item.referenceCount > 0);

                    if (anyHadDependents)
                    {
                        RescanAfterChange();
                    }
                    else
                    {
                        var deletedPaths = new HashSet<string>(
                            preview.items.Select(item => item.path),
                            StringComparer.Ordinal);

                        m_ScanResult.RemovePaths(deletedPaths);

                        foreach (string path in deletedPaths)
                            context.Cache.RemoveEntry(path);

                        resolver?.RemoveAssets(deletedPaths);
                        context.Cache.Save();
                        PCPResultCache.Save(m_ScanResult);

                        OnScanComplete();
                        onScanComplete?.Invoke();
                    }
                }
                finally
                {
                    settings.archiveBeforeDelete = originalArchive;
                }
            }
        }

        private void OnIgnoreSelected()
        {
            var ignorable = this as IPCPIgnorableView;
            if (ignorable == null) return;

            var paths = ignorable.GetSelectedPaths();
            if (paths == null || paths.Count == 0)
            {
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    "No items selected. Use the checkboxes to select items.", "OK");
                return;
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(path))
                    AddPathToIgnoreList(path);
            }

            ignorable.ClearSelection();
            RescanAfterChange();
        }

        private void OnExport()
        {
            if (m_ScanResult == null)
                return;

            var exportable = this as IPCPExportableView;
            string key = exportable?.ModuleExportKey;

            var exportResult = !string.IsNullOrEmpty(key)
                ? PCPReportExporter.CreateModuleSubset(m_ScanResult, key)
                : m_ScanResult;

            PCPReportExporter.ShowExportMenu(exportResult);
        }

        private void OnMergeAllClicked()
        {
            var mergeable = this as IPCPMergeableView;
            mergeable?.MergeAll();
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void AddPathToIgnoreList(string path)
        {
            var settings = PCPContext.Settings;
            if (settings == null)
                return;

            var rule = new PCPIgnoreRule
            {
                type = PCPIgnoreType.PathPrefix,
                pattern = path,
                comment = "Added via ignore action",
                enabled = true
            };

            settings.ignoreRules.Add(rule);
            settings.Save();
            Debug.Log($"[ProjectCleanPro] Added ignore rule: {path}");

            var windows = Resources.FindObjectsOfTypeAll<PCPWindow>();
            if (windows.Length > 0)
                windows[0].RefreshSettingsView();
        }

        /// <summary>
        /// Copies the orchestrator's module results into the legacy
        /// <see cref="PCPScanResult"/> so that views and the report
        /// exporter see up-to-date data.
        /// </summary>
        internal static void SyncModuleToScanResult(PCPModuleId moduleId, PCPScanResult result)
        {
            if (result == null) return;

            var module = PCPContext.Orchestrator.GetModule(moduleId);
            if (module == null) return;

            switch (moduleId)
            {
                case PCPModuleId.Unused when module is PCPUnusedScanner u:
                    result.unusedAssets.Clear();
                    result.unusedAssets.AddRange(u.Results);
                    break;
                case PCPModuleId.Missing when module is PCPMissingRefScanner m:
                    result.missingReferences.Clear();
                    result.missingReferences.AddRange(m.Results);
                    break;
                case PCPModuleId.Duplicates when module is PCPDuplicateDetector d:
                    result.duplicateGroups.Clear();
                    result.duplicateGroups.AddRange(d.Results);
                    break;
                case PCPModuleId.Packages when module is PCPPackageAuditor p:
                    result.packageAuditEntries.Clear();
                    result.packageAuditEntries.AddRange(p.Results);
                    break;
                case PCPModuleId.Shaders when module is PCPShaderAnalyzer s:
                    result.shaderEntries.Clear();
                    result.shaderEntries.AddRange(s.Results);
                    break;
                case PCPModuleId.Size when module is PCPSizeProfiler z:
                    result.sizeEntries.Clear();
                    result.sizeEntries.AddRange(z.Results);
                    break;
                case PCPModuleId.Dependencies when module is PCPDependencyModule dep:
                    result.circularDependencies.Clear();
                    result.circularDependencies.AddRange(dep.CircularDependencies);
                    result.orphanAssets.Clear();
                    result.orphanAssets.AddRange(dep.OrphanAssets);
                    break;
            }
        }
    }
}
