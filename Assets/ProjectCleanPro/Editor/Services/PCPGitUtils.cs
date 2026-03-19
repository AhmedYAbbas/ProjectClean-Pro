using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Utility class for interacting with the Git repository at the project root.
    /// Provides detection, tracking queries, and batch <c>git rm</c> operations.
    /// </summary>
    public static class PCPGitUtils
    {
        private const int GitTimeoutMs = 30000; // 30 seconds

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the project root directory (parent of Application.dataPath / Assets).
        /// </summary>
        public static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        /// <summary>
        /// Checks whether the project root contains a .git folder (or file for worktrees).
        /// </summary>
        /// <returns><c>true</c> if a Git repository is detected at the project root.</returns>
        public static bool IsGitRepository()
        {
            string projectRoot = GetProjectRoot();
            string gitDir = Path.Combine(projectRoot, ".git");
            // .git can be a directory (normal repo) or a file (worktree/submodule).
            return Directory.Exists(gitDir) || File.Exists(gitDir);
        }

        /// <summary>
        /// Checks whether the given asset path is tracked by Git.
        /// </summary>
        /// <param name="assetPath">Project-relative path (e.g. "Assets/Textures/Hero.png").</param>
        /// <returns><c>true</c> if the file is tracked by Git.</returns>
        public static bool IsTracked(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !IsGitRepository())
                return false;

            // git ls-files exits with code 0 and prints the filename if tracked.
            string output = RunGitCommand($"ls-files \"{assetPath}\"");
            return !string.IsNullOrWhiteSpace(output);
        }

        /// <summary>
        /// Removes a single file from the Git index and working tree.
        /// Equivalent to <c>git rm --cached &lt;path&gt;</c> (keeps the file in the working
        /// tree for our archive step, but removes it from Git tracking).
        /// The file will be physically deleted separately by the caller.
        /// </summary>
        /// <param name="assetPath">Project-relative path to remove.</param>
        public static void GitRm(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            string result = RunGitCommand($"rm --cached \"{assetPath}\"");
            if (result == null)
            {
                Debug.LogWarning($"[ProjectCleanPro] git rm failed for: {assetPath}");
            }

            // Also remove the .meta file if tracked.
            string metaPath = assetPath + ".meta";
            if (IsTracked(metaPath))
            {
                RunGitCommand($"rm --cached \"{metaPath}\"");
            }
        }

        /// <summary>
        /// Batch-removes multiple files from the Git index.
        /// Uses <c>git rm --cached</c> with all paths in a single invocation for efficiency.
        /// </summary>
        /// <param name="assetPaths">List of project-relative paths to remove.</param>
        public static void GitRmMultiple(List<string> assetPaths)
        {
            if (assetPaths == null || assetPaths.Count == 0)
                return;

            // Build the argument list. Include .meta files for each asset.
            var allPaths = new List<string>();
            foreach (string path in assetPaths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                allPaths.Add(path);
                string metaPath = path + ".meta";
                if (IsTracked(metaPath))
                    allPaths.Add(metaPath);
            }

            if (allPaths.Count == 0)
                return;

            // Process in batches to avoid exceeding command-line length limits.
            const int batchSize = 50;
            for (int i = 0; i < allPaths.Count; i += batchSize)
            {
                var batch = allPaths.GetRange(i, Math.Min(batchSize, allPaths.Count - i));
                var args = new StringBuilder("rm --cached --");
                foreach (string path in batch)
                {
                    args.Append($" \"{path}\"");
                }

                string result = RunGitCommand(args.ToString());
                if (result == null)
                {
                    Debug.LogWarning(
                        $"[ProjectCleanPro] git rm batch failed for {batch.Count} file(s) " +
                        $"starting at index {i}.");
                }
            }
        }

        // ----------------------------------------------------------------
        // Internal helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Runs a git command at the project root and returns stdout.
        /// Returns <c>null</c> if the command fails or times out.
        /// </summary>
        private static string RunGitCommand(string arguments)
        {
            string projectRoot = GetProjectRoot();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = psi })
                {
                    var stdout = new StringBuilder();
                    var stderr = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                            stdout.AppendLine(e.Data);
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                            stderr.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(GitTimeoutMs))
                    {
                        try { process.Kill(); }
                        catch { /* best effort */ }
                        Debug.LogWarning($"[ProjectCleanPro] Git command timed out: git {arguments}");
                        return null;
                    }

                    // Ensure async reads complete.
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        string err = stderr.ToString().Trim();
                        if (!string.IsNullOrEmpty(err))
                        {
                            Debug.LogWarning($"[ProjectCleanPro] Git error (exit {process.ExitCode}): {err}");
                        }
                        return null;
                    }

                    return stdout.ToString().TrimEnd();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to run git command: {ex.Message}");
                return null;
            }
        }
    }
}
