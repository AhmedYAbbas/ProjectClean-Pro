using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Abstract base class for module-specific views. Provides a standard layout:
    /// <see cref="PCPModuleHeader"/> at the top, <see cref="PCPFilterBar"/> below it,
    /// <see cref="PCPResultListView"/> in the center, and an action bar at the bottom.
    /// Concrete views override <see cref="PopulateResults"/> to convert module data
    /// into <see cref="PCPRowData"/> for display.
    /// </summary>
    public abstract class PCPModuleView : VisualElement
    {
        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        protected readonly PCPScanResult m_ScanResult;
        protected readonly Func<PCPScanContext> m_CreateContext;
        protected readonly PCPModuleHeader m_Header;
        protected readonly PCPFilterBar m_FilterBar;
        protected readonly PCPResultListView m_ResultList;
        protected readonly VisualElement m_ActionBar;
        protected readonly Button m_DeleteSelectedBtn;
        protected readonly Button m_IgnoreSelectedBtn;
        protected readonly Button m_ExportBtn;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        /// <summary>
        /// Creates the standard module view layout.
        /// </summary>
        /// <param name="scanResult">Shared scan result reference.</param>
        /// <param name="createContext">
        /// Factory delegate that creates a <see cref="PCPScanContext"/> for scanning.
        /// </param>
        /// <param name="moduleName">Display name for the module header.</param>
        /// <param name="moduleIcon">Unicode icon for the module header.</param>
        /// <param name="accentColor">Accent color for the header bar.</param>
        protected PCPModuleView(
            PCPScanResult scanResult,
            Func<PCPScanContext> createContext,
            string moduleName,
            string moduleIcon,
            Color accentColor)
        {
            m_ScanResult = scanResult;
            m_CreateContext = createContext;

            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // ---- Module header ----
            m_Header = new PCPModuleHeader(moduleName, moduleIcon, accentColor);
            m_Header.onScan += OnScanClicked;
            m_Header.style.flexShrink = 0;
            Add(m_Header);

            // ---- Filter bar ----
            m_FilterBar = new PCPFilterBar();
            m_FilterBar.onFilterChanged += OnFilterChanged;
            m_FilterBar.style.flexShrink = 0;
            Add(m_FilterBar);

            // ---- Result list (center, grows to fill) ----
            m_ResultList = new PCPResultListView();
            m_ResultList.onContextMenu += OnResultContextMenu;
            Add(m_ResultList);

            // ---- Action bar at bottom ----
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

            m_DeleteSelectedBtn = new Button(OnDeleteSelected)
            {
                text = "Delete Selected"
            };
            m_DeleteSelectedBtn.style.paddingLeft = 10;
            m_DeleteSelectedBtn.style.paddingRight = 10;
            m_DeleteSelectedBtn.style.paddingTop = 4;
            m_DeleteSelectedBtn.style.paddingBottom = 4;
            m_DeleteSelectedBtn.style.marginRight = 4;
            m_DeleteSelectedBtn.style.backgroundColor = new Color(0.753f, 0.224f, 0.169f, 1f);
            m_DeleteSelectedBtn.style.color = Color.white;
            m_DeleteSelectedBtn.style.borderTopLeftRadius = 3;
            m_DeleteSelectedBtn.style.borderTopRightRadius = 3;
            m_DeleteSelectedBtn.style.borderBottomLeftRadius = 3;
            m_DeleteSelectedBtn.style.borderBottomRightRadius = 3;
            m_ActionBar.Add(m_DeleteSelectedBtn);

            m_IgnoreSelectedBtn = new Button(OnIgnoreSelected)
            {
                text = "Ignore Selected"
            };
            m_IgnoreSelectedBtn.AddToClassList("pcp-button-secondary");
            m_IgnoreSelectedBtn.style.paddingLeft = 10;
            m_IgnoreSelectedBtn.style.paddingRight = 10;
            m_IgnoreSelectedBtn.style.paddingTop = 4;
            m_IgnoreSelectedBtn.style.paddingBottom = 4;
            m_IgnoreSelectedBtn.style.marginRight = 4;
            m_ActionBar.Add(m_IgnoreSelectedBtn);

            m_ExportBtn = new Button(OnExport)
            {
                text = "Export"
            };
            m_ExportBtn.AddToClassList("pcp-button-secondary");
            m_ExportBtn.style.paddingLeft = 10;
            m_ExportBtn.style.paddingRight = 10;
            m_ExportBtn.style.paddingTop = 4;
            m_ExportBtn.style.paddingBottom = 4;
            m_ActionBar.Add(m_ExportBtn);

            // Spacer + selection count
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            m_ActionBar.Add(spacer);

            Add(m_ActionBar);

            // Initial population
            RefreshFromModule();
        }

        // --------------------------------------------------------------------
        // Abstract
        // --------------------------------------------------------------------

        /// <summary>
        /// Called after a scan completes (or on initial display) to convert
        /// module-specific result data into the result list's data source.
        /// Subclasses should call <see cref="PCPResultListView.SetData"/>
        /// on <see cref="m_ResultList"/>.
        /// </summary>
        protected abstract void PopulateResults();

        // --------------------------------------------------------------------
        // Public API
        // --------------------------------------------------------------------

        /// <summary>
        /// Reads current module data and populates the list.
        /// </summary>
        public void RefreshFromModule()
        {
            PopulateResults();
        }

        /// <summary>
        /// Called when a scan completes. Refreshes the list with new data.
        /// </summary>
        public void OnScanComplete()
        {
            m_Header.IsScanning = false;
            RefreshFromModule();
        }

        // --------------------------------------------------------------------
        // Event handlers
        // --------------------------------------------------------------------

        private void OnScanClicked()
        {
            m_Header.IsScanning = true;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    var context = m_CreateContext?.Invoke();
                    if (context != null)
                    {
                        DoModuleScan(context);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProjectCleanPro] Scan failed: {ex}");
                }
                finally
                {
                    OnScanComplete();
                }
            };
        }

        /// <summary>
        /// Override in subclasses to perform the actual module scan.
        /// Default implementation does nothing.
        /// </summary>
        protected virtual void DoModuleScan(PCPScanContext context) { }

        private void OnFilterChanged(PCPFilterState filterState)
        {
            if (filterState == null)
                return;

            m_ResultList.ApplyFilters(
                filterState.searchText,
                filterState.activeTypes.Count > 0 ? filterState.activeTypes : null,
                filterState.severityFilter);
        }

        private void OnResultContextMenu(Vector2 mousePos, int rawIndex)
        {
            var menu = new GenericMenu();

            if (rawIndex >= 0)
            {
                var selectedRows = m_ResultList.GetSelectedRowData();
                PCPRowData? clickedRow = null;

                // Get the clicked row data
                var items = m_ResultList.ItemsSource;
                if (items != null && rawIndex < items.Count)
                {
                    var allSelected = m_ResultList.GetSelectedRowData();
                    if (allSelected.Count > 0)
                        clickedRow = allSelected[0];
                }

                menu.AddItem(new GUIContent("Ping in Project"), false, () =>
                {
                    if (clickedRow.HasValue && !string.IsNullOrEmpty(clickedRow.Value.path))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(clickedRow.Value.path);
                        if (obj != null)
                            EditorGUIUtility.PingObject(obj);
                    }
                });

                menu.AddItem(new GUIContent("Add to Ignore List"), false, () =>
                {
                    if (clickedRow.HasValue && !string.IsNullOrEmpty(clickedRow.Value.path))
                    {
                        AddPathToIgnoreList(clickedRow.Value.path);
                    }
                });

                menu.AddItem(new GUIContent("Show Dependencies"), false, () =>
                {
                    if (clickedRow.HasValue && !string.IsNullOrEmpty(clickedRow.Value.path))
                    {
                        ShowDependencies(clickedRow.Value.path);
                    }
                });

                menu.AddSeparator("");

                menu.AddItem(new GUIContent("Preview Delete"), false, () =>
                {
                    if (clickedRow.HasValue && !string.IsNullOrEmpty(clickedRow.Value.path))
                    {
                        PreviewDeleteSingle(clickedRow.Value.path);
                    }
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("No item selected"));
            }

            menu.ShowAsContext();
        }

        // --------------------------------------------------------------------
        // Actions
        // --------------------------------------------------------------------

        private void OnDeleteSelected()
        {
            var selectedRows = m_ResultList.GetSelectedRowData();
            if (selectedRows.Count == 0)
            {
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    "No items selected. Use the checkboxes to select items.", "OK");
                return;
            }

            var paths = new List<string>();
            foreach (var row in selectedRows)
            {
                if (!string.IsNullOrEmpty(row.path))
                    paths.Add(row.path);
            }

            if (paths.Count == 0)
                return;

            var context = m_CreateContext?.Invoke();
            var resolver = context?.DependencyResolver;
            var preview = PCPSafeDelete.Preview(paths, resolver);

            if (PCPDeletePreviewDialog.Show(preview, out bool archive))
            {
                var settings = PCPContext.Settings;
                bool originalArchive = settings.archiveBeforeDelete;
                settings.archiveBeforeDelete = archive;

                try
                {
                    PCPSafeDelete.ArchiveAndDelete(preview, settings);
                    RefreshFromModule();
                }
                finally
                {
                    settings.archiveBeforeDelete = originalArchive;
                }
            }
        }

        private void OnIgnoreSelected()
        {
            var selectedRows = m_ResultList.GetSelectedRowData();
            if (selectedRows.Count == 0)
            {
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    "No items selected. Use the checkboxes to select items.", "OK");
                return;
            }

            foreach (var row in selectedRows)
            {
                if (!string.IsNullOrEmpty(row.path))
                    AddPathToIgnoreList(row.path);
            }

            m_ResultList.ClearSelection();
            RefreshFromModule();
        }

        private void OnExport()
        {
            if (m_ScanResult == null)
                return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Export as JSON"), false,
                () => PCPReportExporter.ExportJSON(m_ScanResult));
            menu.AddItem(new GUIContent("Export as CSV"), false,
                () => PCPReportExporter.ExportCSV(m_ScanResult));
            menu.AddItem(new GUIContent("Export as HTML"), false,
                () => PCPReportExporter.ExportHTML(m_ScanResult));
            menu.ShowAsContext();
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
                comment = "Added via context menu",
                enabled = true
            };

            settings.ignoreRules.Add(rule);
            settings.Save();
            Debug.Log($"[ProjectCleanPro] Added ignore rule: {path}");
        }

        private void ShowDependencies(string path)
        {
            var context = m_CreateContext?.Invoke();
            var resolver = context?.DependencyResolver;
            if (resolver == null || !resolver.IsBuilt)
            {
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    "Dependency graph not yet built. Run a scan first.", "OK");
                return;
            }

            var deps = resolver.GetDependencies(path);
            var dependents = resolver.GetDependents(path);

            string message = $"Asset: {path}\n\n" +
                $"Dependencies ({deps.Count}):\n" +
                string.Join("\n", deps.Take(20)) +
                (deps.Count > 20 ? $"\n...and {deps.Count - 20} more" : "") +
                $"\n\nDependent on this ({dependents.Count}):\n" +
                string.Join("\n", dependents.Take(20)) +
                (dependents.Count > 20 ? $"\n...and {dependents.Count - 20} more" : "");

            EditorUtility.DisplayDialog("Dependencies", message, "OK");
        }

        private void PreviewDeleteSingle(string path)
        {
            var context = m_CreateContext?.Invoke();
            var resolver = context?.DependencyResolver;
            var preview = PCPSafeDelete.Preview(new List<string> { path }, resolver);

            if (PCPDeletePreviewDialog.Show(preview, out bool archive))
            {
                var settings = PCPContext.Settings;
                bool originalArchive = settings.archiveBeforeDelete;
                settings.archiveBeforeDelete = archive;

                try
                {
                    PCPSafeDelete.ArchiveAndDelete(preview, settings);
                    RefreshFromModule();
                }
                finally
                {
                    settings.archiveBeforeDelete = originalArchive;
                }
            }
        }
    }
}
