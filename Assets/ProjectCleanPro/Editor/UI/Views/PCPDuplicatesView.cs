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
    /// with a <see cref="Foldout"/> per duplicate group. Each group shows its
    /// hash header, file count, wasted space, and a list of entries with
    /// reference counts and radio-style "Keep" selection.
    /// </summary>
    public sealed class PCPDuplicatesView : PCPModuleView,
        IPCPMergeableView, IPCPExportableView
    {
        // Colors
        private Color k_AccentColor => PCPContext.Settings.GetModuleColor(2);
        private static readonly Color k_CanonicalColor = new Color(0.416f, 0.600f, 0.333f, 1f);
        private static readonly Color k_DuplicateColor = new Color(0.800f, 0.655f, 0.000f, 1f);

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private VisualElement m_GroupContainer;
        private Label m_EmptyLabel;
        private TextField m_SearchField;
        private PopupField<string> m_TypeDropdown;
        private PopupField<string> m_SortDropdown;

        private string m_SearchText = string.Empty;
        private string m_TypeFilter = "All Types";
        private string m_SortMode = "Wasted Size";

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPDuplicatesView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
            : base(
                scanResult,
                createContext,
                "Duplicate Assets",
                "\u25A6",
                2)
        {
        }

        protected override PCPModuleId GetModuleId() => PCPModuleId.Duplicates;

        // --------------------------------------------------------------------
        // IPCPExportableView
        // --------------------------------------------------------------------

        public string ModuleExportKey => "duplicates";

        // --------------------------------------------------------------------
        // IPCPMergeableView
        // --------------------------------------------------------------------

        public void MergeAll()
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

            m_SearchField = new TextField();
            m_SearchField.style.flexGrow = 1;
            m_SearchField.style.height = 24;
            m_SearchField.style.marginRight = 8;
            m_SearchField.style.minWidth = 120;
            m_SearchField.value = string.Empty;
            m_SearchField.tooltip = "Search by file name...";
            m_SearchField.RegisterValueChangedCallback(evt =>
            {
                m_SearchText = evt.newValue ?? string.Empty;
                RefreshGroups();
            });
            filterBar.Add(m_SearchField);

            var typeChoices = new List<string>
            {
                "All Types", "Textures", "Audio", "Models", "Materials", "Other"
            };
            m_TypeDropdown = new PopupField<string>(typeChoices, 0);
            m_TypeDropdown.style.minWidth = 110;
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
                "Wasted Size", "Copy Count", "File Size"
            };
            m_SortDropdown = new PopupField<string>(sortChoices, 0);
            m_SortDropdown.style.minWidth = 110;
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
                m_TypeDropdown.value = "All Types";
                m_SortDropdown.value = "Wasted Size";
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
            m_EmptyLabel = new Label("No duplicate assets found.\nRun a scan to detect duplicates.");
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

            if (m_ScanResult == null || m_ScanResult.duplicateGroups == null ||
                m_ScanResult.duplicateGroups.Count == 0)
            {
                m_GroupContainer.Add(m_EmptyLabel);
                m_EmptyLabel.text = "No duplicate assets found.\nRun a scan to detect duplicates.";
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                UpdateHeader(0, 0);
                return;
            }

            m_EmptyLabel.style.display = DisplayStyle.None;

            // Filter groups
            var filtered = new List<PCPDuplicateGroup>();
            foreach (var group in m_ScanResult.duplicateGroups)
            {
                if (!MatchesFilters(group))
                    continue;
                filtered.Add(group);
            }

            if (filtered.Count == 0)
            {
                m_GroupContainer.Add(m_EmptyLabel);
                m_EmptyLabel.text = "No results match the current filters.";
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                UpdateHeader(0, 0);
                return;
            }

            // Sort groups
            switch (m_SortMode)
            {
                case "Copy Count":
                    filtered.Sort((a, b) =>
                    {
                        int cmp = b.entries.Count.CompareTo(a.entries.Count);
                        return cmp != 0 ? cmp : b.WastedBytes.CompareTo(a.WastedBytes);
                    });
                    break;
                case "File Size":
                    filtered.Sort((a, b) =>
                    {
                        long sizeA = a.entries.Count > 0 ? a.entries[0].sizeBytes : 0;
                        long sizeB = b.entries.Count > 0 ? b.entries[0].sizeBytes : 0;
                        return sizeB.CompareTo(sizeA);
                    });
                    break;
                default: // "Wasted Size"
                    filtered.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));
                    break;
            }

            long totalWasted = 0;
            foreach (var group in filtered)
                totalWasted += group.WastedBytes;

            UpdateHeader(filtered.Count, totalWasted);

            for (int g = 0; g < filtered.Count; g++)
            {
                var groupElement = BuildGroupElement(filtered[g], g);
                m_GroupContainer.Add(groupElement);
            }
        }

        private bool MatchesFilters(PCPDuplicateGroup group)
        {
            if (group.entries == null || group.entries.Count == 0)
                return false;

            // Name search — match if any entry's filename contains the search text
            if (!string.IsNullOrEmpty(m_SearchText))
            {
                string searchLower = m_SearchText.ToLowerInvariant();
                bool anyMatch = false;
                foreach (var entry in group.entries)
                {
                    string fileName = System.IO.Path.GetFileName(entry.path) ?? string.Empty;
                    if (fileName.ToLowerInvariant().IndexOf(searchLower, StringComparison.Ordinal) >= 0)
                    {
                        anyMatch = true;
                        break;
                    }
                }
                if (!anyMatch)
                    return false;
            }

            // Type filter — match based on the first entry's extension
            if (m_TypeFilter != "All Types")
            {
                string ext = System.IO.Path.GetExtension(group.entries[0].path).ToLowerInvariant();
                bool matchesType = false;
                switch (m_TypeFilter)
                {
                    case "Textures":
                        matchesType = ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                                      ext == ".tga" || ext == ".psd" || ext == ".tif" ||
                                      ext == ".tiff" || ext == ".bmp" || ext == ".exr" || ext == ".hdr";
                        break;
                    case "Audio":
                        matchesType = ext == ".wav" || ext == ".mp3" || ext == ".ogg" || ext == ".aiff";
                        break;
                    case "Models":
                        matchesType = ext == ".fbx" || ext == ".obj" || ext == ".blend" ||
                                      ext == ".dae" || ext == ".3ds";
                        break;
                    case "Materials":
                        matchesType = ext == ".mat" || ext == ".shader" || ext == ".shadergraph";
                        break;
                    case "Other":
                        matchesType = !(ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                                        ext == ".tga" || ext == ".psd" || ext == ".tif" ||
                                        ext == ".tiff" || ext == ".bmp" || ext == ".exr" || ext == ".hdr" ||
                                        ext == ".wav" || ext == ".mp3" || ext == ".ogg" || ext == ".aiff" ||
                                        ext == ".fbx" || ext == ".obj" || ext == ".blend" ||
                                        ext == ".dae" || ext == ".3ds" ||
                                        ext == ".mat" || ext == ".shader" || ext == ".shadergraph");
                        break;
                }
                if (!matchesType)
                    return false;
            }

            return true;
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
            foldout.value = groupIndex < 5;
            foldout.style.paddingLeft = 8;
            foldout.style.paddingRight = 8;
            foldout.style.paddingTop = 6;
            foldout.style.paddingBottom = 6;

            // Merge single group button
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
                    if (entry.isCanonical) return;
                    foreach (var en in group.entries)
                        en.isCanonical = false;
                    entry.isCanonical = true;
                    group.hasUserOverride = true;
                    RefreshGroups();
                });
                keepBtn.text = entry.isCanonical ? "\u25C9" : "\u25CB";
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
        // Merge logic
        // --------------------------------------------------------------------

        private void OnMergeSingleGroup(PCPDuplicateGroup group)
        {
            MergeGroups(new List<PCPDuplicateGroup> { group });
        }

        private void MergeGroups(List<PCPDuplicateGroup> groups)
        {
            // Collect all non-canonical entries for deletion
            var pathsToDelete = new List<string>();
            foreach (var group in groups)
            {
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
