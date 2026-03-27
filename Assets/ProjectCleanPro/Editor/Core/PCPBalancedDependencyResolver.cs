using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Balanced mode: GUID parsing for simple assets (background), AssetDatabase for complex
    /// types (.prefab, .unity) that have variant inheritance or implicit deps.
    /// Both run concurrently — background threads parse files while main thread handles AssetDatabase.
    /// </summary>
    internal sealed class PCPBalancedDependencyResolver : PCPDependencyResolverBase
    {
        private readonly PCPGuidIndex m_GuidIndex;

        private static readonly HashSet<string> k_ComplexExtensions = new()
        {
            ".prefab", ".unity"
        };

        public PCPBalancedDependencyResolver(PCPGuidIndex guidIndex)
        {
            m_GuidIndex = guidIndex;
        }

        public override async Task BuildGraphAsync(PCPScanContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var scheduler = context.Scheduler;

            if (!m_IsBuilt)
                LoadFromDisk();

            // Step 1: Build GUID index (background)
            var metaFiles = await context.GetAllMetaFilesAsync(ct);
            var changedFiles = context.Cache.HasAnyChanges
                ? new HashSet<string>(context.Cache.GetStaleAssets()) as IReadOnlySet<string>
                : null;
            await m_GuidIndex.BuildAsync(metaFiles, changedFiles, ct);

            // Step 2: Classify stale assets
            var stalePaths = context.Cache.GetStaleAssets()
                .Where(p => !p.EndsWith(".meta"))
                .ToList();

            var simple = stalePaths
                .Where(p => !k_ComplexExtensions.Contains(Path.GetExtension(p))
                            && PCPGuidParser.IsGuidParseable(p))
                .ToList();

            var complex = stalePaths
                .Where(p => k_ComplexExtensions.Contains(Path.GetExtension(p)))
                .ToList();

            // Step 3: Run both in parallel
            // Simple: GUID parse on background threads
            var simpleResults = new ConcurrentDictionary<string, HashSet<string>>();
            var simpleTask = PCPThreading.ParallelForEachAsync(simple, async (path, token) =>
            {
                try
                {
                    var guids = await PCPGuidParser.ParseReferencesAsync(path, token);
                    simpleResults[path] = m_GuidIndex.ResolveAll(guids);
                }
                catch (IOException) { }
                catch (System.UnauthorizedAccessException) { }
            }, PCPThreading.DefaultConcurrency, ct);

            // Complex: AssetDatabase on main thread (frame-budgeted)
            Task<List<string[]>> complexTask;
            if (complex.Count > 0)
            {
                complexTask = scheduler.BatchOnMainThread(
                    complex,
                    path => AssetDatabase.GetDependencies(path, false),
                    ct);
            }
            else
            {
                complexTask = Task.FromResult(new List<string[]>());
            }

            await Task.WhenAll(simpleTask, complexTask);

            // Step 4: Merge into graph
            foreach (var (asset, deps) in simpleResults)
                UpdateEdges(asset, deps);

            var complexDeps = complexTask.Result;
            for (int i = 0; i < complex.Count; i++)
                UpdateEdges(complex[i], complexDeps[i]);

            // Register all assets
            var allAssets = await context.GetAllProjectAssetsAsync(ct);
            foreach (var asset in allAssets)
                m_AllAssets[asset] = 0;

            // Step 5: BFS reachability
            ct.ThrowIfCancellationRequested();
            var roots = CollectRoots(context);
            ComputeReachability(roots);

            m_IsBuilt = true;
            SaveToDisk();
        }
    }
}
