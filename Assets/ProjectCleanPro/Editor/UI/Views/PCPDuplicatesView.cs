using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for displaying duplicate asset groups. Uses a custom grouped layout
    /// with a <see cref="Foldout"/> per duplicate group rather than a flat list.
    /// Each group shows its hash header, file count, wasted space, and a list
    /// of entries with reference counts and radio-style "Keep" selection.
    /// </summary>
    public sealed class PCPDuplicatesView : VisualElement, IPCPRefreshable
    {
        // Colors
        private Color k_AccentColor => PCPContext.Settings.GetModuleColor(2);
        private static readonly Color k_CanonicalColor = new Color(0.416f, 0.600f, 0.333f, 1f);
        private static readonly Color k_DuplicateColor = new Color(0.800f, 0.655f, 0.000f, 1f);

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private readonly PCPScanResult m_ScanResult;
        private readonly Func<PCPScanContext> m_CreateContext;
        private readonly PCPModuleHeader m_Header;
        private readonly VisualElement m_GroupContainer;
        private readonly Label m_EmptyLabel;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPDuplicatesView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
        {
            m_ScanResult = scanResult;
            m_CreateContext = createContext;

            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // Module header
            m_Header = new PCPModuleHeader(
                "Duplicate Assets",
                "\u2687",
                k_AccentColor);
            m_Header.onScan += OnScanClicked;
            m_Header.style.flexShrink = 0;
            Add(m_Header);

            // Top action bar
            var topBar = new VisualElement();
            topBar.style.flexDirection = FlexDirection.Row;
            topBar.style.alignItems = Align.Center;
            topBar.style.paddingLeft = 8;
            topBar.style.paddingRight = 8;
            topBar.style.paddingTop = 4;
            topBar.style.paddingBottom = 4;
            topBar.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            topBar.style.borderBottomWidth = 1;
            topBar.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            topBar.style.flexShrink = 0;

            var mergeAllBtn = new Button(OnMergeAllDuplicates)
            {
                text = "Merge All Duplicates"
            };
            mergeAllBtn.style.paddingLeft = 12;
            mergeAllBtn.style.paddingRight = 12;
            mergeAllBtn.style.paddingTop = 4;
            mergeAllBtn.style.paddingBottom = 4;
            mergeAllBtn.style.backgroundColor = k_AccentColor;
            mergeAllBtn.style.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            mergeAllBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            mergeAllBtn.style.borderTopLeftRadius = 3;
            mergeAllBtn.style.borderTopRightRadius = 3;
            mergeAllBtn.style.borderBottomLeftRadius = 3;
            mergeAllBtn.style.borderBottomRightRadius = 3;
            topBar.Add(mergeAllBtn);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            topBar.Add(spacer);

            Add(topBar);

            // Scrollable group container
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            Add(scrollView);

            m_GroupContainer = scrollView.contentContainer;
            m_GroupContainer.style.paddingTop = 8;
            m_GroupContainer.style.paddingBottom = 8;
            m_GroupContainer.style.paddingLeft = 12;
            m_GroupContainer.style.paddingRight = 12;

            // Empty state
            m_EmptyLabel = new Label("No duplicate assets found.\nRun a scan to detect duplicates.");
            m_EmptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_EmptyLabel.style.fontSize = 13;
            m_EmptyLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            m_EmptyLabel.style.paddingTop = 48;
            m_EmptyLabel.style.paddingBottom = 48;
            m_EmptyLabel.style.display = DisplayStyle.None;
            m_GroupContainer.Add(m_EmptyLabel);

            // Initial population
            RefreshGroups();
        }

        // --------------------------------------------------------------------
        // Data population
        // --------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the group display from the current scan result.
        /// </summary>
        public void Refresh()
        {
            m_Header.AccentColor = k_AccentColor;
            RefreshGroups();
        }

        public void RefreshGroups()
        {
            m_GroupContainer.Clear();

            if (m_ScanResult == null || m_ScanResult.duplicateGroups == null ||
                m_ScanResult.duplicateGroups.Count == 0)
            {
                m_GroupContainer.Add(m_EmptyLabel);
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                UpdateHeader(0, 0);
                return;
            }

            m_EmptyLabel.style.display = DisplayStyle.None;

            var groups = m_ScanResult.duplicateGroups;

            long totalWasted = 0;
            foreach (var group in groups)
                totalWasted += group.WastedBytes;

            UpdateHeader(groups.Count, totalWasted);

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var groupElement = BuildGroupElement(group, g);
                m_GroupContainer.Add(groupElement);
            }
        }

        // --------------------------------------------------------------------
        // Group element
        // --------------------------------------------------------------------

        private VisualElement BuildGroupElement(PCPDuplicateGroup group, int groupIndex)
        {
            var container = new VisualElement();
            container.style.marginBottom = 8;
            container.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            container.style.borderTopLeftRadius = 4;
            container.style.borderTopRightRadius = 4;
            container.style.borderBottomLeftRadius = 4;
            container.style.borderBottomRightRadius = 4;
            container.style.borderLeftWidth = 3;
            container.style.borderLeftColor = k_AccentColor;

            // Short hash for display
            string shortHash = !string.IsNullOrEmpty(group.hash) && group.hash.Length >= 12
                ? group.hash.Substring(0, 12) + "..."
                : group.hash ?? "unknown";

            string foldoutTitle = $"Hash: {shortHash}  |  " +
                $"{group.entries.Count} copies  |  " +
                $"{PCPAssetInfo.FormatBytes(group.WastedBytes)} wasted";

            var foldout = new Foldout { text = foldoutTitle };
            foldout.value = groupIndex < 5; // Expand first 5 groups by default
            foldout.style.paddingLeft = 8;
            foldout.style.paddingRight = 8;
            foldout.style.paddingTop = 6;
            foldout.style.paddingBottom = 6;

            // Merge single group button – placed inside the foldout toggle so it
            // sits on the header row. We stop propagation so the click doesn't
            // toggle the foldout open/closed.
            var mergeBtn = new Button(() => OnMergeSingleGroup(group))
            {
                text = "Merge"
            };
            mergeBtn.style.marginLeft = 8;
            mergeBtn.style.paddingLeft = 8;
            mergeBtn.style.paddingRight = 8;
            mergeBtn.style.paddingTop = 2;
            mergeBtn.style.paddingBottom = 2;
            mergeBtn.style.fontSize = 11;
            mergeBtn.style.backgroundColor = k_AccentColor;
            mergeBtn.style.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            mergeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            mergeBtn.style.borderTopLeftRadius = 3;
            mergeBtn.style.borderTopRightRadius = 3;
            mergeBtn.style.borderBottomLeftRadius = 3;
            mergeBtn.style.borderBottomRightRadius = 3;
            mergeBtn.tooltip = "Merge this duplicate group only";
            mergeBtn.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            foldout.Q<Toggle>().Add(mergeBtn);

            // Auto-elect the entry with the highest reference count unless
            // the user has manually chosen which copy to keep.
            if (!group.hasUserOverride)
                group.ElectCanonical();

            // Build entry rows
            for (int e = 0; e < group.entries.Count; e++)
            {
                var entry = group.entries[e];
                int entryIndex = e;

                var entryRow = new VisualElement();
                entryRow.style.flexDirection = FlexDirection.Row;
                entryRow.style.alignItems = Align.Center;
                entryRow.style.paddingTop = 4;
                entryRow.style.paddingBottom = 4;
                entryRow.style.paddingLeft = 4;

                if (e < group.entries.Count - 1)
                {
                    entryRow.style.borderBottomWidth = 1;
                    entryRow.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                }

                // "Keep" radio button
                var keepBtn = new Button(() =>
                {
                    if (entry.isCanonical) return; // Already selected
                    foreach (var en in group.entries)
                        en.isCanonical = false;
                    entry.isCanonical = true;
                    group.hasUserOverride = true;
                    RefreshGroups();
                });
                keepBtn.text = entry.isCanonical ? "\u25C9" : "\u25CB"; // ◉ filled / ○ empty
                keepBtn.style.width = 22;
                keepBtn.style.height = 22;
                keepBtn.style.marginRight = 8;
                keepBtn.style.fontSize = 14;
                keepBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                keepBtn.style.backgroundColor = Color.clear;
                keepBtn.style.borderLeftWidth = 0;
                keepBtn.style.borderRightWidth = 0;
                keepBtn.style.borderTopWidth = 0;
                keepBtn.style.borderBottomWidth = 0;
                keepBtn.style.color = entry.isCanonical ? k_CanonicalColor : new Color(0.5f, 0.5f, 0.5f, 1f);
                keepBtn.tooltip = "Mark this copy as the one to keep";
                entryRow.Add(keepBtn);

                // Keep/Duplicate badge
                if (entry.isCanonical)
                {
                    var keepBadge = new PCPBadge("KEEP", k_CanonicalColor);
                    keepBadge.style.marginRight = 8;
                    keepBadge.style.minWidth = 55;
                    entryRow.Add(keepBadge);
                }
                else
                {
                    var dupeBadge = new PCPBadge("DUPE", k_DuplicateColor);
                    dupeBadge.style.marginRight = 8;
                    dupeBadge.style.minWidth = 55;
                    entryRow.Add(dupeBadge);
                }

                // Icon
                Texture2D icon = null;
                if (!string.IsNullOrEmpty(entry.path))
                    icon = AssetDatabase.GetCachedIcon(entry.path) as Texture2D;

                if (icon != null)
                {
                    var iconImage = new Image { image = icon };
                    iconImage.style.width = 16;
                    iconImage.style.height = 16;
                    iconImage.style.marginRight = 6;
                    iconImage.scaleMode = ScaleMode.ScaleToFit;
                    entryRow.Add(iconImage);
                }

                // File path
                string fileName = System.IO.Path.GetFileName(entry.path);
                var nameLabel = new Label(fileName);
                nameLabel.style.fontSize = 12;
                nameLabel.style.flexGrow = 1;
                nameLabel.style.overflow = Overflow.Hidden;
                nameLabel.style.textOverflow = TextOverflow.Ellipsis;
                nameLabel.tooltip = entry.path;

                // Double-click to ping
                nameLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount == 2 && !string.IsNullOrEmpty(entry.path))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.path);
                        if (obj != null)
                            EditorGUIUtility.PingObject(obj);
                    }
                });
                entryRow.Add(nameLabel);

                // Reference count
                var refLabel = new Label($"{entry.referenceCount} refs");
                refLabel.style.fontSize = 11;
                refLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                refLabel.style.marginRight = 8;
                refLabel.style.minWidth = 50;
                refLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                entryRow.Add(refLabel);

                // Size
                var sizeLabel = new Label(PCPAssetInfo.FormatBytes(entry.sizeBytes));
                sizeLabel.style.fontSize = 11;
                sizeLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                sizeLabel.style.minWidth = 60;
                sizeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                entryRow.Add(sizeLabel);

                foldout.Add(entryRow);
            }

            container.Add(foldout);
            return container;
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
                        // Ensure the dependency graph is built so we get
                        // accurate reference counts and deletion warnings.
                        if (!context.DependencyResolver.IsBuilt)
                            context.DependencyResolver.Build(System.Array.Empty<string>());

                        var scanner = new PCPDuplicateDetector();
                        scanner.Scan(context);

                        m_ScanResult.duplicateGroups.Clear();
                        foreach (var result in scanner.Results)
                            m_ScanResult.duplicateGroups.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ProjectCleanPro] Duplicate scan failed: {ex}");
                }
                finally
                {
                    m_Header.IsScanning = false;
                    RefreshGroups();
                }
            };
        }

        private void OnMergeSingleGroup(PCPDuplicateGroup group)
        {
            MergeGroups(new List<PCPDuplicateGroup> { group });
        }

        private void OnMergeAllDuplicates()
        {
            if (m_ScanResult == null || m_ScanResult.duplicateGroups == null ||
                m_ScanResult.duplicateGroups.Count == 0)
            {
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    "No duplicate groups to merge.", "OK");
                return;
            }

            MergeGroups(m_ScanResult.duplicateGroups);
        }

        private void MergeGroups(List<PCPDuplicateGroup> groups)
        {
            // Collect all non-canonical entries for deletion
            var pathsToDelete = new List<string>();
            foreach (var group in groups)
            {
                // Respect the user's radio-button selection – do NOT re-elect here.
                if (!group.hasUserOverride)
                    group.ElectCanonical();

                foreach (var entry in group.entries)
                {
                    if (!entry.isCanonical && !string.IsNullOrEmpty(entry.path))
                        pathsToDelete.Add(entry.path);
                }
            }

            if (pathsToDelete.Count == 0)
            {
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    "No duplicate entries to remove.", "OK");
                return;
            }

            var context = m_CreateContext?.Invoke();
            var resolver = context?.DependencyResolver;
            var preview = PCPSafeDelete.Preview(pathsToDelete, resolver);

            if (PCPDeletePreviewDialog.Show(preview, out bool archive))
            {
                var settings = PCPContext.Settings;
                bool originalArchive = settings.archiveBeforeDelete;
                settings.archiveBeforeDelete = archive;

                try
                {
                    PCPSafeDelete.ArchiveAndDelete(preview, settings, resolver);

                    // Remove deleted entries from groups
                    var deletedSet = new HashSet<string>(pathsToDelete, StringComparer.Ordinal);
                    foreach (var group in m_ScanResult.duplicateGroups)
                    {
                        group.entries.RemoveAll(e => deletedSet.Contains(e.path));
                    }

                    // Remove groups with fewer than 2 entries
                    m_ScanResult.duplicateGroups.RemoveAll(g => g.entries.Count < 2);

                    RefreshGroups();
                }
                finally
                {
                    settings.archiveBeforeDelete = originalArchive;
                }
            }
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void UpdateHeader(int groupCount, long wastedBytes)
        {
            m_Header.FindingCount = groupCount;
            m_Header.TotalSize = wastedBytes;
        }
    }
}
