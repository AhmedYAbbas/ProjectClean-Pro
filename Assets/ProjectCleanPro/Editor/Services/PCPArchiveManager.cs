using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    // ----------------------------------------------------------------
    // Archive data types
    // ----------------------------------------------------------------

    /// <summary>
    /// Represents a single archived file entry within an <see cref="PCPArchiveSession"/>.
    /// </summary>
    [Serializable]
    public sealed class PCPArchiveEntry
    {
        /// <summary>Original project-relative asset path (e.g. "Assets/Textures/Hero.png").</summary>
        public string originalPath;

        /// <summary>Relative path within the archive folder.</summary>
        public string archivedRelativePath;

        /// <summary>File size in bytes at time of archival.</summary>
        public long sizeBytes;
    }

    /// <summary>
    /// Manifest data written as manifest.json inside each archive session folder.
    /// </summary>
    [Serializable]
    public sealed class PCPArchiveManifest
    {
        /// <summary>Unique session identifier (timestamp-based).</summary>
        public string sessionId;

        /// <summary>UTC timestamp when the archive was created (ISO 8601).</summary>
        public string createdAtUtc;

        /// <summary>Total number of archived files.</summary>
        public int fileCount;

        /// <summary>Total size of all archived files in bytes.</summary>
        public long totalSizeBytes;

        /// <summary>Individual file entries.</summary>
        public List<PCPArchiveEntry> entries = new List<PCPArchiveEntry>();
    }

    /// <summary>
    /// Runtime representation of an archive session, used for listing, restoring, and purging.
    /// </summary>
    [Serializable]
    public sealed class PCPArchiveSession
    {
        /// <summary>Unique session identifier (folder name).</summary>
        public string sessionId;

        /// <summary>Absolute path to the session folder on disk.</summary>
        public string archivePath;

        /// <summary>UTC time the archive was created.</summary>
        public DateTime createdAtUtc;

        /// <summary>Number of assets in this session.</summary>
        public int assetCount;

        /// <summary>Total size of archived assets in bytes.</summary>
        public long totalSizeBytes;

        /// <summary>Human-readable total size.</summary>
        public string formattedTotalSize;

        /// <summary>List of original asset paths that were archived.</summary>
        public List<string> archivedPaths = new List<string>();
    }

    // ----------------------------------------------------------------
    // Archive manager
    // ----------------------------------------------------------------

    /// <summary>
    /// Manages the .pcp_archive/ directory at the project root, providing
    /// session creation, listing, restoration, and purging.
    /// </summary>
    public static class PCPArchiveManager
    {
        private const string ArchiveFolderName = ".pcp_archive";
        private const string ManifestFileName = "manifest.json";

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the absolute path to the archive root directory (.pcp_archive/ at project root).
        /// Creates the directory if it does not exist.
        /// </summary>
        public static string GetArchiveRoot()
        {
            string projectRoot = PCPAssetUtils.GetProjectRoot();
            string archiveRoot = Path.Combine(projectRoot, ArchiveFolderName);
            if (!Directory.Exists(archiveRoot))
            {
                Directory.CreateDirectory(archiveRoot);

                // Write a .gitignore so archives are not committed by default.
                string gitignorePath = Path.Combine(archiveRoot, ".gitignore");
                if (!File.Exists(gitignorePath))
                {
                    File.WriteAllText(gitignorePath, "# ProjectCleanPro archive data\n*\n");
                }
            }
            return archiveRoot;
        }

        /// <summary>
        /// Creates a new archive session by copying the specified assets (and their .meta files)
        /// to a timestamped folder under the archive root.
        /// </summary>
        /// <param name="assetPaths">Project-relative paths of assets to archive.</param>
        /// <returns>The newly created <see cref="PCPArchiveSession"/>.</returns>
        public static PCPArchiveSession CreateSession(List<string> assetPaths)
        {
            if (assetPaths == null || assetPaths.Count == 0)
                throw new ArgumentException("No asset paths provided.", nameof(assetPaths));

            string archiveRoot = GetArchiveRoot();
            DateTime now = DateTime.UtcNow;
            string sessionId = now.ToString("yyyyMMdd_HHmmss_fff");
            string sessionPath = Path.Combine(archiveRoot, sessionId);
            Directory.CreateDirectory(sessionPath);

            string projectRoot = PCPAssetUtils.GetProjectRoot();
            var manifest = new PCPArchiveManifest
            {
                sessionId = sessionId,
                createdAtUtc = now.ToString("o"),
                entries = new List<PCPArchiveEntry>()
            };

            long totalSize = 0L;
            var archivedPaths = new List<string>();

            foreach (string assetPath in assetPaths)
            {
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                string sourceFullPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(sourceFullPath))
                {
                    Debug.LogWarning($"[ProjectCleanPro] Archive skipped (file not found): {assetPath}");
                    continue;
                }

                // Copy the asset file preserving relative path structure.
                string relativePath = assetPath.Replace('/', Path.DirectorySeparatorChar);
                string destPath = Path.Combine(sessionPath, relativePath);
                PCPFileUtils.CopyFileWithDirectories(sourceFullPath, destPath);

                long fileSize = new FileInfo(sourceFullPath).Length;
                totalSize += fileSize;

                manifest.entries.Add(new PCPArchiveEntry
                {
                    originalPath = assetPath,
                    archivedRelativePath = relativePath,
                    sizeBytes = fileSize
                });

                archivedPaths.Add(assetPath);

                // Also copy the .meta file if it exists.
                string metaSource = sourceFullPath + ".meta";
                if (File.Exists(metaSource))
                {
                    string metaDest = destPath + ".meta";
                    PCPFileUtils.CopyFileWithDirectories(metaSource, metaDest);
                }
            }

            manifest.fileCount = manifest.entries.Count;
            manifest.totalSizeBytes = totalSize;

            // Write manifest.json
            string manifestPath = Path.Combine(sessionPath, ManifestFileName);
            string json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(manifestPath, json);

            return new PCPArchiveSession
            {
                sessionId = sessionId,
                archivePath = sessionPath,
                createdAtUtc = now,
                assetCount = manifest.fileCount,
                totalSizeBytes = totalSize,
                formattedTotalSize = PCPAssetUtils.FormatSize(totalSize),
                archivedPaths = archivedPaths
            };
        }

        /// <summary>
        /// Lists all archive sessions found under the archive root, sorted newest first.
        /// </summary>
        public static List<PCPArchiveSession> GetAllSessions()
        {
            var sessions = new List<PCPArchiveSession>();
            string archiveRoot = GetArchiveRoot();

            if (!Directory.Exists(archiveRoot))
                return sessions;

            string[] directories = Directory.GetDirectories(archiveRoot);
            foreach (string dir in directories)
            {
                string manifestPath = Path.Combine(dir, ManifestFileName);
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    string json = File.ReadAllText(manifestPath);
                    var manifest = JsonUtility.FromJson<PCPArchiveManifest>(json);

                    DateTime createdAt = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(manifest.createdAtUtc))
                    {
                        DateTime.TryParse(manifest.createdAtUtc, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out createdAt);
                    }

                    var archivedPaths = new List<string>();
                    if (manifest.entries != null)
                    {
                        foreach (var entry in manifest.entries)
                            archivedPaths.Add(entry.originalPath);
                    }

                    sessions.Add(new PCPArchiveSession
                    {
                        sessionId = manifest.sessionId,
                        archivePath = dir,
                        createdAtUtc = createdAt,
                        assetCount = manifest.fileCount,
                        totalSizeBytes = manifest.totalSizeBytes,
                        formattedTotalSize = PCPAssetUtils.FormatSize(manifest.totalSizeBytes),
                        archivedPaths = archivedPaths
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ProjectCleanPro] Failed to read archive manifest at {dir}: {ex.Message}");
                }
            }

            // Sort by creation time descending (newest first).
            sessions.Sort((a, b) => b.createdAtUtc.CompareTo(a.createdAtUtc));
            return sessions;
        }

        /// <summary>
        /// Restores all files from the specified archive session back to their original
        /// project-relative locations.
        /// </summary>
        /// <param name="sessionId">The session identifier (folder name) to restore.</param>
        public static void RestoreSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

            string archiveRoot = GetArchiveRoot();
            string sessionPath = Path.Combine(archiveRoot, sessionId);

            if (!Directory.Exists(sessionPath))
                throw new DirectoryNotFoundException($"Archive session not found: {sessionId}");

            string manifestPath = Path.Combine(sessionPath, ManifestFileName);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException($"Manifest not found for session: {sessionId}");

            string json = File.ReadAllText(manifestPath);
            var manifest = JsonUtility.FromJson<PCPArchiveManifest>(json);

            string projectRoot = PCPAssetUtils.GetProjectRoot();
            int restoredCount = 0;

            foreach (var entry in manifest.entries)
            {
                string archivedFilePath = Path.Combine(sessionPath,
                    entry.archivedRelativePath);
                string originalFullPath = Path.Combine(projectRoot,
                    entry.originalPath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(archivedFilePath))
                {
                    Debug.LogWarning($"[ProjectCleanPro] Archived file missing, cannot restore: " +
                                     $"{entry.originalPath}");
                    continue;
                }

                // Restore the asset file.
                PCPFileUtils.CopyFileWithDirectories(archivedFilePath, originalFullPath);
                restoredCount++;

                // Restore the .meta file if archived.
                string archivedMeta = archivedFilePath + ".meta";
                if (File.Exists(archivedMeta))
                {
                    File.Copy(archivedMeta, originalFullPath + ".meta", true);
                }
            }

            Debug.Log($"[ProjectCleanPro] Restored {restoredCount}/{manifest.fileCount} file(s) " +
                      $"from session '{sessionId}'.");
        }

        /// <summary>
        /// Purges archive sessions older than the specified retention period.
        /// </summary>
        /// <param name="retentionDays">Number of days to retain. Sessions older than this are deleted.</param>
        /// <returns>Number of sessions purged.</returns>
        public static int PurgeOldSessions(int retentionDays)
        {
            if (retentionDays < 1)
                throw new ArgumentOutOfRangeException(nameof(retentionDays), "Must be at least 1.");

            DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var sessions = GetAllSessions();
            int purgedCount = 0;

            foreach (var session in sessions)
            {
                if (session.createdAtUtc < cutoff)
                {
                    DeleteSession(session.sessionId);
                    purgedCount++;
                }
            }

            if (purgedCount > 0)
            {
                Debug.Log($"[ProjectCleanPro] Purged {purgedCount} archive session(s) " +
                          $"older than {retentionDays} day(s).");
            }

            return purgedCount;
        }

        // ----------------------------------------------------------------
        // Auto-purge on editor startup
        // ----------------------------------------------------------------

        /// <summary>
        /// Automatically purges expired archive sessions once per editor session.
        /// Runs silently on domain reload — no dialogs or user interaction.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoPurgeOnStartup()
        {
            const string sessionKey = "PCP_AutoPurgeRan";
            if (SessionState.GetBool(sessionKey, false))
                return;

            SessionState.SetBool(sessionKey, true);

            // Delay so that ScriptableSingleton settings are ready.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var settings = PCPContext.Settings;
                    if (settings == null)
                        return;

                    int retentionDays = settings.archiveRetentionDays;
                    if (retentionDays < 1)
                        return;

                    int purged = PurgeOldSessions(retentionDays);
                    if (purged > 0)
                    {
                        Debug.Log($"[ProjectCleanPro] Auto-purged {purged} expired archive session(s) " +
                                  $"(retention: {retentionDays} days).");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ProjectCleanPro] Auto-purge failed: {ex.Message}");
                }
            };
        }

        /// <summary>
        /// Deletes a specific archive session and all its files from disk.
        /// </summary>
        /// <param name="sessionId">The session identifier to delete.</param>
        public static void DeleteSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

            string archiveRoot = GetArchiveRoot();
            string sessionPath = Path.Combine(archiveRoot, sessionId);

            if (!Directory.Exists(sessionPath))
            {
                Debug.LogWarning($"[ProjectCleanPro] Archive session not found: {sessionId}");
                return;
            }

            try
            {
                Directory.Delete(sessionPath, true);
                Debug.Log($"[ProjectCleanPro] Deleted archive session '{sessionId}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectCleanPro] Failed to delete archive session '{sessionId}': " +
                               $"{ex.Message}");
            }
        }
    }
}
