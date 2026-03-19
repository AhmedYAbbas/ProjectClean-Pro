using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for browsing, restoring, and purging archive sessions created by
    /// <see cref="PCPArchiveManager"/>. Each session is shown as an expandable
    /// card with file list, timestamp, and size information.
    /// </summary>
    public sealed class PCPArchiveView : VisualElement
    {
        // USS class names
        public const string UssClassName = "pcp-archive-view";
        public const string SessionCardUssClassName = "pcp-archive-view__session";
        public const string EmptyStateUssClassName = "pcp-archive-view__empty";

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private readonly VisualElement m_SessionList;
        private readonly Label m_EmptyLabel;
        private readonly Label m_SummaryLabel;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPArchiveView()
        {
            AddToClassList(UssClassName);
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("pcp-scroll-view");
            Add(scrollView);

            var container = scrollView.contentContainer;
            container.style.paddingTop = 16;
            container.style.paddingBottom = 16;
            container.style.paddingLeft = 24;
            container.style.paddingRight = 24;

            // Header
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 16;

            var header = new Label("Archive Sessions");
            header.AddToClassList("pcp-label-header");
            header.style.flexGrow = 1;
            headerRow.Add(header);

            // Purge button
            var purgeBtn = new Button(OnPurgeOldArchives)
            {
                text = "Purge Old Archives"
            };
            purgeBtn.AddToClassList("pcp-button-danger");
            purgeBtn.style.paddingLeft = 12;
            purgeBtn.style.paddingRight = 12;
            purgeBtn.style.paddingTop = 4;
            purgeBtn.style.paddingBottom = 4;
            headerRow.Add(purgeBtn);

            // Refresh button
            var refreshBtn = new Button(RefreshSessions)
            {
                text = "Refresh"
            };
            refreshBtn.AddToClassList("pcp-button-secondary");
            refreshBtn.style.paddingLeft = 12;
            refreshBtn.style.paddingRight = 12;
            refreshBtn.style.paddingTop = 4;
            refreshBtn.style.paddingBottom = 4;
            refreshBtn.style.marginLeft = 8;
            headerRow.Add(refreshBtn);

            container.Add(headerRow);

            // Summary label
            m_SummaryLabel = new Label();
            m_SummaryLabel.style.fontSize = 12;
            m_SummaryLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            m_SummaryLabel.style.marginBottom = 12;
            container.Add(m_SummaryLabel);

            // Session list
            m_SessionList = new VisualElement();
            container.Add(m_SessionList);

            // Empty state
            m_EmptyLabel = new Label("No archive sessions found.\n\n" +
                "Archives are created automatically when assets are deleted with " +
                "the \"Archive before deletion\" setting enabled.");
            m_EmptyLabel.AddToClassList(EmptyStateUssClassName);
            m_EmptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_EmptyLabel.style.fontSize = 13;
            m_EmptyLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            m_EmptyLabel.style.paddingTop = 48;
            m_EmptyLabel.style.paddingBottom = 48;
            m_EmptyLabel.style.display = DisplayStyle.None;
            container.Add(m_EmptyLabel);

            // Initial load
            RefreshSessions();
        }

        // --------------------------------------------------------------------
        // Data loading
        // --------------------------------------------------------------------

        /// <summary>
        /// Reloads all archive sessions from disk and rebuilds the UI.
        /// </summary>
        public void RefreshSessions()
        {
            m_SessionList.Clear();

            List<PCPArchiveSession> sessions;
            try
            {
                sessions = PCPArchiveManager.GetAllSessions();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectCleanPro] Failed to load archive sessions: {ex.Message}");
                sessions = new List<PCPArchiveSession>();
            }

            if (sessions.Count == 0)
            {
                m_EmptyLabel.style.display = DisplayStyle.Flex;
                m_SummaryLabel.text = "No archive sessions.";
                return;
            }

            m_EmptyLabel.style.display = DisplayStyle.None;

            // Summary
            long totalArchiveSize = 0;
            int totalFiles = 0;
            foreach (var session in sessions)
            {
                totalArchiveSize += session.totalSizeBytes;
                totalFiles += session.assetCount;
            }

            m_SummaryLabel.text = $"{sessions.Count} session(s), " +
                $"{totalFiles} file(s), " +
                $"{PCPAssetInfo.FormatBytes(totalArchiveSize)} total";

            // Build cards
            foreach (var session in sessions)
            {
                var card = BuildSessionCard(session);
                m_SessionList.Add(card);
            }
        }

        // --------------------------------------------------------------------
        // Session card
        // --------------------------------------------------------------------

        private VisualElement BuildSessionCard(PCPArchiveSession session)
        {
            var card = new VisualElement();
            card.AddToClassList(SessionCardUssClassName);
            card.AddToClassList("pcp-card");
            card.style.marginBottom = 8;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = new Color(0.337f, 0.612f, 0.839f, 1f);

            // Header row: timestamp + stats + actions
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            // Timestamp
            var timestampLabel = new Label(session.createdAtUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
            timestampLabel.style.fontSize = 13;
            timestampLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            timestampLabel.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
            timestampLabel.style.marginRight = 12;
            headerRow.Add(timestampLabel);

            // File count badge
            var countBadge = new PCPBadge(
                $"{session.assetCount} file{(session.assetCount != 1 ? "s" : "")}",
                new Color(0.337f, 0.612f, 0.839f, 1f));
            countBadge.style.marginRight = 8;
            headerRow.Add(countBadge);

            // Size badge
            var sizeBadge = new PCPBadge(
                session.formattedTotalSize ?? PCPAssetInfo.FormatBytes(session.totalSizeBytes),
                new Color(0.416f, 0.600f, 0.333f, 1f));
            sizeBadge.style.marginRight = 8;
            headerRow.Add(sizeBadge);

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            headerRow.Add(spacer);

            // Restore button
            var restoreBtn = new Button(() => OnRestore(session))
            {
                text = "Restore"
            };
            restoreBtn.AddToClassList("pcp-button-secondary");
            restoreBtn.style.paddingLeft = 8;
            restoreBtn.style.paddingRight = 8;
            restoreBtn.style.paddingTop = 3;
            restoreBtn.style.paddingBottom = 3;
            restoreBtn.style.fontSize = 11;
            restoreBtn.style.marginRight = 4;
            headerRow.Add(restoreBtn);

            // Delete archive button
            var deleteBtn = new Button(() => OnDeleteArchive(session))
            {
                text = "Delete Archive"
            };
            deleteBtn.style.paddingLeft = 8;
            deleteBtn.style.paddingRight = 8;
            deleteBtn.style.paddingTop = 3;
            deleteBtn.style.paddingBottom = 3;
            deleteBtn.style.fontSize = 11;
            deleteBtn.style.color = new Color(0.957f, 0.278f, 0.278f, 1f);
            headerRow.Add(deleteBtn);

            card.Add(headerRow);

            // Session ID
            var idLabel = new Label($"Session: {session.sessionId}");
            idLabel.style.fontSize = 10;
            idLabel.style.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            idLabel.style.marginTop = 4;
            card.Add(idLabel);

            // Expandable file list
            if (session.archivedPaths != null && session.archivedPaths.Count > 0)
            {
                var foldout = new Foldout { text = $"Files ({session.archivedPaths.Count})" };
                foldout.value = false;
                foldout.style.marginTop = 4;
                foldout.style.fontSize = 11;

                foreach (string path in session.archivedPaths)
                {
                    var fileLabel = new Label(path);
                    fileLabel.style.fontSize = 11;
                    fileLabel.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                    fileLabel.style.paddingLeft = 8;
                    fileLabel.style.overflow = Overflow.Hidden;
                    fileLabel.style.textOverflow = TextOverflow.Ellipsis;
                    fileLabel.tooltip = path;
                    foldout.Add(fileLabel);
                }

                card.Add(foldout);
            }

            return card;
        }

        // --------------------------------------------------------------------
        // Actions
        // --------------------------------------------------------------------

        private void OnRestore(PCPArchiveSession session)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Restore Archive",
                $"Restore {session.assetCount} file(s) from archive " +
                $"'{session.sessionId}'?\n\n" +
                "Existing files at the original locations will be overwritten.",
                "Restore", "Cancel");

            if (!confirmed)
                return;

            try
            {
                PCPArchiveManager.RestoreSession(session.sessionId);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("ProjectCleanPro",
                    $"Successfully restored {session.assetCount} file(s).", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Restore Failed",
                    $"Failed to restore archive:\n{ex.Message}", "OK");
            }
        }

        private void OnDeleteArchive(PCPArchiveSession session)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Archive",
                $"Permanently delete archive '{session.sessionId}'?\n\n" +
                $"This will remove {session.assetCount} archived file(s) " +
                $"({session.formattedTotalSize ?? PCPAssetInfo.FormatBytes(session.totalSizeBytes)}).\n" +
                "This cannot be undone.",
                "Delete", "Cancel");

            if (!confirmed)
                return;

            try
            {
                PCPArchiveManager.DeleteSession(session.sessionId);
                RefreshSessions();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Delete Failed",
                    $"Failed to delete archive:\n{ex.Message}", "OK");
            }
        }

        private void OnPurgeOldArchives()
        {
            var settings = PCPContext.Settings;
            int retentionDays = settings != null ? settings.archiveRetentionDays : 30;

            bool confirmed = EditorUtility.DisplayDialog(
                "Purge Old Archives",
                $"Delete all archive sessions older than {retentionDays} day(s)?\n\n" +
                "This cannot be undone.",
                "Purge", "Cancel");

            if (!confirmed)
                return;

            try
            {
                int purged = PCPArchiveManager.PurgeOldSessions(retentionDays);
                RefreshSessions();

                EditorUtility.DisplayDialog("ProjectCleanPro",
                    purged > 0
                        ? $"Purged {purged} old archive session(s)."
                        : "No archive sessions were old enough to purge.",
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Purge Failed",
                    $"Failed to purge archives:\n{ex.Message}", "OK");
            }
        }
    }
}
