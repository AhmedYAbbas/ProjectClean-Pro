using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for displaying shader analysis results. Extends <see cref="PCPModuleView"/>
    /// with delete, ignore, and export capabilities, plus a detail panel that
    /// shows keyword lists on selection.
    /// </summary>
    public sealed class PCPShadersView : PCPModuleView,
        IPCPDeletableView, IPCPIgnorableView, IPCPExportableView
    {
        // Colors
        private static readonly Color k_HighVariantColor = new Color(0.957f, 0.278f, 0.278f, 1f);
        private static readonly Color k_MismatchColor = new Color(0.800f, 0.655f, 0.000f, 1f);
        private static readonly Color k_UnusedColor = new Color(0.753f, 0.224f, 0.169f, 1f);
        private static readonly Color k_OkColor = new Color(0.416f, 0.600f, 0.333f, 1f);

        private const int HighVariantThreshold = 256;

        // UI elements
        private PCPFilterBar m_FilterBar;
        private PCPResultListView m_ResultList;
        private VisualElement m_DetailPanel;
        private Label m_DetailTitle;
        private Label m_DetailInfo;
        private VisualElement m_KeywordsList;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPShadersView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
            : base(
                scanResult,
                createContext,
                "Shader Analyzer",
                "\u2726",
                5)
        {
        }

        protected override PCPModuleId GetModuleId() => PCPModuleId.Shaders;

        // --------------------------------------------------------------------
        // IPCPExportableView
        // --------------------------------------------------------------------

        public string ModuleExportKey => "shaders";

        // --------------------------------------------------------------------
        // IPCPDeletableView / IPCPIgnorableView
        // --------------------------------------------------------------------

        public IReadOnlyList<string> GetSelectedPaths()
        {
            var selectedRows = m_ResultList.GetSelectedRowData();
            var paths = new List<string>();
            foreach (var row in selectedRows)
            {
                if (!string.IsNullOrEmpty(row.path))
                    paths.Add(row.path);
            }
            return paths;
        }

        public void ClearSelection() => m_ResultList.ClearSelection();

        // --------------------------------------------------------------------
        // BuildContent / RefreshContent
        // --------------------------------------------------------------------

        protected override void BuildContent(VisualElement content)
        {
            m_ResultList = CreateResultList();
            m_FilterBar = CreateFilterBar(m_ResultList);
            content.Add(m_FilterBar);
            content.Add(m_ResultList);

            // Shaders: wider Type for pipeline/variant/keyword/material info
            m_ResultList.SetColumnWidths(name: 180, path: 200, type: 200, size: 60, status: 100);
            m_FilterBar.ShowTypeChips = false;
            m_FilterBar.SetStatusChoices("All Statuses", "MISMATCH", "UNUSED", "HIGH VARIANTS", "OK");

            // Detail panel below the result list
            BuildDetailPanel(content);

            // Listen for selection changes to update detail panel
            m_ResultList.onSelectionChanged += OnSelectionChanged;
        }

        protected override void RefreshContent()
        {
            if (m_ScanResult == null || m_ScanResult.shaderEntries == null)
            {
                m_ResultList.SetData(new ArrayList(), _ => default);
                UpdateHeader(0);
                return;
            }

            var items = m_ScanResult.shaderEntries;
            m_ResultList.SetData(items as IList, ConvertRow);
            UpdateHeader(items.Count);

            // Hide detail panel on refresh
            if (m_DetailPanel != null)
                m_DetailPanel.style.display = DisplayStyle.None;
        }

        // --------------------------------------------------------------------
        // Detail panel
        // --------------------------------------------------------------------

        private void BuildDetailPanel(VisualElement content)
        {
            m_DetailPanel = new VisualElement();
            m_DetailPanel.style.minHeight = 120;
            m_DetailPanel.style.maxHeight = 200;
            m_DetailPanel.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            m_DetailPanel.style.borderTopWidth = 1;
            m_DetailPanel.style.borderTopColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            m_DetailPanel.style.paddingLeft = 12;
            m_DetailPanel.style.paddingRight = 12;
            m_DetailPanel.style.paddingTop = 8;
            m_DetailPanel.style.paddingBottom = 8;
            m_DetailPanel.style.flexShrink = 0;
            m_DetailPanel.style.display = DisplayStyle.None;

            m_DetailTitle = new Label("Shader Details");
            m_DetailTitle.style.fontSize = 13;
            m_DetailTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_DetailTitle.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
            m_DetailTitle.style.marginBottom = 4;
            m_DetailPanel.Add(m_DetailTitle);

            m_DetailInfo = new Label();
            m_DetailInfo.style.fontSize = 11;
            m_DetailInfo.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            m_DetailInfo.style.marginBottom = 4;
            m_DetailPanel.Add(m_DetailInfo);

            var keywordsLabel = new Label("Keywords:");
            keywordsLabel.style.fontSize = 11;
            keywordsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            keywordsLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            keywordsLabel.style.marginBottom = 2;
            m_DetailPanel.Add(keywordsLabel);

            var keywordsScroll = new ScrollView(ScrollViewMode.Vertical);
            keywordsScroll.style.flexGrow = 1;
            m_DetailPanel.Add(keywordsScroll);

            m_KeywordsList = keywordsScroll.contentContainer;

            content.Add(m_DetailPanel);
        }

        private void OnSelectionChanged(IReadOnlyList<int> selectedIndices)
        {
            if (selectedIndices == null || selectedIndices.Count == 0)
            {
                m_DetailPanel.style.display = DisplayStyle.None;
                return;
            }

            int rawIndex = selectedIndices[0];
            var items = m_ScanResult?.shaderEntries;
            if (items == null || rawIndex < 0 || rawIndex >= items.Count)
            {
                m_DetailPanel.style.display = DisplayStyle.None;
                return;
            }

            ShowShaderDetails(items[rawIndex]);
        }

        private void ShowShaderDetails(PCPShaderEntry shader)
        {
            m_DetailPanel.style.display = DisplayStyle.Flex;

            m_DetailTitle.text = shader.shaderName ?? "Unknown Shader";

            m_DetailInfo.text = $"Pipeline: {shader.targetPipeline}" +
                $"{(shader.pipelineMismatch ? " [MISMATCH]" : "")}" +
                $"  |  Variants: {shader.estimatedVariants}" +
                $"  |  Passes: {shader.passCount}" +
                $"  |  Keywords: {shader.keywordCount}" +
                $"  |  Materials: {shader.materialCount}" +
                $"{(shader.isUnused ? "  |  UNUSED" : "")}";

            m_KeywordsList.Clear();

            if (shader.keywords != null && shader.keywords.Count > 0)
            {
                var keywordsContainer = new VisualElement();
                keywordsContainer.style.flexDirection = FlexDirection.Row;
                keywordsContainer.style.flexWrap = Wrap.Wrap;

                foreach (string keyword in shader.keywords)
                {
                    var keywordBadge = new PCPBadge(keyword,
                        new Color(0.25f, 0.25f, 0.35f, 1f));
                    keywordBadge.style.marginRight = 4;
                    keywordBadge.style.marginBottom = 2;
                    keywordsContainer.Add(keywordBadge);
                }

                m_KeywordsList.Add(keywordsContainer);
            }
            else
            {
                var noKeywords = new Label("No keywords declared.");
                noKeywords.style.fontSize = 11;
                noKeywords.style.color = new Color(0.4f, 0.4f, 0.4f, 1f);
                noKeywords.style.unityFontStyleAndWeight = FontStyle.Italic;
                m_KeywordsList.Add(noKeywords);
            }
        }

        // --------------------------------------------------------------------
        // Row conversion
        // --------------------------------------------------------------------

        private PCPRowData ConvertRow(object item)
        {
            var shader = item as PCPShaderEntry;
            if (shader == null)
                return default;

            string status;
            Color statusColor;

            if (shader.pipelineMismatch)
            {
                status = "MISMATCH";
                statusColor = k_MismatchColor;
            }
            else if (shader.isUnused)
            {
                status = "UNUSED";
                statusColor = k_UnusedColor;
            }
            else if (shader.estimatedVariants > HighVariantThreshold)
            {
                status = "HIGH VARIANTS";
                statusColor = k_HighVariantColor;
            }
            else
            {
                status = "OK";
                statusColor = k_OkColor;
            }

            string typeInfo = $"{shader.targetPipeline} | " +
                $"V:{shader.estimatedVariants} K:{shader.keywordCount} M:{shader.materialCount}";

            return new PCPRowData
            {
                selected = false,
                icon = null,
                name = shader.shaderName ?? string.Empty,
                path = shader.assetPath ?? string.Empty,
                type = typeInfo,
                sizeBytes = shader.sizeBytes,
                status = status,
                statusColor = statusColor,
                guid = string.Empty
            };
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void UpdateHeader(int count)
        {
            m_Header.FindingCount = count;
            long total = 0;
            if (m_ScanResult?.shaderEntries != null)
            {
                foreach (var entry in m_ScanResult.shaderEntries)
                    total += entry.sizeBytes;
            }
            m_Header.TotalSize = total;
        }
    }
}
