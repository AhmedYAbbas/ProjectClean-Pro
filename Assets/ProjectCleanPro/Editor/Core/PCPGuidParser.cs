using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Extracts GUID references from Unity YAML-serialized asset files.
    /// Uses streaming line-by-line reading to avoid loading large files into memory.
    /// Used by Fast and Balanced scan modes.
    /// </summary>
    internal static class PCPGuidParser
    {
        private const string k_GuidPrefix = "guid: ";
        private const int k_GuidLength = 32;

        /// <summary>
        /// Reads a file line-by-line and extracts all GUID references.
        /// Uses StreamReader for constant memory usage regardless of file size.
        /// </summary>
        public static async Task<HashSet<string>> ParseReferencesAsync(
            string filePath, CancellationToken ct)
        {
            var guids = new HashSet<string>();

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 65536, useAsync: true);
            using var reader = new StreamReader(stream);

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();
                ExtractGuidsFromLine(line, guids);
            }

            return guids;
        }

        private static void ExtractGuidsFromLine(string line, HashSet<string> guids)
        {
            int searchFrom = 0;
            while (true)
            {
                int idx = line.IndexOf(k_GuidPrefix, searchFrom, StringComparison.Ordinal);
                if (idx < 0) break;

                int guidStart = idx + k_GuidPrefix.Length;
                if (guidStart + k_GuidLength <= line.Length)
                {
                    var candidate = line.Substring(guidStart, k_GuidLength);
                    if (IsHexString(candidate))
                        guids.Add(candidate);
                }
                searchFrom = guidStart + k_GuidLength;
            }
        }

        private static bool IsHexString(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if the file extension indicates a YAML-serialized Unity asset
        /// that may contain GUID references.
        /// </summary>
        public static bool IsGuidParseable(string extensionOrPath)
        {
            var ext = extensionOrPath.StartsWith(".")
                ? extensionOrPath
                : Path.GetExtension(extensionOrPath);

            return ext is ".prefab" or ".unity" or ".asset" or ".mat"
                or ".controller" or ".anim" or ".overrideController"
                or ".lighting" or ".playable" or ".signal"
                or ".spriteatlasv2" or ".spriteatlas" or ".terrainlayer"
                or ".mixer" or ".renderTexture" or ".flare";
        }
    }
}
