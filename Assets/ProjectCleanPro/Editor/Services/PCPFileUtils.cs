using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// File-system utility methods for ProjectCleanPro, including SHA-256 hashing,
    /// size/date queries, and path helpers.
    /// </summary>
    public static class PCPFileUtils
    {
        // ----------------------------------------------------------------
        // Hashing
        // ----------------------------------------------------------------

        /// <summary>
        /// Computes the SHA-256 hash of a file using a streaming approach for
        /// memory efficiency on large files.
        /// </summary>
        /// <param name="filePath">Absolute path to the file.</param>
        /// <returns>Lowercase hexadecimal hash string (64 characters).</returns>
        /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
        public static string ComputeSHA256(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}", filePath);

            using (var sha256 = SHA256.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                       FileShare.Read, bufferSize: 81920))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                return BytesToHex(hashBytes);
            }
        }

        // ----------------------------------------------------------------
        // File metadata
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the size of a file in bytes, or 0 if the file does not exist.
        /// </summary>
        /// <param name="filePath">Absolute path to the file.</param>
        public static long GetFileSize(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return 0L;

            return new FileInfo(filePath).Length;
        }

        /// <summary>
        /// Returns the last write time of a file in UTC, or <see cref="DateTime.MinValue"/>
        /// if the file does not exist.
        /// </summary>
        /// <param name="filePath">Absolute path to the file.</param>
        public static DateTime GetLastModified(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return DateTime.MinValue;

            return File.GetLastWriteTimeUtc(filePath);
        }

        // ----------------------------------------------------------------
        // File operations
        // ----------------------------------------------------------------

        /// <summary>
        /// Copies a file from <paramref name="source"/> to <paramref name="destination"/>,
        /// creating any intermediate directories that do not exist.
        /// Overwrites the destination if it already exists.
        /// </summary>
        /// <param name="source">Absolute path of the source file.</param>
        /// <param name="destination">Absolute path of the destination file.</param>
        public static void CopyFileWithDirectories(string source, string destination)
        {
            if (string.IsNullOrEmpty(source))
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrEmpty(destination))
                throw new ArgumentNullException(nameof(destination));

            string destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(source, destination, true);
        }

        /// <summary>
        /// Creates the specified directory if it does not already exist.
        /// </summary>
        /// <param name="path">Absolute directory path.</param>
        public static void EnsureDirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// Computes a relative path from a base directory to a full (absolute) file path.
        /// </summary>
        /// <param name="basePath">The base directory path (absolute).</param>
        /// <param name="fullPath">The target file path (absolute).</param>
        /// <returns>
        /// A relative path from <paramref name="basePath"/> to <paramref name="fullPath"/>,
        /// using forward slashes as separators.
        /// </returns>
        public static string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath))
                throw new ArgumentNullException(nameof(basePath));
            if (string.IsNullOrEmpty(fullPath))
                throw new ArgumentNullException(nameof(fullPath));

            // Normalize separators and ensure the base ends with a separator.
            string normalizedBase = basePath.Replace('\\', '/').TrimEnd('/') + "/";
            string normalizedFull = fullPath.Replace('\\', '/');

            // Fast path: if the full path starts with the base path, just strip the prefix.
            if (normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFull.Substring(normalizedBase.Length);
            }

            // General case: use Uri-based relative path computation.
            try
            {
                var baseUri = new Uri(normalizedBase);
                var fullUri = new Uri(normalizedFull);
                Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
                return Uri.UnescapeDataString(relativeUri.ToString());
            }
            catch (UriFormatException)
            {
                // Fallback: return the full path as-is.
                return normalizedFull;
            }
        }

        // ----------------------------------------------------------------
        // Internal helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Converts a byte array to a lowercase hexadecimal string.
        /// </summary>
        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
