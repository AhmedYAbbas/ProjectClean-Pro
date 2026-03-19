using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Modal dialog window that displays a <see cref="PCPDeletePreview"/> and
    /// allows the user to confirm or cancel the deletion. Shows file list with
    /// sizes, warnings for referenced assets, a total size summary, and an
    /// archive toggle.
    /// </summary>
    public sealed class PCPDeletePreviewDialog : EditorWindow
    {
        // --------------------------------------------------------------------
        // Constants
        // --------------------------------------------------------------------

        private const string WindowTitle = "Confirm Deletion";
        private const float WindowWidth = 580f;
        private const float WindowHeight = 480f;

        private static readonly Color k_DangerColor = new Color(0.957f, 0.278f, 0.278f, 1f);
        private static readonly Color k_WarningColor = new Color(0.800f, 0.655f, 0.000f, 1f);
        private static readonly Color k_SummaryBg = new Color(0.18f, 0.18f, 0.18f, 1f);

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private PCPDeletePreview m_Preview;
        private bool m_ArchiveEnabled = true;
        private bool m_Confirmed;
        private bool m_Closed;

        /// <summary>Whether the user chose to archive before deletion.</summary>
        public bool ArchiveEnabled => m_ArchiveEnabled;

        // --------------------------------------------------------------------
        // Static API
        // --------------------------------------------------------------------

        /// <summary>
        /// Shows a modal delete-preview dialog. Returns true if the user
        /// confirmed the deletion.
        /// </summary>
        /// <param name="preview">The delete preview to display.</param>
        /// <param name="archiveEnabled">
        /// Outputs whether the archive checkbox was checked when confirmed.
        /// </param>
        /// <returns>True if the user clicked Delete, false if cancelled.</returns>
        public static bool Show(PCPDeletePreview preview, out bool archiveEnabled)
        {
            archiveEnabled = true;

            if (preview == null || !preview.HasItems)
                return false;

            var dialog = CreateInstance<PCPDeletePreviewDialog>();
            dialog.m_Preview = preview;
            dialog.m_ArchiveEnabled = true;
            dialog.m_Confirmed = false;
            dialog.m_Closed = false;
            dialog.titleContent = new GUIContent(WindowTitle);
            dialog.minSize = new Vector2(WindowWidth, WindowHeight);
            dialog.maxSize = new Vector2(WindowWidth + 200, WindowHeight + 300);
            dialog.ShowUtility();

            // Block until the dialog is closed (modal behavior)
            while (!dialog.m_Closed)
            {
                // Process editor events
                if (!dialog)
                    break;
            }

            archiveEnabled = dialog.m_ArchiveEnabled;
            bool confirmed = dialog.m_Confirmed;

            if (dialog)
                dialog.Close();

            return confirmed;
        }

        /// <summary>
        /// Simplified overload that returns true if confirmed, with archive defaulted on.
        /// </summary>
        public static bool Show(PCPDeletePreview preview)
        {
            return Show(preview, out _);
        }

        // --------------------------------------------------------------------
        // Lifecycle
        // --------------------------------------------------------------------

        private void OnDestroy()
        {
            m_Closed = true;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);

            if (m_Preview == null)
            {
                root.Add(new Label("No preview data."));
                return;
            }

            // ---- Header ----
            BuildHeader(root);

            // ---- Warning panel (if any) ----
            BuildWarningPanel(root);

            // ---- File list ----
            BuildFileList(root);

            // ---- Summary bar ----
            BuildSummaryBar(root);

            // ---- Action bar ----
            BuildActionBar(root);
        }

        // --------------------------------------------------------------------
        // Header
        // --------------------------------------------------------------------

        private void BuildHeader(VisualElement parent)
        {
            var header = new VisualElement();
            header.style.paddingLeft = 16;
            header.style.paddingRight = 16;
            header.style.paddingTop = 12;
            header.style.paddingBottom = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            header.style.flexShrink = 0;

            var title = new Label("Delete Preview");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
            title.style.marginBottom = 4;
            header.Add(title);

            int count = m_Preview.items.Count;
            var subtitle = new Label(
                $"{count} file{(count != 1 ? "s" : "")} selected for deletion");
            subtitle.style.fontSize = 12;
            subtitle.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            header.Add(subtitle);

            parent.Add(header);
        }

        // --------------------------------------------------------------------
        // Warning panel
        // --------------------------------------------------------------------

        private void BuildWarningPanel(VisualElement parent)
        {
            if (m_Preview.warnings == null || m_Preview.warnings.Count == 0)
                return;

            var panel = new VisualElement();
            panel.style.paddingLeft = 16;
            panel.style.paddingRight = 16;
            panel.style.paddingTop = 8;
            panel.style.paddingBottom = 8;
            panel.style.flexShrink = 0;

            Color panelBg = m_Preview.HasErrors
                ? new Color(k_DangerColor.r, k_DangerColor.g, k_DangerColor.b, 0.12f)
                : new Color(k_WarningColor.r, k_WarningColor.g, k_WarningColor.b, 0.12f);
            panel.style.backgroundColor = panelBg;

            Color borderColor = m_Preview.HasErrors ? k_DangerColor : k_WarningColor;
            panel.style.borderLeftWidth = 3;
            panel.style.borderLeftColor = borderColor;

            var warningTitle = new Label(
                m_Preview.HasErrors
                    ? $"{m_Preview.errorCount} error(s), {m_Preview.warningCount} warning(s)"
                    : $"{m_Preview.warningCount} warning(s)");
            warningTitle.style.fontSize = 12;
            warningTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            warningTitle.style.color = borderColor;
            warningTitle.style.marginBottom = 4;
            panel.Add(warningTitle);

            int maxWarnings = Mathf.Min(m_Preview.warnings.Count, 10);
            for (int i = 0; i < maxWarnings; i++)
            {
                var warning = m_Preview.warnings[i];
                var warnLabel = new Label($"  {warning}");
                warnLabel.style.fontSize = 11;
                warnLabel.style.color = new Color(0.831f, 0.831f, 0.831f, 0.8f);
                warnLabel.style.overflow = Overflow.Hidden;
                warnLabel.style.textOverflow = TextOverflow.Ellipsis;
                warnLabel.tooltip = warning.ToString();
                panel.Add(warnLabel);
            }

            if (m_Preview.warnings.Count > maxWarnings)
            {
                var moreLabel = new Label(
                    $"  ...and {m_Preview.warnings.Count - maxWarnings} more warning(s)");
                moreLabel.style.fontSize = 11;
                moreLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                panel.Add(moreLabel);
            }

            parent.Add(panel);
        }

        // --------------------------------------------------------------------
        // File list
        // --------------------------------------------------------------------

        private void BuildFileList(VisualElement parent)
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            scrollView.style.paddingLeft = 16;
            scrollView.style.paddingRight = 16;
            scrollView.style.paddingTop = 8;

            foreach (var item in m_Preview.items)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingTop = 3;
                row.style.paddingBottom = 3;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

                // File name
                string fileName = System.IO.Path.GetFileName(item.path);
                var nameLabel = new Label(fileName);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.fontSize = 12;
                nameLabel.style.overflow = Overflow.Hidden;
                nameLabel.style.textOverflow = TextOverflow.Ellipsis;
                nameLabel.tooltip = item.path;
                row.Add(nameLabel);

                // Reference count warning
                if (item.referenceCount > 0)
                {
                    var refBadge = PCPBadge.Warning($"{item.referenceCount} refs");
                    refBadge.style.marginRight = 4;
                    row.Add(refBadge);
                }

                // Resources warning
                if (item.isInResources)
                {
                    var resBadge = PCPBadge.Warning("Resources");
                    resBadge.style.marginRight = 4;
                    row.Add(resBadge);
                }

                // Size
                var sizeLabel = new Label(item.formattedSize ?? PCPAssetInfo.FormatBytes(item.sizeBytes));
                sizeLabel.style.fontSize = 11;
                sizeLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                sizeLabel.style.minWidth = 60;
                sizeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(sizeLabel);

                scrollView.Add(row);
            }

            parent.Add(scrollView);
        }

        // --------------------------------------------------------------------
        // Summary bar
        // --------------------------------------------------------------------

        private void BuildSummaryBar(VisualElement parent)
        {
            var summary = new VisualElement();
            summary.style.flexDirection = FlexDirection.Row;
            summary.style.alignItems = Align.Center;
            summary.style.paddingLeft = 16;
            summary.style.paddingRight = 16;
            summary.style.paddingTop = 8;
            summary.style.paddingBottom = 8;
            summary.style.backgroundColor = k_SummaryBg;
            summary.style.borderTopWidth = 1;
            summary.style.borderTopColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            summary.style.flexShrink = 0;

            var totalLabel = new Label(
                $"Total: {m_Preview.items.Count} file(s), " +
                $"{m_Preview.formattedTotalSize ?? PCPAssetInfo.FormatBytes(m_Preview.totalSizeBytes)}");
            totalLabel.style.flexGrow = 1;
            totalLabel.style.fontSize = 12;
            totalLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            summary.Add(totalLabel);

            // Archive toggle
            var archiveToggle = new Toggle("Archive before deletion");
            archiveToggle.value = m_ArchiveEnabled;
            archiveToggle.style.marginLeft = 16;
            archiveToggle.RegisterValueChangedCallback(evt =>
            {
                m_ArchiveEnabled = evt.newValue;
            });
            summary.Add(archiveToggle);

            parent.Add(summary);
        }

        // --------------------------------------------------------------------
        // Action bar
        // --------------------------------------------------------------------

        private void BuildActionBar(VisualElement parent)
        {
            var actionBar = new VisualElement();
            actionBar.style.flexDirection = FlexDirection.Row;
            actionBar.style.justifyContent = Justify.FlexEnd;
            actionBar.style.alignItems = Align.Center;
            actionBar.style.paddingLeft = 16;
            actionBar.style.paddingRight = 16;
            actionBar.style.paddingTop = 8;
            actionBar.style.paddingBottom = 12;
            actionBar.style.flexShrink = 0;

            // Cancel button
            var cancelBtn = new Button(OnCancel) { text = "Cancel" };
            cancelBtn.AddToClassList("pcp-button-secondary");
            cancelBtn.style.paddingLeft = 20;
            cancelBtn.style.paddingRight = 20;
            cancelBtn.style.paddingTop = 6;
            cancelBtn.style.paddingBottom = 6;
            cancelBtn.style.marginRight = 8;
            actionBar.Add(cancelBtn);

            // Delete button (danger style)
            var deleteBtn = new Button(OnConfirm)
            {
                text = $"Delete {m_Preview.items.Count} File(s)"
            };
            deleteBtn.style.paddingLeft = 20;
            deleteBtn.style.paddingRight = 20;
            deleteBtn.style.paddingTop = 6;
            deleteBtn.style.paddingBottom = 6;
            deleteBtn.style.backgroundColor = k_DangerColor;
            deleteBtn.style.color = Color.white;
            deleteBtn.style.borderTopLeftRadius = 3;
            deleteBtn.style.borderTopRightRadius = 3;
            deleteBtn.style.borderBottomLeftRadius = 3;
            deleteBtn.style.borderBottomRightRadius = 3;
            deleteBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            actionBar.Add(deleteBtn);

            parent.Add(actionBar);
        }

        // --------------------------------------------------------------------
        // Callbacks
        // --------------------------------------------------------------------

        private void OnConfirm()
        {
            m_Confirmed = true;
            m_Closed = true;
            Close();
        }

        private void OnCancel()
        {
            m_Confirmed = false;
            m_Closed = true;
            Close();
        }
    }
}
