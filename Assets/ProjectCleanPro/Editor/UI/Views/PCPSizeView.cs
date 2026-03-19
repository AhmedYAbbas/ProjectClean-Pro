using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for the size profiler. Uses a custom dual-panel layout with a
    /// <see cref="PCPTreemapView"/> in the top half and a <see cref="PCPResultListView"/>
    /// in the bottom half. Includes type filter tabs, a summary bar with percentage
    /// breakdowns, and optimization suggestion badges.
    /// </summary>
    public sealed class PCPSizeView : VisualElement
    {
        // Type categories and their colors
        private static readonly (string label, Color color, string[] extensions)[] k_TypeCategories = new[]
        {
            ("All",        new Color(0.337f, 0.612f, 0.839f, 1f), Array.Empty<string>()),
            ("Textures",   new Color(0.400f, 0.600f, 0.800f, 1f),
                new[] { "Texture2D", "Sprite", "Cubemap", "RenderTexture" }),
            ("Meshes",     new Color(0.800f, 0.600f, 0.400f, 1f),
                new[] { "Mesh", "GameObject" }),
            ("Audio",      new Color(0.800f, 0.500f, 0.700f, 1f),
                new[] { "AudioClip" }),
            ("Animations", new Color(0.600f, 0.800f, 0.400f, 1f),
                new[] { "AnimationClip", "AnimatorController", "AnimatorOverrideController" }),
            ("Other",      new Color(0.502f, 0.502f, 0.502f, 1f), Array.Empty<string>()),
        };

        // Colors for optimization badges
        private static readonly Color k_SuggestionColor = new Color(0.800f, 0.655f, 0.000f, 1f);
        private static readonly Color k_OkColor = new Color(0.416f, 0.600f, 0.333f, 1f);

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private readonly PCPScanResult m_ScanResult;
        private readonly Func<PCPScanContext> m_CreateContext;
        private readonly PCPModuleHeader m_Header;
        private readonly PCPTreemapView m_TreemapView;
        private readonly PCPResultListView m_ResultList;
        private readonly VisualElement m_SummaryBar;
        private readonly VisualElement m_BreakdownContainer;
        private readonly Label m_TotalSizeLabel;
        private readonly Dictionary<string, Button> m_FilterButtons = new Dictionary<string, Button>();
        private readonly HashSet<string> m_ActiveFilters = new HashSet<string>();

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPSizeView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
        {
            m_ScanResult = scanResult;
            m_CreateContext = createContext;

            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // Module header
            m_Header = new PCPModuleHeader(
                "Size Profiler",
                "\u25A3",
                new Color(0.969f, 0.863f, 0.435f, 1f));
            m_Header.onScan += OnScanClicked;
            m_Header.style.flexShrink = 0;
            Add(m_Header);

            // Summary bar with total size and type percentage bars
            m_SummaryBar = new VisualElement();
            m_SummaryBar.style.paddingLeft = 12;
            m_SummaryBar.style.paddingRight = 12;
            m_SummaryBar.style.paddingTop = 8;
            m_SummaryBar.style.paddingBottom = 8;
            m_SummaryBar.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            m_SummaryBar.style.borderBottomWidth = 1;
            m_SummaryBar.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            m_SummaryBar.style.flexShrink = 0;

            var summaryTopRow = new VisualElement();
            summaryTopRow.style.flexDirection = FlexDirection.Row;
            summaryTopRow.style.alignItems = Align.Center;
            summaryTopRow.style.marginBottom = 6;

            m_TotalSizeLabel = new Label("Total project size: --");
            m_TotalSizeLabel.style.fontSize = 13;
            m_TotalSizeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_TotalSizeLabel.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
            m_TotalSizeLabel.style.flexGrow = 1;
            summaryTopRow.Add(m_TotalSizeLabel);

            m_SummaryBar.Add(summaryTopRow);

            // Percentage breakdown bars
            m_BreakdownContainer = new VisualElement();
            m_BreakdownContainer.style.flexDirection = FlexDirection.Row;
            m_BreakdownContainer.style.height = 20;
            m_BreakdownContainer.style.borderTopLeftRadius = 3;
            m_BreakdownContainer.style.borderTopRightRadius = 3;
            m_BreakdownContainer.style.borderBottomLeftRadius = 3;
            m_BreakdownContainer.style.borderBottomRightRadius = 3;
            m_BreakdownContainer.style.overflow = Overflow.Hidden;
            m_SummaryBar.Add(m_BreakdownContainer);

            Add(m_SummaryBar);

            // Type filter tabs
            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.alignItems = Align.Center;
            filterRow.style.paddingLeft = 8;
            filterRow.style.paddingRight = 8;
            filterRow.style.paddingTop = 4;
            filterRow.style.paddingBottom = 4;
            filterRow.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            filterRow.style.borderBottomWidth = 1;
            filterRow.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            filterRow.style.flexShrink = 0;

            for (int i = 0; i < k_TypeCategories.Length; i++)
            {
                var cat = k_TypeCategories[i];
                var btn = CreateFilterTab(cat.label, cat.color);
                m_FilterButtons[cat.label] = btn;
                filterRow.Add(btn);
            }

            Add(filterRow);

            // Two-panel splitter: treemap (top) + list (bottom)
            var splitContainer = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Vertical);
            splitContainer.style.flexGrow = 1;

            // Top half: Treemap
            m_TreemapView = new PCPTreemapView();
            m_TreemapView.style.minHeight = 100;
            splitContainer.Add(m_TreemapView);

            // Bottom half: Result list
            m_ResultList = new PCPResultListView();
            m_ResultList.style.minHeight = 100;
            splitContainer.Add(m_ResultList);

            Add(splitContainer);

            // Set initial active filter
            SetActiveFilter("All");

            // Initial population
            RefreshData();
        }

        // --------------------------------------------------------------------
        // Filter tabs
        // --------------------------------------------------------------------

        private Button CreateFilterTab(string label, Color color)
        {
            var btn = new Button(() => SetActiveFilter(label))
            {
                text = label
            };
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            btn.style.paddingTop = 3;
            btn.style.paddingBottom = 3;
            btn.style.marginRight = 4;
            btn.style.borderTopLeftRadius = 12;
            btn.style.borderTopRightRadius = 12;
            btn.style.borderBottomLeftRadius = 12;
            btn.style.borderBottomRightRadius = 12;
            btn.style.fontSize = 11;

            // Color indicator dot
            btn.style.borderLeftWidth = 3;
            btn.style.borderLeftColor = color;

            return btn;
        }

        private void SetActiveFilter(string filter)
        {
            if (filter == "All")
            {
                m_ActiveFilters.Clear();
            }
            else
            {
                if (m_ActiveFilters.Contains(filter))
                    m_ActiveFilters.Remove(filter);
                else
                    m_ActiveFilters.Add(filter);
            }

            foreach (var kvp in m_FilterButtons)
            {
                bool isActive = kvp.Key == "All"
                    ? m_ActiveFilters.Count == 0
                    : m_ActiveFilters.Contains(kvp.Key);

                if (isActive)
                {
                    kvp.Value.style.backgroundColor = new Color(0.337f, 0.612f, 0.839f, 0.3f);
                    kvp.Value.style.borderTopWidth = 1;
                    kvp.Value.style.borderBottomWidth = 1;
                    kvp.Value.style.borderRightWidth = 1;
                    kvp.Value.style.borderTopColor = new Color(0.337f, 0.612f, 0.839f, 0.6f);
                    kvp.Value.style.borderBottomColor = new Color(0.337f, 0.612f, 0.839f, 0.6f);
                    kvp.Value.style.borderRightColor = new Color(0.337f, 0.612f, 0.839f, 0.6f);
                }
                else
                {
                    kvp.Value.style.backgroundColor = Color.clear;
                    kvp.Value.style.borderTopWidth = 0;
                    kvp.Value.style.borderBottomWidth = 0;
                    kvp.Value.style.borderRightWidth = 0;
                }
            }

            RefreshData();
        }

        // --------------------------------------------------------------------
        // Data population
        // --------------------------------------------------------------------

        /// <summary>
        /// Rebuilds both the treemap and the list from current scan data.
        /// </summary>
        public void RefreshData()
        {
            var filteredEntries = GetFilteredEntries();

            // Update header
            long totalSize = 0;
            foreach (var e in filteredEntries)
                totalSize += e.sizeBytes;

            m_Header.FindingCount = filteredEntries.Count;
            m_Header.TotalSize = totalSize;

            // Update summary bar
            UpdateSummaryBar();

            // Sort by size descending (default)
            filteredEntries.Sort((a, b) => b.sizeBytes.CompareTo(a.sizeBytes));

            // Populate result list
            m_ResultList.SetData(filteredEntries as IList, ConvertRow);

            // Populate treemap
            UpdateTreemap(filteredEntries);
        }

        private List<PCPSizeEntry> GetFilteredEntries()
        {
            if (m_ScanResult?.sizeEntries == null)
                return new List<PCPSizeEntry>();

            if (m_ActiveFilters.Count == 0)
                return new List<PCPSizeEntry>(m_ScanResult.sizeEntries);

            bool includeOther = m_ActiveFilters.Contains("Other");

            // Collect all type name extensions for active (non-Other) filters
            var matchTypeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string activeFilter in m_ActiveFilters)
            {
                if (activeFilter == "Other") continue;
                foreach (var cat in k_TypeCategories)
                {
                    if (cat.label == activeFilter)
                    {
                        foreach (string t in cat.extensions)
                            matchTypeSet.Add(t);
                        break;
                    }
                }
            }

            // Build known-types set for "Other" exclusion check
            var knownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (includeOther)
            {
                foreach (var cat in k_TypeCategories)
                {
                    if (cat.label != "All" && cat.label != "Other")
                    {
                        foreach (string t in cat.extensions)
                            knownTypes.Add(t);
                    }
                }
            }

            var result = new List<PCPSizeEntry>();
            foreach (var entry in m_ScanResult.sizeEntries)
            {
                string typeName = entry.assetTypeName ?? string.Empty;
                bool matches = matchTypeSet.Contains(typeName) ||
                               (includeOther && !knownTypes.Contains(typeName));
                if (matches)
                    result.Add(entry);
            }

            return result;
        }

        // --------------------------------------------------------------------
        // Summary bar
        // --------------------------------------------------------------------

        private void UpdateSummaryBar()
        {
            if (m_ScanResult?.sizeEntries == null || m_ScanResult.sizeEntries.Count == 0)
            {
                m_TotalSizeLabel.text = "Total project size: --";
                m_BreakdownContainer.Clear();
                return;
            }

            long totalSize = 0;
            foreach (var e in m_ScanResult.sizeEntries)
                totalSize += e.sizeBytes;

            m_TotalSizeLabel.text = $"Total project size: {PCPAssetInfo.FormatBytes(totalSize)}";

            // Calculate per-category sizes
            m_BreakdownContainer.Clear();

            for (int i = 1; i < k_TypeCategories.Length; i++) // Skip "All"
            {
                var cat = k_TypeCategories[i];
                long catSize = 0;
                bool isOther = cat.label == "Other";

                var knownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (isOther)
                {
                    for (int j = 1; j < k_TypeCategories.Length - 1; j++)
                    {
                        foreach (string t in k_TypeCategories[j].extensions)
                            knownTypes.Add(t);
                    }
                }

                foreach (var entry in m_ScanResult.sizeEntries)
                {
                    string typeName = entry.assetTypeName ?? string.Empty;

                    if (isOther)
                    {
                        if (!knownTypes.Contains(typeName))
                            catSize += entry.sizeBytes;
                    }
                    else
                    {
                        foreach (string t in cat.extensions)
                        {
                            if (string.Equals(typeName, t, StringComparison.OrdinalIgnoreCase))
                            {
                                catSize += entry.sizeBytes;
                                break;
                            }
                        }
                    }
                }

                if (catSize <= 0 || totalSize <= 0)
                    continue;

                float percentage = (float)catSize / totalSize * 100f;

                var segment = new VisualElement();
                segment.style.width = new StyleLength(Length.Percent(percentage));
                segment.style.height = new StyleLength(Length.Percent(100));
                segment.style.backgroundColor = cat.color;
                segment.tooltip = $"{cat.label}: {PCPAssetInfo.FormatBytes(catSize)} ({percentage:F1}%)";
                m_BreakdownContainer.Add(segment);
            }
        }

        // --------------------------------------------------------------------
        // Treemap
        // --------------------------------------------------------------------

        private void UpdateTreemap(List<PCPSizeEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                m_TreemapView.SetData(null);
                return;
            }

            // Build treemap hierarchy by folder
            var root = new PCPTreemapNode
            {
                name = "Project",
                size = 0,
                color = new Color(0.3f, 0.3f, 0.3f, 1f),
                path = "Assets"
            };

            // Group entries by folder
            var folderMap = new Dictionary<string, PCPTreemapNode>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                string folder = entry.folderPath ?? "Assets";
                if (string.IsNullOrEmpty(folder))
                    folder = "Assets";

                if (!folderMap.TryGetValue(folder, out var folderNode))
                {
                    string folderName = System.IO.Path.GetFileName(folder);
                    if (string.IsNullOrEmpty(folderName))
                        folderName = folder;

                    folderNode = new PCPTreemapNode
                    {
                        name = folderName,
                        size = 0,
                        color = GetColorForType(entry.assetTypeName),
                        path = folder
                    };
                    folderMap[folder] = folderNode;
                    root.children.Add(folderNode);
                }

                // Add leaf node for each entry
                var leaf = new PCPTreemapNode
                {
                    name = entry.name ?? "Unknown",
                    size = entry.sizeBytes,
                    color = GetColorForType(entry.assetTypeName),
                    path = entry.path
                };

                folderNode.children.Add(leaf);
                folderNode.size += entry.sizeBytes;
                root.size += entry.sizeBytes;
            }

            // Sort folder children by size
            root.children.Sort((a, b) => b.size.CompareTo(a.size));

            m_TreemapView.SetData(root);
        }

        // --------------------------------------------------------------------
        // Row conversion
        // --------------------------------------------------------------------

        private PCPRowData ConvertRow(object item)
        {
            var sizeEntry = item as PCPSizeEntry;
            if (sizeEntry == null)
                return default;

            // Status badge: optimization suggestion or OK
            string status;
            Color statusColor;

            if (sizeEntry.hasOptimizationSuggestion)
            {
                status = "OPTIMIZE";
                statusColor = k_SuggestionColor;
            }
            else
            {
                status = sizeEntry.FormattedSize;
                statusColor = k_OkColor;
            }

            // Load icon
            Texture2D icon = null;
            if (!string.IsNullOrEmpty(sizeEntry.path))
            {
                icon = AssetDatabase.GetCachedIcon(sizeEntry.path) as Texture2D;
            }

            // Type column shows asset type + compression info
            string typeDisplay = sizeEntry.assetTypeName ?? "Unknown";
            if (!string.IsNullOrEmpty(sizeEntry.compressionInfo))
            {
                typeDisplay += $" ({sizeEntry.compressionInfo})";
            }

            return new PCPRowData
            {
                selected = false,
                icon = icon,
                name = sizeEntry.name ?? string.Empty,
                path = sizeEntry.path ?? string.Empty,
                type = typeDisplay,
                sizeBytes = sizeEntry.sizeBytes,
                status = status,
                statusColor = statusColor,
                guid = string.Empty
            };
        }

        // --------------------------------------------------------------------
        // Actions
        // --------------------------------------------------------------------

        private void OnScanClicked()
        {
            m_Header.IsScanning = true;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    Debug.Log("[ProjectCleanPro] Size profiler scan requested. Use 'Scan All' from the dashboard.");
                }
                finally
                {
                    m_Header.IsScanning = false;
                    RefreshData();
                }
            };
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static Color GetColorForType(string assetTypeName)
        {
            if (string.IsNullOrEmpty(assetTypeName))
                return k_TypeCategories[k_TypeCategories.Length - 1].color;

            for (int i = 1; i < k_TypeCategories.Length - 1; i++)
            {
                var cat = k_TypeCategories[i];
                foreach (string t in cat.extensions)
                {
                    if (string.Equals(assetTypeName, t, StringComparison.OrdinalIgnoreCase))
                        return cat.color;
                }
            }

            return k_TypeCategories[k_TypeCategories.Length - 1].color;
        }
    }
}
