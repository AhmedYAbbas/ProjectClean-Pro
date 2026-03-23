using System;
using System.IO;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Low-level binary I/O utilities for all ProjectCleanPro cache files.
    /// Provides atomic writes (write-to-temp-then-rename) and safe reads
    /// with graceful version-mismatch handling.
    /// </summary>
    public static class PCPCacheIO
    {
        /// <summary>Magic bytes identifying a PCP cache file.</summary>
        public static readonly byte[] Magic = { (byte)'P', (byte)'C', (byte)'P', 0 };

        // ----------------------------------------------------------------
        // Atomic write
        // ----------------------------------------------------------------

        /// <summary>
        /// Atomically writes a binary cache file. The payload is written to a
        /// temporary file first; only after a successful write is the temporary
        /// file renamed to the target path. If the editor crashes mid-write,
        /// the previous valid file remains intact.
        /// </summary>
        /// <param name="targetPath">Final file path (e.g. ScanCache.bin).</param>
        /// <param name="version">Format version written into the header.</param>
        /// <param name="writePayload">
        /// Callback that writes the payload. The <see cref="BinaryWriter"/>
        /// position is immediately after the header when this is called.
        /// </param>
        public static void AtomicWrite(string targetPath, int version, Action<BinaryWriter> writePayload)
        {
            if (writePayload == null)
                throw new ArgumentNullException(nameof(writePayload));

            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tempPath = targetPath + ".tmp";

            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fs))
                {
                    // Header: magic + version
                    writer.Write(Magic);
                    writer.Write(version);

                    // Payload
                    writePayload(writer);
                }

                // Atomic rename (same volume)
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to write cache file '{targetPath}': {ex.Message}");
                CleanupTemp(tempPath);
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Safe read
        // ----------------------------------------------------------------

        /// <summary>
        /// Safely reads a binary cache file. Returns <c>default</c> if the file
        /// is missing, has an invalid header, or a version mismatch.
        /// On any deserialization error the file is left on disk untouched and
        /// <c>default(T)</c> is returned so the caller can rebuild from scratch.
        /// </summary>
        /// <typeparam name="T">Return type produced by the reader callback.</typeparam>
        /// <param name="filePath">Path to the .bin file.</param>
        /// <param name="expectedVersion">Expected format version.</param>
        /// <param name="readPayload">
        /// Callback that reads the payload. The <see cref="BinaryReader"/>
        /// position is immediately after the header when this is called.
        /// </param>
        /// <param name="result">The deserialized value, or <c>default</c> on failure.</param>
        /// <returns><c>true</c> if the file was read successfully.</returns>
        public static bool SafeRead<T>(string filePath, int expectedVersion, Func<BinaryReader, T> readPayload, out T result)
        {
            result = default;

            if (!File.Exists(filePath))
                return false;

            // Clean up orphaned .tmp files from a previous crash
            CleanupTemp(filePath + ".tmp");

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    // Validate magic bytes
                    byte[] magic = reader.ReadBytes(Magic.Length);
                    if (magic.Length != Magic.Length)
                        return false;

                    for (int i = 0; i < Magic.Length; i++)
                    {
                        if (magic[i] != Magic[i])
                            return false;
                    }

                    // Validate version
                    int version = reader.ReadInt32();
                    if (version != expectedVersion)
                    {
                        Debug.Log($"[ProjectCleanPro] Cache version mismatch in '{filePath}' " +
                                  $"(found {version}, expected {expectedVersion}). Discarding.");
                        return false;
                    }

                    result = readPayload(reader);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectCleanPro] Failed to read cache file '{filePath}': {ex.Message}");
                return false;
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Writes a string that may be null. Uses a bool prefix to distinguish
        /// null from empty.
        /// </summary>
        public static void WriteNullableString(BinaryWriter writer, string value)
        {
            bool hasValue = value != null;
            writer.Write(hasValue);
            if (hasValue)
                writer.Write(value);
        }

        /// <summary>
        /// Reads a string that may be null (written by <see cref="WriteNullableString"/>).
        /// </summary>
        public static string ReadNullableString(BinaryReader reader)
        {
            bool hasValue = reader.ReadBoolean();
            return hasValue ? reader.ReadString() : null;
        }

        /// <summary>
        /// Deletes a file if it exists. Swallows exceptions.
        /// </summary>
        private static void CleanupTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
