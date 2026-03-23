using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for displaying package audit results as a card layout. Each package
    /// is shown as a card with its name, version, status badge, reference counts,
    /// and dependent packages. Card border color indicates status.
    /// </summary>
    public sealed class PCPPackagesView : PCPModuleView, IPCPExportableView
    {
        // Status colors
        private static readonly Color k_UsedColor = new Color(0.416f, 0.600f, 0.333f, 1f);
        private static readonly Color k_UnusedColor = new Color(0.957f, 0.278f, 0.278f, 1f);
        private static readonly Color k_TransitiveColor = new Color(0.800f, 0.655f, 0.000f, 1f);
        private static readonly Color k_UnknownColor = new Color(0.502f, 0.502f, 0.502f, 1f);

        // Filter enum
        private enum PackageFilter { All, Used, Unused, Transitive }

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private VisualElement m_CardContainer;
        private Label m_EmptyLabel;
        private readonly Dictionary<PackageFilter, Button> m_FilterButtons =
            new Dictionary<PackageFilter, Button>();
        private readonly HashSet<PackageFilter> m_ActiveFilters = new HashSet<PackageFilter>();
        private readonly HashSet<string> m_SelectedPackages = new HashSet<string>();
        private VisualElement m_SelectionBar;
        private Label m_SelectionCountLabel;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPPackagesView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
            : base(
                scanResult,
                createContext,
                "Package Audit",
                "\u2750",
                4)
        {
        }

        protected override PCPModuleId GetModuleId() => PCPModuleId.Packages;

        // --------------------------------------------------------------------
        // IPCPExportableView
        // --------------------------------------------------------------------

        public string ModuleExportKey => "packages";

        // --------------------------------------------------------------------
        // BuildContent / RefreshContent
        // --------------------------------------------------------------------

        protected override void BuildContent(VisualElement content)
        {
            // Filter bar
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

            foreach (PackageFilter filter in Enum.GetValues(typeof(PackageFilter)))
            {
                var btn = CreateFilterButton(filter);
                m_FilterButtons[filter] = btn;
                filterBar.Add(btn);
            }

            content.Add(filterBar);

            // Selection action bar (hidden by default)
            m_SelectionBar = BuildSelectionBar();
            content.Add(m_SelectionBar);

            // Scrollable card container
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            content.Add(scrollView);

            m_CardContainer = scrollView.contentContainer;
            m_CardContainer.style.paddingTop = 12;
            m_CardContainer.style.paddingBottom = 12;
            m_CardContainer.style.paddingLeft = 16;
            m_CardContainer.style.paddingRight = 16;

            // Empty state
            m_EmptyLabel = new Label("No package audit data.\nRun a scan to audit installed packages.");
            m_EmptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_EmptyLabel.style.fontSize = 13;
            m_EmptyLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            m_EmptyLabel.style.paddingTop = 48;
            m_EmptyLabel.style.paddingBottom = 48;
            m_EmptyLabel.style.display = DisplayStyle.None;
            m_CardContainer.Add(m_EmptyLabel);

            // Set initial active filter
            SetActiveFilter(PackageFilter.All);
        }

        protected override void RefreshContent()
        {
            RefreshCards();
        }

        // --------------------------------------------------------------------
        // Filter buttons
        // --------------------------------------------------------------------

        private Button CreateFilterButton(PackageFilter filter)
        {
            var btn = new Button(() => SetActiveFilter(filter))
            {
                text = filter.ToString()
            };
            btn.style.height = 22;
            btn.style.paddingLeft = 6;
            btn.style.paddingRight = 6;
            btn.style.paddingTop = 2;
            btn.style.paddingBottom = 2;
            btn.style.marginRight = 2;
            btn.style.marginBottom = 2;
            btn.style.borderTopLeftRadius = 10;
            btn.style.borderTopRightRadius = 10;
            btn.style.borderBottomLeftRadius = 10;
            btn.style.borderBottomRightRadius = 10;
            btn.style.fontSize = 11;
            return btn;
        }

        private void SetActiveFilter(PackageFilter filter)
        {
            if (filter == PackageFilter.All)
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
                bool isActive = kvp.Key == PackageFilter.All
                    ? m_ActiveFilters.Count == 0
                    : m_ActiveFilters.Contains(kvp.Key);

                ApplyChipStyle(kvp.Value, isActive);
            }

            RefreshCards();
        }

        private static void ApplyChipStyle(Button chip, bool active)
        {
            if (active)
            {
                chip.style.backgroundColor = new Color(0.337f, 0.612f, 0.839f, 1f);
                chip.style.color = Color.white;
                chip.style.borderTopColor = new Color(0.337f, 0.612f, 0.839f, 1f);
                chip.style.borderBottomColor = new Color(0.337f, 0.612f, 0.839f, 1f);
                chip.style.borderLeftColor = new Color(0.337f, 0.612f, 0.839f, 1f);
                chip.style.borderRightColor = new Color(0.337f, 0.612f, 0.839f, 1f);
            }
            else
            {
                chip.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f);
                chip.style.color = new Color(0.66f, 0.66f, 0.66f, 1f);
                chip.style.borderTopColor = new Color(0.33f, 0.33f, 0.33f, 1f);
                chip.style.borderBottomColor = new Color(0.33f, 0.33f, 0.33f, 1f);
                chip.style.borderLeftColor = new Color(0.33f, 0.33f, 0.33f, 1f);
                chip.style.borderRightColor = new Color(0.33f, 0.33f, 0.33f, 1f);
            }
            chip.style.borderTopWidth = 1;
            chip.style.borderBottomWidth = 1;
            chip.style.borderLeftWidth = 1;
            chip.style.borderRightWidth = 1;
        }

        // --------------------------------------------------------------------
        // Data population
        // --------------------------------------------------------------------

        private void RefreshCards()
        {
            m_CardContainer.Clear();

            var entries = GetFilteredEntries();

            if (entries.Count == 0)
            {
                m_CardContainer.Add(m_EmptyLabel);
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                m_Header.FindingCount = 0;
                UpdateSelectionBar();
                return;
            }

            m_EmptyLabel.style.display = DisplayStyle.None;
            m_Header.FindingCount = entries.Count;

            foreach (var entry in entries)
            {
                var card = BuildPackageCard(entry);
                m_CardContainer.Add(card);
            }

            UpdateSelectionBar();
        }

        private List<PCPPackageAuditEntry> GetFilteredEntries()
        {
            var result = new List<PCPPackageAuditEntry>();
            if (m_ScanResult?.packageAuditEntries == null)
                return result;

            foreach (var entry in m_ScanResult.packageAuditEntries)
            {
                if (m_ActiveFilters.Count == 0)
                {
                    result.Add(entry);
                    continue;
                }

                bool matches = false;
                foreach (var filter in m_ActiveFilters)
                {
                    switch (filter)
                    {
                        case PackageFilter.Used:
                            if (entry.status == PCPPackageStatus.Used) matches = true;
                            break;
                        case PackageFilter.Unused:
                            if (entry.status == PCPPackageStatus.Unused) matches = true;
                            break;
                        case PackageFilter.Transitive:
                            if (entry.status == PCPPackageStatus.TransitiveOnly) matches = true;
                            break;
                    }
                    if (matches) break;
                }

                if (matches)
                    result.Add(entry);
            }

            return result;
        }

        // --------------------------------------------------------------------
        // Card building
        // --------------------------------------------------------------------

        private VisualElement BuildPackageCard(PCPPackageAuditEntry entry)
        {
            Color borderColor = GetStatusColor(entry.status);

            var card = new VisualElement();
            card.AddToClassList("pcp-card");
            card.style.marginBottom = 8;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.paddingLeft = 16;
            card.style.paddingRight = 16;
            card.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = borderColor;

            // Top row: checkbox + name + version + status badge
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 6;

            // Selection checkbox for unused packages
            if (entry.status == PCPPackageStatus.Unused && !string.IsNullOrEmpty(entry.packageName))
            {
                var checkbox = new Toggle();
                checkbox.value = m_SelectedPackages.Contains(entry.packageName);
                checkbox.style.marginRight = 6;
                string pkgName = entry.packageName;
                checkbox.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        m_SelectedPackages.Add(pkgName);
                    else
                        m_SelectedPackages.Remove(pkgName);
                    UpdateSelectionBar();
                });
                topRow.Add(checkbox);
            }

            var nameLabel = new Label(entry.displayName ?? entry.packageName ?? "Unknown");
            nameLabel.style.fontSize = 14;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
            nameLabel.style.marginRight = 8;
            topRow.Add(nameLabel);

            var versionLabel = new Label($"v{entry.version ?? "?"}");
            versionLabel.style.fontSize = 11;
            versionLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            versionLabel.style.marginRight = 8;
            topRow.Add(versionLabel);

            var statusBadge = new PCPBadge(entry.status.ToString(), borderColor);
            topRow.Add(statusBadge);

            var topSpacer = new VisualElement();
            topSpacer.style.flexGrow = 1;
            topRow.Add(topSpacer);

            // Remove button for unused packages
            if (entry.status == PCPPackageStatus.Unused)
            {
                var removeBtn = new Button(() => OnRemovePackage(entry))
                {
                    text = "Remove Package"
                };
                removeBtn.style.paddingLeft = 8;
                removeBtn.style.paddingRight = 8;
                removeBtn.style.paddingTop = 3;
                removeBtn.style.paddingBottom = 3;
                removeBtn.style.fontSize = 11;
                removeBtn.style.backgroundColor = k_UnusedColor;
                removeBtn.style.color = Color.white;
                removeBtn.style.borderTopLeftRadius = 3;
                removeBtn.style.borderTopRightRadius = 3;
                removeBtn.style.borderBottomLeftRadius = 3;
                removeBtn.style.borderBottomRightRadius = 3;
                topRow.Add(removeBtn);
            }

            card.Add(topRow);

            // Package ID
            var idLabel = new Label(entry.packageName ?? "");
            idLabel.style.fontSize = 11;
            idLabel.style.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            idLabel.style.marginBottom = 8;
            card.Add(idLabel);

            // Stats row
            var statsRow = new VisualElement();
            statsRow.style.flexDirection = FlexDirection.Row;
            statsRow.style.alignItems = Align.Center;
            statsRow.style.marginBottom = 4;

            var directRefLabel = new Label($"Direct refs: {entry.directReferenceCount}");
            directRefLabel.style.fontSize = 11;
            directRefLabel.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            directRefLabel.style.marginRight = 16;
            statsRow.Add(directRefLabel);

            var codeRefLabel = new Label($"Code refs: {entry.codeReferenceCount}");
            codeRefLabel.style.fontSize = 11;
            codeRefLabel.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            codeRefLabel.style.marginRight = 16;
            statsRow.Add(codeRefLabel);

            var totalRefLabel = new Label($"Total: {entry.TotalReferenceCount}");
            totalRefLabel.style.fontSize = 11;
            totalRefLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            totalRefLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            statsRow.Add(totalRefLabel);

            card.Add(statsRow);

            // Dependent packages
            if (entry.dependedOnBy != null && entry.dependedOnBy.Count > 0)
            {
                var depsRow = new VisualElement();
                depsRow.style.flexDirection = FlexDirection.Row;
                depsRow.style.flexWrap = Wrap.Wrap;
                depsRow.style.alignItems = Align.Center;
                depsRow.style.marginTop = 4;

                var depsLabel = new Label("Required by: ");
                depsLabel.style.fontSize = 11;
                depsLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                depsRow.Add(depsLabel);

                foreach (string dep in entry.dependedOnBy)
                {
                    var depBadge = new PCPBadge(dep, new Color(0.25f, 0.25f, 0.35f, 1f));
                    depBadge.style.marginRight = 4;
                    depBadge.style.marginBottom = 2;
                    depsRow.Add(depBadge);
                }

                card.Add(depsRow);
            }

            // Description
            if (!string.IsNullOrEmpty(entry.description))
            {
                var descLabel = new Label(entry.description);
                descLabel.style.fontSize = 11;
                descLabel.style.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                descLabel.style.marginTop = 6;
                descLabel.style.overflow = Overflow.Hidden;
                descLabel.style.textOverflow = TextOverflow.Ellipsis;
                descLabel.style.maxHeight = 32;
                descLabel.tooltip = entry.description;
                card.Add(descLabel);
            }

            return card;
        }

        // --------------------------------------------------------------------
        // Package removal
        // --------------------------------------------------------------------

        private void OnRemovePackage(PCPPackageAuditEntry entry)
        {
            if (string.IsNullOrEmpty(entry.packageName))
                return;

            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Package",
                $"Remove package '{entry.displayName ?? entry.packageName}'?\n\n" +
                "This will modify your project's manifest.json.",
                "Remove", "Cancel");

            if (!confirmed)
                return;

            try
            {
                UnityEditor.PackageManager.Client.Remove(entry.packageName);
                Debug.Log($"[ProjectCleanPro] Requested removal of package: {entry.packageName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectCleanPro] Failed to remove package: {ex.Message}");
            }
        }

        // --------------------------------------------------------------------
        // Selection bar
        // --------------------------------------------------------------------

        private VisualElement BuildSelectionBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 8;
            bar.style.paddingRight = 8;
            bar.style.paddingTop = 6;
            bar.style.paddingBottom = 6;
            bar.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            bar.style.flexShrink = 0;
            bar.style.display = DisplayStyle.None;

            m_SelectionCountLabel = new Label("0 selected");
            m_SelectionCountLabel.style.fontSize = 12;
            m_SelectionCountLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            m_SelectionCountLabel.style.marginRight = 12;
            bar.Add(m_SelectionCountLabel);

            var selectAllBtn = new Button(OnSelectAllUnused) { text = "Select All Unused" };
            StyleSelectionBarButton(selectAllBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            bar.Add(selectAllBtn);

            var deselectAllBtn = new Button(OnDeselectAll) { text = "Deselect All" };
            StyleSelectionBarButton(deselectAllBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            bar.Add(deselectAllBtn);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);

            var removeBtn = new Button(OnRemoveSelected) { text = "Remove Selected" };
            StyleSelectionBarButton(removeBtn, k_UnusedColor);
            removeBtn.style.color = Color.white;
            bar.Add(removeBtn);

            return bar;
        }

        private static void StyleSelectionBarButton(Button btn, Color bgColor)
        {
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            btn.style.paddingTop = 3;
            btn.style.paddingBottom = 3;
            btn.style.marginRight = 4;
            btn.style.fontSize = 11;
            btn.style.backgroundColor = bgColor;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
        }

        private void UpdateSelectionBar()
        {
            int count = m_SelectedPackages.Count;
            bool hasSelection = count > 0;
            m_SelectionBar.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            m_SelectionCountLabel.text = $"{count} selected";
        }

        private void OnSelectAllUnused()
        {
            if (m_ScanResult?.packageAuditEntries == null) return;

            var visible = GetFilteredEntries();
            foreach (var entry in visible)
            {
                if (entry.status == PCPPackageStatus.Unused && !string.IsNullOrEmpty(entry.packageName))
                    m_SelectedPackages.Add(entry.packageName);
            }

            RefreshCards();
        }

        private void OnDeselectAll()
        {
            m_SelectedPackages.Clear();
            RefreshCards();
        }

        private void OnRemoveSelected()
        {
            if (m_SelectedPackages.Count == 0) return;

            var names = new List<string>(m_SelectedPackages);
            names.Sort();

            string list = string.Join("\n", names);
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Selected Packages",
                $"Remove {names.Count} package(s)?\n\n{list}\n\n" +
                "This will modify your project's manifest.json.",
                "Remove All", "Cancel");

            if (!confirmed) return;

            foreach (string pkg in names)
            {
                try
                {
                    UnityEditor.PackageManager.Client.Remove(pkg);
                    Debug.Log($"[ProjectCleanPro] Requested removal of package: {pkg}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProjectCleanPro] Failed to remove package {pkg}: {ex.Message}");
                }
            }

            m_SelectedPackages.Clear();
            UpdateSelectionBar();
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static Color GetStatusColor(PCPPackageStatus status)
        {
            switch (status)
            {
                case PCPPackageStatus.Used: return k_UsedColor;
                case PCPPackageStatus.Unused: return k_UnusedColor;
                case PCPPackageStatus.TransitiveOnly: return k_TransitiveColor;
                default: return k_UnknownColor;
            }
        }
    }
}
