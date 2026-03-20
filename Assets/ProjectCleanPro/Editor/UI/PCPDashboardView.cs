using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Dashboard view showing summary cards for each module, a health score,
    /// and a prominent "Scan All" button.
    /// </summary>
    public sealed class PCPDashboardView : VisualElement, IPCPRefreshable
    {
        // --------------------------------------------------------------------
        // Module card definition
        // --------------------------------------------------------------------

        private struct ModuleCardDef
        {
            public string name;
            public string icon;
            public string cssModifier;
            public int tabIndex;
            public Func<int> getFindingCount;
            public Func<long> getTotalSize;
        }

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private readonly List<IPCPModule> m_Modules;
        private readonly PCPScanResult m_ScanResult;
        private readonly Action<int> m_OnTabSwitch;
        private readonly Action m_OnScanAll;

        // Summary elements
        private Label m_TotalFindingsValue;
        private Label m_WastedSpaceValue;
        private Label m_HealthScoreValue;
        private VisualElement m_CardGrid;

        // Per-card elements
        private readonly List<Label> m_CardCountLabels = new List<Label>();
        private readonly List<Label> m_CardSizeLabels = new List<Label>();
        private readonly List<Label> m_CardStatusLabels = new List<Label>();
        private readonly List<VisualElement> m_CardHeaders = new List<VisualElement>();

        // Card definitions
        private static readonly ModuleCardDef[] k_CardDefs;

        // --------------------------------------------------------------------
        // Static constructor
        // --------------------------------------------------------------------

        static PCPDashboardView()
        {
            k_CardDefs = new[]
            {
                new ModuleCardDef { name = "Unused Assets",      icon = "\u2716", cssModifier = "unused",       tabIndex = 1 },
                new ModuleCardDef { name = "Missing References", icon = "\u26A0", cssModifier = "missing",      tabIndex = 2 },
                new ModuleCardDef { name = "Duplicates",         icon = "\u2687", cssModifier = "duplicates",   tabIndex = 3 },
                new ModuleCardDef { name = "Dependencies",       icon = "\u2194", cssModifier = "dependencies", tabIndex = 4 },
                new ModuleCardDef { name = "Packages",           icon = "\u2750", cssModifier = "packages",     tabIndex = 5 },
                new ModuleCardDef { name = "Shaders",            icon = "\u2726", cssModifier = "shaders",      tabIndex = 6 },
                new ModuleCardDef { name = "Size Profiler",      icon = "\u25A3", cssModifier = "size",         tabIndex = 7 },
            };
        }

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPDashboardView(
            List<IPCPModule> modules,
            PCPScanResult scanResult,
            Action<int> onTabSwitch,
            Action onScanAll)
        {
            m_Modules = modules;
            m_ScanResult = scanResult;
            m_OnTabSwitch = onTabSwitch;
            m_OnScanAll = onScanAll;

            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("pcp-scroll-view");
            Add(scrollView);

            var container = scrollView.contentContainer;
            container.style.paddingTop = 24;
            container.style.paddingBottom = 24;
            container.style.paddingLeft = 24;
            container.style.paddingRight = 24;

            // Scan All button at top
            BuildScanAllButton(container);

            // Summary row
            BuildSummaryRow(container);

            // Card grid
            BuildCardGrid(container);

            // Initial refresh
            RefreshData();
        }

        // --------------------------------------------------------------------
        // Scan All button
        // --------------------------------------------------------------------

        private void BuildScanAllButton(VisualElement parent)
        {
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.Center;
            btnRow.style.marginBottom = 16;

            var scanAllBtn = new Button(() => m_OnScanAll?.Invoke());
            scanAllBtn.text = "Scan All Modules";
            scanAllBtn.AddToClassList("pcp-dashboard__scan-all-button");
            btnRow.Add(scanAllBtn);

            parent.Add(btnRow);
        }

        // --------------------------------------------------------------------
        // Summary row
        // --------------------------------------------------------------------

        private void BuildSummaryRow(VisualElement parent)
        {
            var summary = new VisualElement();
            summary.AddToClassList("pcp-dashboard__summary");

            // Total findings
            var findingsItem = BuildSummaryItem("0", "Total Findings");
            m_TotalFindingsValue = findingsItem.Q<Label>("summary-value");
            summary.Add(findingsItem);

            // Wasted space
            var wastedItem = BuildSummaryItem("0 B", "Wasted Space");
            m_WastedSpaceValue = wastedItem.Q<Label>("summary-value");
            summary.Add(wastedItem);

            // Health score
            var healthItem = BuildSummaryItem("100%", "Project Health");
            m_HealthScoreValue = healthItem.Q<Label>("summary-value");
            summary.Add(healthItem);

            parent.Add(summary);
        }

        private VisualElement BuildSummaryItem(string value, string label)
        {
            var item = new VisualElement();
            item.AddToClassList("pcp-dashboard__summary-item");

            var valueLabel = new Label(value);
            valueLabel.name = "summary-value";
            valueLabel.AddToClassList("pcp-dashboard__summary-value");
            item.Add(valueLabel);

            var textLabel = new Label(label);
            textLabel.AddToClassList("pcp-dashboard__summary-label");
            item.Add(textLabel);

            return item;
        }

        // --------------------------------------------------------------------
        // Card grid
        // --------------------------------------------------------------------

        private void BuildCardGrid(VisualElement parent)
        {
            m_CardGrid = new VisualElement();
            m_CardGrid.AddToClassList("pcp-dashboard");
            parent.Add(m_CardGrid);

            m_CardCountLabels.Clear();
            m_CardSizeLabels.Clear();
            m_CardStatusLabels.Clear();
            m_CardHeaders.Clear();

            for (int i = 0; i < k_CardDefs.Length; i++)
            {
                var def = k_CardDefs[i];
                int tabIndex = def.tabIndex;

                var card = new VisualElement();
                card.AddToClassList("pcp-dashboard__card");
                card.AddToClassList($"pcp-dashboard__card--{def.cssModifier}");

                card.RegisterCallback<ClickEvent>(evt => m_OnTabSwitch?.Invoke(tabIndex));

                // Card header
                var header = new VisualElement();
                header.AddToClassList("pcp-dashboard__card-header");

                // Apply module accent color from settings (overrides USS variable)
                var settings = PCPContext.Settings;
                if (settings != null)
                {
                    // tabIndex 1-7 maps to moduleColors 0-6
                    header.style.borderLeftColor = settings.GetModuleColor(tabIndex - 1);
                }

                var iconLabel = new Label(def.icon);
                iconLabel.AddToClassList("pcp-dashboard__card-icon");
                iconLabel.style.fontSize = 16;
                iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                header.Add(iconLabel);

                var titleLabel = new Label(def.name);
                titleLabel.AddToClassList("pcp-dashboard__card-title");
                header.Add(titleLabel);

                card.Add(header);
                m_CardHeaders.Add(header);

                // Finding count (large number)
                var countLabel = new Label("0");
                countLabel.AddToClassList("pcp-dashboard__card-count");
                card.Add(countLabel);
                m_CardCountLabels.Add(countLabel);

                // Size info
                var sizeLabel = new Label("0 B");
                sizeLabel.AddToClassList("pcp-dashboard__card-size");
                card.Add(sizeLabel);
                m_CardSizeLabels.Add(sizeLabel);

                // Status / last scan
                var statusRow = new VisualElement();
                statusRow.AddToClassList("pcp-dashboard__card-status");

                var statusLabel = new Label("Not scanned");
                statusRow.Add(statusLabel);
                m_CardStatusLabels.Add(statusLabel);

                card.Add(statusRow);

                // Quick action buttons
                var actionsRow = new VisualElement();
                actionsRow.style.flexDirection = FlexDirection.Row;
                actionsRow.style.marginTop = 8;

                var viewBtn = new Button(() => m_OnTabSwitch?.Invoke(tabIndex));
                viewBtn.text = "View Results";
                viewBtn.AddToClassList("pcp-button-secondary");
                viewBtn.style.height = 22;
                viewBtn.style.fontSize = 10;
                viewBtn.style.paddingLeft = 8;
                viewBtn.style.paddingRight = 8;
                viewBtn.style.marginRight = 4;
                actionsRow.Add(viewBtn);

                card.Add(actionsRow);

                m_CardGrid.Add(card);
            }
        }

        // --------------------------------------------------------------------
        // Data refresh
        // --------------------------------------------------------------------

        /// <summary>
        /// Refreshes all dashboard data from the current scan result.
        /// </summary>
        public void Refresh() => RefreshData();

        public void RefreshData()
        {
            RefreshCardColors();

            if (m_ScanResult == null) return;

            int totalFindings = m_ScanResult.TotalFindingCount;
            string wastedSpace = m_ScanResult.FormattedWastedBytes;

            int health = m_ScanResult.HealthScore;

            if (m_TotalFindingsValue != null)
                m_TotalFindingsValue.text = totalFindings.ToString();
            if (m_WastedSpaceValue != null)
                m_WastedSpaceValue.text = wastedSpace;
            if (m_HealthScoreValue != null)
            {
                m_HealthScoreValue.text = $"{health}%";
                if (health >= 80)
                    m_HealthScoreValue.style.color = new Color(0.416f, 0.600f, 0.333f); // green
                else if (health >= 50)
                    m_HealthScoreValue.style.color = new Color(0.800f, 0.655f, 0f); // yellow
                else
                    m_HealthScoreValue.style.color = new Color(0.957f, 0.278f, 0.278f); // red
            }

            // Update per-card data
            int[] cardCounts = GetCardFindingCounts();
            long[] cardSizes = GetCardSizes();

            for (int i = 0; i < k_CardDefs.Length && i < m_CardCountLabels.Count; i++)
            {
                m_CardCountLabels[i].text = cardCounts[i].ToString();
                m_CardSizeLabels[i].text = PCPAssetInfo.FormatBytes(cardSizes[i]);

                if (!string.IsNullOrEmpty(m_ScanResult.scanTimestampUtc))
                {
                    string statusText = cardCounts[i] == 0
                        ? "Clean"
                        : $"{cardCounts[i]} issue(s) found";
                    m_CardStatusLabels[i].text = statusText;

                    // Apply status class
                    m_CardStatusLabels[i].RemoveFromClassList("pcp-dashboard__card-status--clean");
                    m_CardStatusLabels[i].RemoveFromClassList("pcp-dashboard__card-status--issues");
                    m_CardStatusLabels[i].AddToClassList(cardCounts[i] == 0
                        ? "pcp-dashboard__card-status--clean"
                        : "pcp-dashboard__card-status--issues");
                }
            }
        }

        private int[] GetCardFindingCounts()
        {
            return new[]
            {
                m_ScanResult.unusedAssets.Count,
                m_ScanResult.missingReferences.Count,
                m_ScanResult.duplicateGroups.Count,
                0, // Dependencies (not a "finding" count)
                m_ScanResult.packageAuditEntries.Count,
                m_ScanResult.shaderEntries.Count,
                m_ScanResult.sizeEntries.Count,
            };
        }

        private void RefreshCardColors()
        {
            var settings = PCPContext.Settings;
            if (settings == null) return;

            for (int i = 0; i < m_CardHeaders.Count && i < k_CardDefs.Length; i++)
            {
                int tabIndex = k_CardDefs[i].tabIndex;
                m_CardHeaders[i].style.borderLeftColor = settings.GetModuleColor(tabIndex - 1);
            }
        }

        private long[] GetCardSizes()
        {
            long unusedSize = 0;
            foreach (var u in m_ScanResult.unusedAssets) unusedSize += u.SizeBytes;

            long dupeSize = 0;
            foreach (var g in m_ScanResult.duplicateGroups) dupeSize += g.WastedBytes;

            long totalSize = 0;
            foreach (var s in m_ScanResult.sizeEntries) totalSize += s.sizeBytes;

            return new long[]
            {
                unusedSize,
                0, // Missing refs have no direct size
                dupeSize,
                0, // Dependencies
                0, // Packages
                0, // Shaders
                totalSize,
            };
        }
    }
}
