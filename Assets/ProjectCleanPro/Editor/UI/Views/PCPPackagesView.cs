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
    /// and dependent packages. Card border color indicates status:
    /// green = used, red = unused, yellow = transitive only.
    /// </summary>
    public sealed class PCPPackagesView : VisualElement, IPCPRefreshable
    {
        // Status colors
        private static readonly Color k_UsedColor = new Color(0.416f, 0.600f, 0.333f, 1f);
        private static readonly Color k_UnusedColor = new Color(0.957f, 0.278f, 0.278f, 1f);
        private static readonly Color k_TransitiveColor = new Color(0.800f, 0.655f, 0.000f, 1f);
        private static readonly Color k_UnknownColor = new Color(0.502f, 0.502f, 0.502f, 1f);
        private Color k_AccentColor => PCPContext.Settings.GetModuleColor(4);

        // Filter enum
        private enum PackageFilter
        {
            All,
            Used,
            Unused,
            Transitive
        }

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private readonly PCPScanResult m_ScanResult;
        private readonly Func<PCPScanContext> m_CreateContext;
        private readonly PCPModuleHeader m_Header;
        private readonly VisualElement m_CardContainer;
        private readonly Label m_EmptyLabel;
        private readonly Dictionary<PackageFilter, Button> m_FilterButtons =
            new Dictionary<PackageFilter, Button>();
        private readonly HashSet<PackageFilter> m_ActiveFilters = new HashSet<PackageFilter>();

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPPackagesView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
        {
            m_ScanResult = scanResult;
            m_CreateContext = createContext;

            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // Module header
            m_Header = new PCPModuleHeader(
                "Package Audit",
                "\u2750",
                k_AccentColor);
            m_Header.onScan += OnScanClicked;
            m_Header.style.flexShrink = 0;
            Add(m_Header);

            // Filter bar
            var filterBar = new VisualElement();
            filterBar.style.flexDirection = FlexDirection.Row;
            filterBar.style.alignItems = Align.Center;
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

            Add(filterBar);

            // Scrollable card container
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            Add(scrollView);

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

            // Initial population
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

                if (isActive)
                {
                    kvp.Value.style.backgroundColor = new Color(0.337f, 0.612f, 0.839f, 0.3f);
                    kvp.Value.style.borderTopWidth = 1;
                    kvp.Value.style.borderBottomWidth = 1;
                    kvp.Value.style.borderLeftWidth = 1;
                    kvp.Value.style.borderRightWidth = 1;
                    kvp.Value.style.borderTopColor = new Color(0.337f, 0.612f, 0.839f, 0.6f);
                    kvp.Value.style.borderBottomColor = new Color(0.337f, 0.612f, 0.839f, 0.6f);
                    kvp.Value.style.borderLeftColor = new Color(0.337f, 0.612f, 0.839f, 0.6f);
                    kvp.Value.style.borderRightColor = new Color(0.337f, 0.612f, 0.839f, 0.6f);
                }
                else
                {
                    kvp.Value.style.backgroundColor = Color.clear;
                    kvp.Value.style.borderTopWidth = 0;
                    kvp.Value.style.borderBottomWidth = 0;
                    kvp.Value.style.borderLeftWidth = 0;
                    kvp.Value.style.borderRightWidth = 0;
                }
            }

            RefreshCards();
        }

        // --------------------------------------------------------------------
        // Data population
        // --------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the card display from current scan result data.
        /// </summary>
        public void Refresh()
        {
            m_Header.AccentColor = k_AccentColor;
            RefreshCards();
        }

        public void RefreshCards()
        {
            m_CardContainer.Clear();

            var entries = GetFilteredEntries();

            if (entries.Count == 0)
            {
                m_CardContainer.Add(m_EmptyLabel);
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                m_Header.FindingCount = 0;
                return;
            }

            m_EmptyLabel.style.display = DisplayStyle.None;
            m_Header.FindingCount = entries.Count;

            foreach (var entry in entries)
            {
                var card = BuildPackageCard(entry);
                m_CardContainer.Add(card);
            }
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

            // Top row: name + version + status badge
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 6;

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

            // Status badge
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

            // Stats row: reference counts
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

            // Description (if available)
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
        // Actions
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
                        var scanner = new PCPPackageAuditor();
                        scanner.Scan(context);

                        m_ScanResult.packageAuditEntries.Clear();
                        foreach (var result in scanner.Results)
                            m_ScanResult.packageAuditEntries.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProjectCleanPro] Package audit scan failed: {ex}");
                }
                finally
                {
                    m_Header.IsScanning = false;
                    RefreshCards();
                }
            };
        }

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
