using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for displaying missing reference scan results grouped by source asset.
    /// Each asset appears as a collapsible foldout with a count badge and severity
    /// indicator, expandable to show individual broken properties.
    /// </summary>
    public sealed class PCPMissingRefsView : PCPModuleView, IPCPExportableView
    {
        // Badge colors
        private static readonly Color k_ErrorColor = new Color(0.957f, 0.278f, 0.278f, 1f);
        private static readonly Color k_WarningColor = new Color(0.800f, 0.655f, 0.000f, 1f);
        private static readonly Color k_InfoColor = new Color(0.337f, 0.612f, 0.839f, 1f);

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private VisualElement m_GroupContainer;
        private Label m_EmptyLabel;
        private TextField m_SearchField;
        private PopupField<string> m_SeverityDropdown;
        private PopupField<string> m_TypeDropdown;
        private PopupField<string> m_SortDropdown;

        private string m_SearchText = string.Empty;
        private string m_SeverityFilter = "All Severities";
        private string m_TypeFilter = "All Types";
        private string m_SortMode = "Severity";

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPMissingRefsView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
            : base(
                scanResult,
                createContext,
                "Missing References",
                "\u26A0",
                1)
        {
        }

        protected override PCPModuleId GetModuleId() => PCPModuleId.Missing;

        // --------------------------------------------------------------------
        // IPCPExportableView
        // --------------------------------------------------------------------

        public string ModuleExportKey => "missing";

        // --------------------------------------------------------------------
        // BuildContent / RefreshContent
        // --------------------------------------------------------------------

        protected override void BuildContent(VisualElement content)
        {
            // Custom filter bar with search + severity dropdown
            var filterBar = new VisualElement();
            filterBar.style.flexDirection = FlexDirection.Row;
            filterBar.style.alignItems = Align.Center;
            filterBar.style.minHeight = 32;
            filterBar.style.paddingLeft = 8;
            filterBar.style.paddingRight = 8;
            filterBar.style.paddingTop = 4;
            filterBar.style.paddingBottom = 4;
            filterBar.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            filterBar.style.borderBottomWidth = 1;
            filterBar.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            filterBar.style.flexShrink = 0;

            m_SearchField = new TextField();
            m_SearchField.style.flexGrow = 1;
            m_SearchField.style.height = 24;
            m_SearchField.style.marginRight = 8;
            m_SearchField.style.minWidth = 120;
            m_SearchField.value = string.Empty;
            m_SearchField.tooltip = "Search by asset name or path...";
            m_SearchField.RegisterValueChangedCallback(evt =>
            {
                m_SearchText = evt.newValue ?? string.Empty;
                RefreshGroups();
            });
            filterBar.Add(m_SearchField);

            var severityChoices = new List<string>
            {
                "All Severities", "ERROR", "WARNING", "INFO"
            };
            m_SeverityDropdown = new PopupField<string>(severityChoices, 0);
            m_SeverityDropdown.style.minWidth = 120;
            m_SeverityDropdown.style.height = 24;
            m_SeverityDropdown.style.marginRight = 8;
            m_SeverityDropdown.RegisterValueChangedCallback(evt =>
            {
                m_SeverityFilter = evt.newValue;
                RefreshGroups();
            });
            filterBar.Add(m_SeverityDropdown);

            var typeChoices = new List<string>
            {
                "All Types", "Prefab", "Scene", "ScriptableObject"
            };
            m_TypeDropdown = new PopupField<string>(typeChoices, 0);
            m_TypeDropdown.style.minWidth = 120;
            m_TypeDropdown.style.height = 24;
            m_TypeDropdown.style.marginRight = 8;
            m_TypeDropdown.RegisterValueChangedCallback(evt =>
            {
                m_TypeFilter = evt.newValue;
                RefreshGroups();
            });
            filterBar.Add(m_TypeDropdown);

            var sortLabel = new Label("Sort:");
            sortLabel.style.fontSize = 11;
            sortLabel.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            sortLabel.style.marginRight = 4;
            filterBar.Add(sortLabel);

            var sortChoices = new List<string>
            {
                "Severity", "Count", "Name"
            };
            m_SortDropdown = new PopupField<string>(sortChoices, 0);
            m_SortDropdown.style.minWidth = 100;
            m_SortDropdown.style.height = 24;
            m_SortDropdown.style.marginRight = 8;
            m_SortDropdown.RegisterValueChangedCallback(evt =>
            {
                m_SortMode = evt.newValue;
                RefreshGroups();
            });
            filterBar.Add(m_SortDropdown);

            var clearBtn = new Button(() =>
            {
                m_SearchField.value = string.Empty;
                m_SeverityDropdown.value = "All Severities";
                m_TypeDropdown.value = "All Types";
                m_SortDropdown.value = "Severity";
            })
            {
                text = "Clear"
            };
            clearBtn.style.paddingLeft = 8;
            clearBtn.style.paddingRight = 8;
            clearBtn.style.paddingTop = 3;
            clearBtn.style.paddingBottom = 3;
            filterBar.Add(clearBtn);

            content.Add(filterBar);

            // Scrollable group container
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            content.Add(scrollView);

            m_GroupContainer = scrollView.contentContainer;
            m_GroupContainer.style.paddingTop = 8;
            m_GroupContainer.style.paddingBottom = 8;
            m_GroupContainer.style.paddingLeft = 12;
            m_GroupContainer.style.paddingRight = 12;

            // Empty state
            m_EmptyLabel = new Label("No missing references found.\nRun a scan to detect broken references.");
            m_EmptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_EmptyLabel.style.fontSize = 13;
            m_EmptyLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            m_EmptyLabel.style.paddingTop = 48;
            m_EmptyLabel.style.paddingBottom = 48;
            m_EmptyLabel.style.display = DisplayStyle.None;
            m_GroupContainer.Add(m_EmptyLabel);
        }

        protected override void RefreshContent()
        {
            RefreshGroups();
        }

        // --------------------------------------------------------------------
        // Data population
        // --------------------------------------------------------------------

        private void RefreshGroups()
        {
            m_GroupContainer.Clear();

            if (m_ScanResult == null || m_ScanResult.missingReferences == null ||
                m_ScanResult.missingReferences.Count == 0)
            {
                m_GroupContainer.Add(m_EmptyLabel);
                m_EmptyLabel.text = "No missing references found.\nRun a scan to detect broken references.";
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                m_Header.FindingCount = 0;
                m_Header.TotalSize = 0;
                return;
            }

            m_EmptyLabel.style.display = DisplayStyle.None;

            // Group by source asset path
            var groups = new Dictionary<string, List<PCPMissingReference>>(StringComparer.Ordinal);
            foreach (var entry in m_ScanResult.missingReferences)
            {
                string key = entry.sourceAssetPath ?? string.Empty;
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<PCPMissingReference>();
                    groups[key] = list;
                }
                list.Add(entry);
            }

            // Filter and sort groups
            var filteredGroups = new List<KeyValuePair<string, List<PCPMissingReference>>>();
            int totalFilteredEntries = 0;

            foreach (var kvp in groups)
            {
                string assetPath = kvp.Key;
                var entries = kvp.Value;

                // Search filter
                if (!string.IsNullOrEmpty(m_SearchText))
                {
                    string searchLower = m_SearchText.ToLowerInvariant();
                    if (assetPath.ToLowerInvariant().IndexOf(searchLower, StringComparison.Ordinal) < 0)
                        continue;
                }

                // Type filter
                if (m_TypeFilter != "All Types")
                {
                    string ext = Path.GetExtension(assetPath).ToLowerInvariant();
                    bool matchesType = false;
                    switch (m_TypeFilter)
                    {
                        case "Prefab":           matchesType = ext == ".prefab"; break;
                        case "Scene":            matchesType = ext == ".unity";  break;
                        case "ScriptableObject": matchesType = ext == ".asset";  break;
                    }
                    if (!matchesType)
                        continue;
                }

                // Severity filter
                if (m_SeverityFilter != "All Severities")
                {
                    PCPSeverity requiredSeverity = ParseSeverity(m_SeverityFilter);
                    var filtered = new List<PCPMissingReference>();
                    foreach (var e in entries)
                    {
                        if (e.severity == requiredSeverity)
                            filtered.Add(e);
                    }
                    if (filtered.Count == 0)
                        continue;
                    entries = filtered;
                }

                filteredGroups.Add(new KeyValuePair<string, List<PCPMissingReference>>(assetPath, entries));
                totalFilteredEntries += entries.Count;
            }

            // Sort based on selected mode
            switch (m_SortMode)
            {
                case "Count":
                    filteredGroups.Sort((a, b) =>
                    {
                        int cmp = b.Value.Count.CompareTo(a.Value.Count);
                        if (cmp != 0) return cmp;
                        return GetWorstSeverityPriority(a.Value).CompareTo(GetWorstSeverityPriority(b.Value));
                    });
                    break;
                case "Name":
                    filteredGroups.Sort((a, b) =>
                        string.Compare(
                            Path.GetFileName(a.Key),
                            Path.GetFileName(b.Key),
                            StringComparison.OrdinalIgnoreCase));
                    break;
                default: // "Severity"
                    filteredGroups.Sort((a, b) =>
                    {
                        int sevA = GetWorstSeverityPriority(a.Value);
                        int sevB = GetWorstSeverityPriority(b.Value);
                        if (sevA != sevB)
                            return sevA.CompareTo(sevB);
                        return b.Value.Count.CompareTo(a.Value.Count);
                    });
                    break;
            }

            if (filteredGroups.Count == 0)
            {
                m_GroupContainer.Add(m_EmptyLabel);
                m_EmptyLabel.text = "No results match the current filters.";
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                m_Header.FindingCount = 0;
                m_Header.TotalSize = 0;
                return;
            }

            m_Header.FindingCount = totalFilteredEntries;
            m_Header.TotalSize = 0;

            for (int i = 0; i < filteredGroups.Count; i++)
            {
                var kvp = filteredGroups[i];
                var groupElement = BuildGroupElement(kvp.Key, kvp.Value, i);
                m_GroupContainer.Add(groupElement);
            }
        }

        // --------------------------------------------------------------------
        // Group element
        // --------------------------------------------------------------------

        private VisualElement BuildGroupElement(string assetPath, List<PCPMissingReference> entries, int groupIndex)
        {
            PCPSeverity worstSeverity = GetWorstSeverity(entries);
            Color borderColor = GetSeverityColor(worstSeverity);

            var container = new VisualElement();
            container.style.marginBottom = 8;
            container.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            container.style.borderTopLeftRadius = 4;
            container.style.borderTopRightRadius = 4;
            container.style.borderBottomLeftRadius = 4;
            container.style.borderBottomRightRadius = 4;
            container.style.borderLeftWidth = 3;
            container.style.borderLeftColor = borderColor;

            // Foldout title
            string assetName = string.IsNullOrEmpty(assetPath)
                ? "(unknown)"
                : Path.GetFileName(assetPath);

            string refWord = entries.Count == 1 ? "broken reference" : "broken references";
            string foldoutTitle = $"{assetName}  |  {entries.Count} {refWord}";

            var foldout = new Foldout { text = foldoutTitle };
            foldout.value = false;
            foldout.style.paddingLeft = 8;
            foldout.style.paddingRight = 8;
            foldout.style.paddingTop = 6;
            foldout.style.paddingBottom = 6;

            // Foldout header row: icon + path + severity badge
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 6;

            // Asset icon
            if (!string.IsNullOrEmpty(assetPath))
            {
                Texture2D icon = AssetDatabase.GetCachedIcon(assetPath) as Texture2D;
                if (icon != null)
                {
                    var iconImage = new Image { image = icon };
                    iconImage.style.width = 16;
                    iconImage.style.height = 16;
                    iconImage.style.marginRight = 6;
                    iconImage.scaleMode = ScaleMode.ScaleToFit;
                    headerRow.Add(iconImage);
                }
            }

            // Asset path label
            var pathLabel = new Label(assetPath);
            pathLabel.style.fontSize = 11;
            pathLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            pathLabel.style.flexGrow = 1;
            pathLabel.style.overflow = Overflow.Hidden;
            pathLabel.style.textOverflow = TextOverflow.Ellipsis;
            pathLabel.tooltip = assetPath;

            // Double-click to ping asset
            pathLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2 && !string.IsNullOrEmpty(assetPath))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (obj != null)
                        EditorGUIUtility.PingObject(obj);
                }
            });
            headerRow.Add(pathLabel);

            // Worst severity badge
            var severityBadge = PCPBadge.FromSeverity(worstSeverity);
            severityBadge.style.marginLeft = 8;
            headerRow.Add(severityBadge);

            foldout.Add(headerRow);

            // Entry rows
            for (int e = 0; e < entries.Count; e++)
            {
                var entry = entries[e];
                var entryRow = BuildEntryRow(entry, e < entries.Count - 1);
                foldout.Add(entryRow);
            }

            container.Add(foldout);
            return container;
        }

        private VisualElement BuildEntryRow(PCPMissingReference entry, bool showBottomBorder)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.paddingLeft = 4;

            if (showBottomBorder)
            {
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            }

            // Severity badge
            var badge = PCPBadge.FromSeverity(entry.severity);
            badge.style.marginRight = 8;
            badge.style.minWidth = 55;
            row.Add(badge);

            // Component type
            string compType = entry.componentType ?? "(unknown)";
            int lastDot = compType.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < compType.Length - 1)
                compType = compType.Substring(lastDot + 1);

            var compLabel = new Label(compType);
            compLabel.style.fontSize = 12;
            compLabel.style.minWidth = 120;
            compLabel.style.marginRight = 8;
            compLabel.style.overflow = Overflow.Hidden;
            compLabel.style.textOverflow = TextOverflow.Ellipsis;
            compLabel.tooltip = entry.componentType;
            row.Add(compLabel);

            // Property path
            string propPath = entry.propertyPath ?? string.Empty;
            string displayProp = propPath;
            if (displayProp.Length > 40)
                displayProp = "..." + displayProp.Substring(displayProp.Length - 37);

            var propLabel = new Label(displayProp);
            propLabel.style.fontSize = 11;
            propLabel.style.color = new Color(0.651f, 0.651f, 0.651f, 1f);
            propLabel.style.flexGrow = 1;
            propLabel.style.overflow = Overflow.Hidden;
            propLabel.style.textOverflow = TextOverflow.Ellipsis;
            propLabel.tooltip = propPath;
            row.Add(propLabel);

            // GameObject path (if present)
            if (!string.IsNullOrEmpty(entry.gameObjectPath))
            {
                var goLabel = new Label(entry.gameObjectPath);
                goLabel.style.fontSize = 11;
                goLabel.style.color = new Color(0.420f, 0.420f, 0.420f, 1f);
                goLabel.style.marginLeft = 8;
                goLabel.style.minWidth = 100;
                goLabel.style.overflow = Overflow.Hidden;
                goLabel.style.textOverflow = TextOverflow.Ellipsis;
                goLabel.tooltip = "GameObject: " + entry.gameObjectPath;
                row.Add(goLabel);
            }

            return row;
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static PCPSeverity ParseSeverity(string text)
        {
            switch (text)
            {
                case "ERROR": return PCPSeverity.Error;
                case "WARNING": return PCPSeverity.Warning;
                case "INFO": return PCPSeverity.Info;
                default: return PCPSeverity.Info;
            }
        }

        private static PCPSeverity GetWorstSeverity(List<PCPMissingReference> entries)
        {
            PCPSeverity worst = PCPSeverity.Info;
            foreach (var e in entries)
            {
                if (e.severity == PCPSeverity.Error)
                    return PCPSeverity.Error;
                if (e.severity == PCPSeverity.Warning)
                    worst = PCPSeverity.Warning;
            }
            return worst;
        }

        private static int GetWorstSeverityPriority(List<PCPMissingReference> entries)
        {
            int best = 3;
            foreach (var e in entries)
            {
                int p = SeverityPriority(e.severity);
                if (p < best)
                    best = p;
            }
            return best;
        }

        private static int SeverityPriority(PCPSeverity severity)
        {
            switch (severity)
            {
                case PCPSeverity.Error: return 0;
                case PCPSeverity.Warning: return 1;
                case PCPSeverity.Info: return 2;
                default: return 3;
            }
        }

        private static Color GetSeverityColor(PCPSeverity severity)
        {
            switch (severity)
            {
                case PCPSeverity.Error: return k_ErrorColor;
                case PCPSeverity.Warning: return k_WarningColor;
                case PCPSeverity.Info: return k_InfoColor;
                default: return k_InfoColor;
            }
        }

    }
}
