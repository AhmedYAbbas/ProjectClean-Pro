using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System.Text;

namespace PCPObfuscator;

/// <summary>
/// Builds a .unitypackage (tar.gz) from a directory of processed files.
///
/// .unitypackage format:
///   &lt;GUID&gt;/pathname    — text file with the Assets/... path
///   &lt;GUID&gt;/asset       — the file content
///   &lt;GUID&gt;/asset.meta  — the .meta file content
/// </summary>
public sealed class UnityPackageBuilder
{
    private readonly string _sourceRoot;
    private readonly string _projectRoot;

    /// <param name="sourceRoot">The Editor/ directory containing processed files.</param>
    /// <param name="projectRoot">The Unity project root (parent of Assets/).</param>
    public UnityPackageBuilder(string sourceRoot, string projectRoot)
    {
        _sourceRoot = Path.GetFullPath(sourceRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    /// <summary>
    /// Build the .unitypackage file.
    /// </summary>
    public void Build(string outputPath,
                      string assetBasePath,
                      Dictionary<string, string> obfuscatedFiles,
                      HashSet<string>? excludePatterns = null)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fileStream = File.Create(outputPath);
        using var gzipStream = new GZipOutputStream(fileStream);
        gzipStream.SetLevel(5);
        using var tarStream = new TarOutputStream(gzipStream, Encoding.UTF8);

        var entries = CollectEntries(assetBasePath, obfuscatedFiles, excludePatterns);

        foreach (var entry in entries)
        {
            // pathname
            WriteStringEntry(tarStream, $"{entry.Guid}/pathname", entry.AssetPath);

            // asset (file content)
            if (entry.Content != null)
                WriteStringEntry(tarStream, $"{entry.Guid}/asset", entry.Content);
            else if (entry.FilePath != null && File.Exists(entry.FilePath))
                WriteBytesEntry(tarStream, $"{entry.Guid}/asset", File.ReadAllBytes(entry.FilePath));

            // asset.meta
            if (entry.MetaContent != null)
                WriteStringEntry(tarStream, $"{entry.Guid}/asset.meta", entry.MetaContent);
            else if (entry.MetaPath != null && File.Exists(entry.MetaPath))
                WriteBytesEntry(tarStream, $"{entry.Guid}/asset.meta", File.ReadAllBytes(entry.MetaPath));
        }

        tarStream.Close();
    }

    private List<PackageEntry> CollectEntries(
        string assetBasePath,
        Dictionary<string, string> obfuscatedFiles,
        HashSet<string>? excludePatterns)
    {
        var entries = new List<PackageEntry>();
        var addedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Normalize base path
        assetBasePath = assetBasePath.Replace('\\', '/').TrimEnd('/');

        // Add folder entries up the hierarchy
        AddFolderHierarchy(entries, addedFolders, assetBasePath);

        // Walk source directory
        foreach (var filePath in Directory.EnumerateFiles(_sourceRoot, "*", SearchOption.AllDirectories))
        {
            string fullPath = Path.GetFullPath(filePath);
            string relativePath = Path.GetRelativePath(_sourceRoot, fullPath).Replace('\\', '/');

            // Skip .meta files (handled alongside their asset)
            if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip excluded patterns
            if (excludePatterns != null && IsExcluded(relativePath, excludePatterns))
                continue;

            string assetPath = $"{assetBasePath}/{relativePath}";
            string metaPath = fullPath + ".meta";

            // Extract GUID from .meta file
            string? guid = ExtractGuid(metaPath);
            if (guid == null)
            {
                Console.WriteLine($"  WARN: No .meta file or GUID for {relativePath}, skipping");
                continue;
            }

            // Add parent folder entries
            string? folderRel = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folderRel))
                AddFolderHierarchy(entries, addedFolders, $"{assetBasePath}/{folderRel}");

            // Create entry
            var entry = new PackageEntry
            {
                Guid = guid,
                AssetPath = assetPath,
                MetaPath = metaPath,
            };

            // Use obfuscated content for .cs files
            if (obfuscatedFiles.TryGetValue(relativePath, out string? obfuscatedContent))
                entry.Content = obfuscatedContent;
            else
                entry.FilePath = fullPath;

            entries.Add(entry);
        }

        return entries;
    }

    private void AddFolderHierarchy(List<PackageEntry> entries,
                                     HashSet<string> addedFolders,
                                     string folderAssetPath)
    {
        var parts = folderAssetPath.Split('/');
        for (int i = 1; i <= parts.Length; i++)
        {
            string partial = string.Join('/', parts.Take(i));
            if (addedFolders.Contains(partial))
                continue;
            addedFolders.Add(partial);

            string folderFullPath = Path.Combine(_projectRoot, partial.Replace('/', Path.DirectorySeparatorChar));
            string folderMetaPath = folderFullPath + ".meta";

            string? guid = ExtractGuid(folderMetaPath);
            if (guid == null)
                guid = GenerateDeterministicGuid(partial);

            entries.Add(new PackageEntry
            {
                Guid = guid,
                AssetPath = partial,
                MetaPath = File.Exists(folderMetaPath) ? folderMetaPath : null,
                MetaContent = File.Exists(folderMetaPath) ? null : GenerateFolderMeta(guid),
            });
        }
    }

    private static bool IsExcluded(string relativePath, HashSet<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (relativePath.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains($"/{pattern}", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? ExtractGuid(string metaFilePath)
    {
        if (!File.Exists(metaFilePath))
            return null;

        foreach (var line in File.ReadLines(metaFilePath))
        {
            if (line.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
                return line.Substring(5).Trim();
        }
        return null;
    }

    private static string GenerateDeterministicGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateFolderMeta(string guid)
    {
        return
            "fileFormatVersion: 2\n" +
            $"guid: {guid}\n" +
            "folderAsset: yes\n" +
            "DefaultImporter:\n" +
            "  externalObjects: {}\n" +
            "  userData:\n" +
            "  assetBundleName:\n" +
            "  assetBundleVariant:\n";
    }

    // ── Tar Helpers using TarOutputStream directly ──────────────

    private static void WriteStringEntry(TarOutputStream tar, string entryName, string content)
    {
        byte[] data = Encoding.UTF8.GetBytes(content);
        WriteBytesEntry(tar, entryName, data);
    }

    private static void WriteBytesEntry(TarOutputStream tar, string entryName, byte[] data)
    {
        var header = new TarHeader
        {
            Name = entryName,
            Size = data.Length,
            ModTime = DateTime.UtcNow,
            TypeFlag = TarHeader.LF_NORMAL,
        };
        var entry = new TarEntry(header);
        tar.PutNextEntry(entry);
        tar.Write(data, 0, data.Length);
        tar.CloseEntry();
    }

    private sealed class PackageEntry
    {
        public string Guid { get; set; } = "";
        public string AssetPath { get; set; } = "";
        public string? Content { get; set; }
        public string? FilePath { get; set; }
        public string? MetaPath { get; set; }
        public string? MetaContent { get; set; }
    }
}
