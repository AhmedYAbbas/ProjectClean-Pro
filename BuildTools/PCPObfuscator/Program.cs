using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;

namespace PCPObfuscator;

class Program
{
    static int Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options == null)
        {
            PrintUsage();
            return 1;
        }

        var sw = Stopwatch.StartNew();
        Console.WriteLine("=== ProjectClean Pro — Source Obfuscator ===");
        Console.WriteLine();

        // ── Step 1: Discover source files ──
        Console.WriteLine($"[1/5] Discovering source files in: {options.SourceDir}");
        if (!Directory.Exists(options.SourceDir))
        {
            Console.Error.WriteLine($"ERROR: Source directory not found: {options.SourceDir}");
            return 1;
        }

        var csFiles = Directory.GetFiles(options.SourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f, options.SourceDir, options.Excludes))
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine($"  Found {csFiles.Count} C# files");

        int nonCsFiles = Directory.GetFiles(options.SourceDir, "*", SearchOption.AllDirectories)
            .Count(f => !f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     && !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                     && !IsExcluded(f, options.SourceDir, options.Excludes));
        Console.WriteLine($"  Found {nonCsFiles} non-C# asset files (.asmdef, etc.)");
        Console.WriteLine();

        // ── Step 2: Parse all files ──
        Console.WriteLine("[2/5] Parsing C# source files with Roslyn...");
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(options.Defines);

        var trees = new List<(string RelativePath, SyntaxTree Tree)>();
        int parseErrors = 0;

        foreach (var file in csFiles)
        {
            string source = File.ReadAllText(file);
            string relativePath = Path.GetRelativePath(options.SourceDir, file).Replace('\\', '/');
            var tree = CSharpSyntaxTree.ParseText(source, parseOptions, file);

            var diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (diagnostics.Count > 0)
            {
                parseErrors += diagnostics.Count;
                foreach (var d in diagnostics.Take(3))
                    Console.WriteLine($"  WARN: Parse issue in {relativePath}: {d.GetMessage()}");
            }

            trees.Add((relativePath, tree));
        }

        if (parseErrors > 0)
            Console.WriteLine($"  {parseErrors} parse warnings (expected without Unity DLL refs, safe to proceed)");
        Console.WriteLine($"  Parsed {trees.Count} files successfully");
        Console.WriteLine();

        // ── Step 3: Obfuscate ──
        Console.WriteLine("[3/5] Obfuscating identifiers...");
        var rewriter = new ObfuscationRewriter();

        // Pass 1: Collect renameable identifiers
        rewriter.Collect(trees.Select(t => t.Tree));

        // Pass 2: Rewrite each tree
        var obfuscatedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, tree) in trees)
        {
            var rewritten = rewriter.Rewrite(tree);
            string obfuscatedSource = rewritten.GetRoot().NormalizeWhitespace().ToFullString();
            obfuscatedFiles[relativePath] = obfuscatedSource;
        }

        Console.WriteLine($"  Identifiers renamed:   {rewriter.IdentifiersRenamed}");
        Console.WriteLine($"  Identifiers preserved: {rewriter.IdentifiersPreserved}");
        Console.WriteLine();

        // ── Step 4: Build .unitypackage ──
        Console.WriteLine("[4/5] Building .unitypackage...");
        var builder = new UnityPackageBuilder(options.SourceDir, options.ProjectRoot);

        var excludeSet = new HashSet<string>(options.Excludes, StringComparer.OrdinalIgnoreCase);

        builder.Build(
            outputPath: options.OutputPath,
            assetBasePath: options.AssetBasePath,
            obfuscatedFiles: obfuscatedFiles,
            excludePatterns: excludeSet
        );

        var fileInfo = new FileInfo(options.OutputPath);
        Console.WriteLine($"  Output: {options.OutputPath}");
        Console.WriteLine($"  Size:   {FormatSize(fileInfo.Length)}");
        Console.WriteLine();

        // ── Step 5: Summary ──
        sw.Stop();
        Console.WriteLine("[5/5] Complete!");
        Console.WriteLine();
        Console.WriteLine("  Summary:");
        Console.WriteLine($"    Source files:         {csFiles.Count}");
        Console.WriteLine($"    Identifiers renamed:  {rewriter.IdentifiersRenamed}");
        Console.WriteLine($"    Identifiers preserved:{rewriter.IdentifiersPreserved}");
        Console.WriteLine($"    Package size:         {FormatSize(fileInfo.Length)}");
        Console.WriteLine($"    Time:                 {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine();
        Console.WriteLine($"  Package ready at: {Path.GetFullPath(options.OutputPath)}");

        return 0;
    }

    // ── Argument Parsing ────────────────────────────────────────

    private sealed class Options
    {
        public string SourceDir { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string ProjectRoot { get; set; } = "";
        public string AssetBasePath { get; set; } = "Assets/ProjectCleanPro/Editor";
        public List<string> Excludes { get; set; } = new() { "Tests" };
        public List<string> Defines { get; set; } = new();
    }

    private static Options? ParseArgs(string[] args)
    {
        var options = new Options();

        // Determine project root from current directory or explicit arg
        string scriptDir = AppContext.BaseDirectory;
        // Default: assume we're in BuildTools/PCPObfuscator/bin/...
        // Walk up to find the project root (contains Assets/)
        string? projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        if (projectRoot != null)
            options.ProjectRoot = projectRoot;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--source":
                    if (i + 1 < args.Length) options.SourceDir = args[++i];
                    break;
                case "--output":
                    if (i + 1 < args.Length) options.OutputPath = args[++i];
                    break;
                case "--project-root":
                    if (i + 1 < args.Length) options.ProjectRoot = args[++i];
                    break;
                case "--asset-base":
                    if (i + 1 < args.Length) options.AssetBasePath = args[++i];
                    break;
                case "--exclude":
                    if (i + 1 < args.Length)
                        options.Excludes.AddRange(args[++i].Split(','));
                    break;
                case "--define":
                    if (i + 1 < args.Length)
                        options.Defines.AddRange(args[++i].Split(','));
                    break;
                case "--version":
                    if (i + 1 < args.Length)
                    {
                        string ver = args[++i];
                        if (string.IsNullOrEmpty(options.OutputPath))
                            options.OutputPath = $"Builds/ProjectCleanPro_v{ver}.unitypackage";
                    }
                    break;
                case "--help":
                case "-h":
                    return null;
            }
        }

        // Defaults
        if (string.IsNullOrEmpty(options.ProjectRoot))
        {
            Console.Error.WriteLine("ERROR: Could not detect project root. Use --project-root.");
            return null;
        }

        if (string.IsNullOrEmpty(options.SourceDir))
            options.SourceDir = Path.Combine(options.ProjectRoot, "Assets", "ProjectCleanPro", "Editor");

        if (string.IsNullOrEmpty(options.OutputPath))
            options.OutputPath = Path.Combine(options.ProjectRoot, "Builds", "ProjectCleanPro_v1.0_beta.unitypackage");

        return options;
    }

    private static string? FindProjectRoot(string startDir)
    {
        string? dir = startDir;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "Assets")) &&
                Directory.Exists(Path.Combine(dir, "ProjectSettings")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private static bool IsExcluded(string filePath, string sourceDir, List<string> excludes)
    {
        string relative = Path.GetRelativePath(sourceDir, filePath).Replace('\\', '/');
        foreach (var pattern in excludes)
        {
            if (relative.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                relative.Contains($"/{pattern}", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains($"/{pattern}/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            ProjectClean Pro — Source Obfuscator

            Usage:
              PCPObfuscator [options]

            Options:
              --source <path>        Editor source directory (default: Assets/ProjectCleanPro/Editor)
              --output <path>        Output .unitypackage path (default: Builds/ProjectCleanPro_v1.0_beta.unitypackage)
              --project-root <path>  Unity project root (auto-detected)
              --asset-base <path>    Asset path prefix (default: Assets/ProjectCleanPro/Editor)
              --version <semver>     Version string for output filename
              --exclude <patterns>   Comma-separated exclusion patterns (default: Tests)
              --define <symbols>     Comma-separated preprocessor defines
              --help, -h             Show this help

            Examples:
              dotnet run -- --version 1.0.0-beta
              dotnet run -- --source ./Assets/ProjectCleanPro/Editor --output ./Builds/PCP.unitypackage
            """);
    }
}
