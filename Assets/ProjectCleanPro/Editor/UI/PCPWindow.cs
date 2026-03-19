using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Main EditorWindow for ProjectCleanPro.
    /// Provides a tabbed interface with a left navigation bar and swappable content area.
    /// </summary>
    public sealed class PCPWindow : EditorWindow
    {
        // --------------------------------------------------------------------
        // Constants
        // --------------------------------------------------------------------

        private const string WindowTitle = "ProjectClean Pro";
        private const float MinWidth = 900f;
        private const float MinHeight = 500f;

        private static readonly string[] k_StyleSheetPaths = new[]
        {
            "Assets/ProjectCleanPro/Editor/UI/Styles/PCPVariables.uss",
            "Assets/ProjectCleanPro/Editor/UI/Styles/PCPCommon.uss",
            "Assets/ProjectCleanPro/Editor/UI/Styles/PCPDashboard.uss",
        };

        // --------------------------------------------------------------------
        // Tab definitions
        // --------------------------------------------------------------------

        private struct TabDefinition
        {
            public string label;
            public string icon;
            public string moduleId;
        }

        private static readonly TabDefinition[] k_Tabs = new[]
        {
            new TabDefinition { label = "Dashboard",    icon = "\u2302", moduleId = null          },
            new TabDefinition { label = "Unused",       icon = "\u2716", moduleId = "unused"      },
            new TabDefinition { label = "Missing",      icon = "\u26A0", moduleId = "missing"     },
            new TabDefinition { label = "Duplicates",   icon = "\u2687", moduleId = "duplicates"  },
            new TabDefinition { label = "Dependencies", icon = "\u2194", moduleId = "dependencies"},
            new TabDefinition { label = "Packages",     icon = "\u2750", moduleId = "packages"    },
            new TabDefinition { label = "Shaders",      icon = "\u2726", moduleId = "shaders"     },
            new TabDefinition { label = "Size",         icon = "\u25A3", moduleId = "size"        },
            new TabDefinition { label = "Settings",     icon = "\u2699", moduleId = null          },
        };

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private int m_ActiveTabIndex;
        private VisualElement m_ContentArea;
        private VisualElement m_TabBar;
        private Label m_StatusLabel;
        private Label m_FindingsLabel;
        private VisualElement m_ProgressOverlay;
        private Label m_ProgressLabel;
        private VisualElement m_ProgressBarFill;
        private List<Button> m_TabButtons = new List<Button>();

        // Modules (lazily created, may be null if not yet registered)
        private readonly List<IPCPModule> m_Modules = new List<IPCPModule>();

        // Views (one per tab)
        private VisualElement[] m_Views;

        // Scan state
        private CancellationTokenSource m_ScanCts;
        private DateTime m_LastScanTime;
        private PCPScanResult m_LastScanResult;

        // --------------------------------------------------------------------
        // Menu item
        // --------------------------------------------------------------------

        [MenuItem("Tools/ProjectClean Pro/Open Window %&p")]
        public static void ShowWindow()
        {
            var window = GetWindow<PCPWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWidth, MinHeight);
            window.Show();
        }

        // --------------------------------------------------------------------
        // Lifecycle
        // --------------------------------------------------------------------

        private void OnEnable()
        {
            PCPContext.Initialize();
            m_LastScanResult = new PCPScanResult();

            RegisterModule(new PCPUnusedScanner());
            RegisterModule(new PCPMissingRefScanner());
            RegisterModule(new PCPDuplicateDetector());
            RegisterModule(new PCPDependencyModule());
            RegisterModule(new PCPPackageAuditor());
            RegisterModule(new PCPShaderAnalyzer());
            RegisterModule(new PCPSizeProfiler());
        }

        private void OnDisable()
        {
            m_ScanCts?.Cancel();
            m_ScanCts?.Dispose();
            m_ScanCts = null;
            PCPContext.Dispose();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            // Load stylesheets
            foreach (string path in k_StyleSheetPaths)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (sheet != null)
                    root.styleSheets.Add(sheet);
            }

            // Root container: horizontal flex
            root.AddToClassList("pcp-root");

            // -- Left tab bar --
            m_TabBar = new VisualElement();
            m_TabBar.AddToClassList("pcp-tab-bar");
            root.Add(m_TabBar);

            BuildTabBar();

            // -- Right area: content + status bar --
            var rightPanel = new VisualElement();
            rightPanel.style.flexGrow = 1;
            rightPanel.style.flexDirection = FlexDirection.Column;
            root.Add(rightPanel);

            // Content area
            m_ContentArea = new VisualElement();
            m_ContentArea.AddToClassList("pcp-content-area");
            rightPanel.Add(m_ContentArea);

            // Progress overlay (hidden by default)
            BuildProgressOverlay(rightPanel);

            // Status bar
            BuildStatusBar(rightPanel);

            // Create views
            BuildViews();

            // Activate default tab
            SwitchToTab(0);
        }

        // --------------------------------------------------------------------
        // Tab bar construction
        // --------------------------------------------------------------------

        private void BuildTabBar()
        {
            m_TabButtons.Clear();

            // Tool title at top of tab bar
            var titleLabel = new Label(WindowTitle);
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.83f, 0.83f, 0.83f);
            titleLabel.style.paddingLeft = 16;
            titleLabel.style.paddingTop = 8;
            titleLabel.style.paddingBottom = 12;
            m_TabBar.Add(titleLabel);

            // Separator
            var sep = new VisualElement();
            sep.AddToClassList("pcp-separator");
            m_TabBar.Add(sep);

            for (int i = 0; i < k_Tabs.Length; i++)
            {
                int tabIndex = i;
                var def = k_Tabs[i];

                var button = new Button(() => SwitchToTab(tabIndex));
                button.AddToClassList("pcp-tab-button");
                button.text = $"{def.icon}  {def.label}";
                button.style.borderTopWidth = 0;
                button.style.borderBottomWidth = 0;
                button.style.borderRightWidth = 0;

                m_TabButtons.Add(button);
                m_TabBar.Add(button);

                // Add separator before Settings tab
                if (i == k_Tabs.Length - 2)
                {
                    var spacer = new VisualElement();
                    spacer.style.flexGrow = 1;
                    m_TabBar.Add(spacer);

                    var sep2 = new VisualElement();
                    sep2.AddToClassList("pcp-separator");
                    m_TabBar.Add(sep2);
                }
            }
        }

        // --------------------------------------------------------------------
        // Progress overlay
        // --------------------------------------------------------------------

        private void BuildProgressOverlay(VisualElement parent)
        {
            m_ProgressOverlay = new VisualElement();
            m_ProgressOverlay.style.position = Position.Absolute;
            m_ProgressOverlay.style.top = 0;
            m_ProgressOverlay.style.bottom = 0;
            m_ProgressOverlay.style.left = 0;
            m_ProgressOverlay.style.right = 0;
            m_ProgressOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            m_ProgressOverlay.style.justifyContent = Justify.Center;
            m_ProgressOverlay.style.alignItems = Align.Center;
            m_ProgressOverlay.style.display = DisplayStyle.None;

            var progressBox = new VisualElement();
            progressBox.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            progressBox.style.borderTopLeftRadius = 6;
            progressBox.style.borderTopRightRadius = 6;
            progressBox.style.borderBottomLeftRadius = 6;
            progressBox.style.borderBottomRightRadius = 6;
            progressBox.style.paddingTop = 24;
            progressBox.style.paddingBottom = 24;
            progressBox.style.paddingLeft = 32;
            progressBox.style.paddingRight = 32;
            progressBox.style.minWidth = 300;
            progressBox.style.alignItems = Align.Center;

            var title = new Label("Scanning Project...");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.83f, 0.83f, 0.83f);
            title.style.marginBottom = 12;
            progressBox.Add(title);

            m_ProgressLabel = new Label("Initializing...");
            m_ProgressLabel.style.fontSize = 12;
            m_ProgressLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            m_ProgressLabel.style.marginBottom = 12;
            progressBox.Add(m_ProgressLabel);

            // Progress bar track
            var progressTrack = new VisualElement();
            progressTrack.style.width = 260;
            progressTrack.style.height = 6;
            progressTrack.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
            progressTrack.style.borderTopLeftRadius = 3;
            progressTrack.style.borderTopRightRadius = 3;
            progressTrack.style.borderBottomLeftRadius = 3;
            progressTrack.style.borderBottomRightRadius = 3;
            progressTrack.style.marginBottom = 16;

            m_ProgressBarFill = new VisualElement();
            m_ProgressBarFill.style.height = new StyleLength(Length.Percent(100));
            m_ProgressBarFill.style.width = new StyleLength(Length.Percent(0));
            m_ProgressBarFill.style.backgroundColor = new Color(0.337f, 0.612f, 0.839f);
            m_ProgressBarFill.style.borderTopLeftRadius = 3;
            m_ProgressBarFill.style.borderTopRightRadius = 3;
            m_ProgressBarFill.style.borderBottomLeftRadius = 3;
            m_ProgressBarFill.style.borderBottomRightRadius = 3;
            progressTrack.Add(m_ProgressBarFill);
            progressBox.Add(progressTrack);

            var cancelButton = new Button(CancelScan) { text = "Cancel" };
            cancelButton.AddToClassList("pcp-button-secondary");
            progressBox.Add(cancelButton);

            m_ProgressOverlay.Add(progressBox);
            parent.Add(m_ProgressOverlay);
        }

        // --------------------------------------------------------------------
        // Status bar
        // --------------------------------------------------------------------

        private void BuildStatusBar(VisualElement parent)
        {
            var statusBar = new VisualElement();
            statusBar.AddToClassList("pcp-status-bar");

            m_StatusLabel = new Label("Ready");
            m_StatusLabel.style.flexGrow = 1;
            statusBar.Add(m_StatusLabel);

            m_FindingsLabel = new Label("");
            m_FindingsLabel.style.marginLeft = 16;
            statusBar.Add(m_FindingsLabel);

            parent.Add(statusBar);
        }

        // --------------------------------------------------------------------
        // View construction
        // --------------------------------------------------------------------

        private void BuildViews()
        {
            m_Views = new VisualElement[k_Tabs.Length];

            // 0: Dashboard
            m_Views[0] = new PCPDashboardView(m_Modules, m_LastScanResult, SwitchToTab, ScanAll);

            // 1: Unused
            m_Views[1] = new PCPUnusedView(m_LastScanResult, CreateScanContext);

            // 2: Missing
            m_Views[2] = new PCPMissingRefsView(m_LastScanResult, CreateScanContext);

            // 3: Duplicates
            m_Views[3] = new PCPDuplicatesView(m_LastScanResult, CreateScanContext);

            // 4: Dependencies
            m_Views[4] = new PCPDependencyGraphView();

            // 5: Packages
            m_Views[5] = new PCPPackagesView(m_LastScanResult, CreateScanContext);

            // 6: Shaders
            m_Views[6] = new PCPShadersView(m_LastScanResult, CreateScanContext);

            // 7: Size
            m_Views[7] = new PCPSizeView(m_LastScanResult, CreateScanContext);

            // 8: Settings
            m_Views[8] = new PCPSettingsView();
        }

        // --------------------------------------------------------------------
        // Tab switching
        // --------------------------------------------------------------------

        private void SwitchToTab(int index)
        {
            if (index < 0 || index >= k_Tabs.Length)
                return;

            m_ActiveTabIndex = index;

            // Update tab button styles
            for (int i = 0; i < m_TabButtons.Count; i++)
            {
                if (i == index)
                {
                    m_TabButtons[i].AddToClassList("pcp-tab-button--active");

                    // Set accent color for the active tab
                    Color accentColor = GetTabAccentColor(i);
                    m_TabButtons[i].style.borderLeftColor = accentColor;
                }
                else
                {
                    m_TabButtons[i].RemoveFromClassList("pcp-tab-button--active");
                    m_TabButtons[i].style.borderLeftColor = Color.clear;
                }
            }

            // Swap content
            m_ContentArea.Clear();

            if (m_Views != null && index < m_Views.Length && m_Views[index] != null)
            {
                m_ContentArea.Add(m_Views[index]);

                if (m_Views[index] is PCPDashboardView dashboardView)
                    dashboardView.RefreshData();
                else if (m_Views[index] is PCPModuleView moduleView)
                    moduleView.RefreshFromModule();
                else if (m_Views[index] is PCPDuplicatesView duplicatesView)
                    duplicatesView.RefreshGroups();
                else if (m_Views[index] is PCPPackagesView packagesView)
                    packagesView.RefreshCards();
                else if (m_Views[index] is PCPSizeView sizeView)
                    sizeView.RefreshData();
            }
        }

        // --------------------------------------------------------------------
        // Accent color mapping
        // --------------------------------------------------------------------

        private Color GetTabAccentColor(int tabIndex)
        {
            var settings = PCPContext.Settings;
            if (settings == null || settings.moduleColors == null)
                return new Color(0.337f, 0.612f, 0.839f);

            switch (tabIndex)
            {
                case 0: return new Color(0.337f, 0.612f, 0.839f); // Dashboard - blue
                case 1: return settings.moduleColors.Length > 0 ? settings.moduleColors[0] : Color.red;
                case 2: return settings.moduleColors.Length > 1 ? settings.moduleColors[1] : Color.yellow;
                case 3: return settings.moduleColors.Length > 3 ? settings.moduleColors[3] : Color.magenta;
                case 4: return settings.moduleColors.Length > 5 ? settings.moduleColors[5] : Color.cyan;
                case 5: return settings.moduleColors.Length > 4 ? settings.moduleColors[4] : Color.green;
                case 6: return settings.moduleColors.Length > 2 ? settings.moduleColors[2] : Color.white;
                case 7: return settings.moduleColors.Length > 7 ? settings.moduleColors[7] : Color.yellow;
                case 8: return new Color(0.5f, 0.5f, 0.5f); // Settings - gray
                default: return new Color(0.337f, 0.612f, 0.839f);
            }
        }

        // --------------------------------------------------------------------
        // Scan all
        // --------------------------------------------------------------------

        /// <summary>
        /// Runs all registered modules sequentially and updates the UI.
        /// </summary>
        public void ScanAll()
        {
            if (m_ScanCts != null)
                return; // Already scanning

            m_ScanCts = new CancellationTokenSource();
            m_LastScanTime = DateTime.UtcNow;
            ShowProgressOverlay(true);

            EditorApplication.delayCall += () =>
            {
                try
                {
                    var scanContext = CreateScanContext();
                    int total = m_Modules.Count;

                    m_LastScanResult.Clear();

                    for (int i = 0; i < total; i++)
                    {
                        if (m_ScanCts == null || m_ScanCts.IsCancellationRequested)
                            break;

                        var module = m_Modules[i];
                        float baseProgress = (float)i / total;
                        UpdateProgress(baseProgress, $"Scanning: {module.DisplayName}...");

                        module.Scan(scanContext);
                        CollectModuleResults(module, m_LastScanResult);
                    }

                    // Aggregate results
                    m_LastScanResult.totalAssetsScanned = UnityEditor.AssetDatabase.GetAllAssetPaths().Length;
                    m_LastScanResult.scanTimestampUtc = m_LastScanTime.ToString("o");
                    m_LastScanResult.scanDurationSeconds = (float)(DateTime.UtcNow - m_LastScanTime).TotalSeconds;
                    m_LastScanTime = DateTime.UtcNow;

                    UpdateStatusBar();
                    RefreshActiveView();
                }
                catch (OperationCanceledException)
                {
                    Debug.Log("[ProjectCleanPro] Scan cancelled.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProjectCleanPro] Scan failed: {ex}");
                }
                finally
                {
                    ShowProgressOverlay(false);
                    m_ScanCts?.Dispose();
                    m_ScanCts = null;
                }
            };
        }

        private void CancelScan()
        {
            m_ScanCts?.Cancel();
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private PCPScanContext CreateScanContext()
        {
            PCPContext.Initialize();
            return new PCPScanContext(
                PCPContext.Settings,
                PCPContext.IgnoreRules,
                PCPContext.DependencyResolver,
                PCPContext.ScanCache,
                PCPContext.RenderPipelineDetector.Info);
        }

        private void ShowProgressOverlay(bool show)
        {
            if (m_ProgressOverlay != null)
                m_ProgressOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateProgress(float progress, string label)
        {
            if (m_ProgressBarFill != null)
                m_ProgressBarFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            if (m_ProgressLabel != null)
                m_ProgressLabel.text = label;
        }

        private void UpdateStatusBar()
        {
            if (m_StatusLabel != null)
            {
                string timeText = m_LastScanTime == default
                    ? "Not scanned yet"
                    : $"Last scan: {m_LastScanTime:HH:mm:ss}";
                m_StatusLabel.text = timeText;
            }

            if (m_FindingsLabel != null && m_LastScanResult != null)
            {
                int total = m_LastScanResult.TotalFindingCount;
                string wastedSize = m_LastScanResult.FormattedWastedBytes;
                m_FindingsLabel.text = total > 0
                    ? $"{total} findings | {wastedSize} reclaimable"
                    : "No findings";
            }
        }

        private void RefreshActiveView()
        {
            // Trigger a re-switch to the same tab to refresh the content
            if (m_ActiveTabIndex >= 0 && m_ActiveTabIndex < k_Tabs.Length)
            {
                SwitchToTab(m_ActiveTabIndex);
            }
        }

        private static void CollectModuleResults(IPCPModule module, PCPScanResult result)
        {
            switch (module.ModuleId)
            {
                case "unused" when module is PCPUnusedScanner u:
                    result.unusedAssets.AddRange(u.Results);
                    break;
                case "missing" when module is PCPMissingRefScanner m:
                    result.missingReferences.AddRange(m.Results);
                    break;
                case "duplicates" when module is PCPDuplicateDetector d:
                    result.duplicateGroups.AddRange(d.Results);
                    break;
                case "packages" when module is PCPPackageAuditor p:
                    result.packageAuditEntries.AddRange(p.Results);
                    break;
                case "shaders" when module is PCPShaderAnalyzer s:
                    result.shaderEntries.AddRange(s.Results);
                    break;
                case "size" when module is PCPSizeProfiler z:
                    result.sizeEntries.AddRange(z.Results);
                    break;
            }
        }

        // --------------------------------------------------------------------
        // Module registration
        // --------------------------------------------------------------------

        /// <summary>
        /// Registers a scan module. Called during initialization to provide
        /// access to scanner instances.
        /// </summary>
        public void RegisterModule(IPCPModule module)
        {
            if (module == null) return;
            if (!m_Modules.Exists(m => m.ModuleId == module.ModuleId))
                m_Modules.Add(module);
        }
    }
}
