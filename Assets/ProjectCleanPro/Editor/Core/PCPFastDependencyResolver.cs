using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Fast mode: pure GUID parsing from file content. Fully background — no main-thread work.
    /// Builds dependency graph by reading .meta files for GUID→path mapping,
    /// then parsing YAML assets for GUID references.
    /// </summary>
    internal sealed class PCPFastDependencyResolver : PCPDependencyResolverBase
    {
        private readonly PCPGuidIndex m_GuidIndex;

        public PCPFastDependencyResolver(PCPGuidIndex guidIndex)
        {
            m_GuidIndex = guidIndex;
        }

        public override async Task BuildGraphAsync(PCPScanContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!m_IsBuilt)
                LoadFromDisk();

            // Step 1: Build GUID index from .meta files (background, parallel)
            var metaFiles = await context.GetAllMetaFilesAsync(ct);
            var changedFiles = context.Cache.HasAnyChanges
                ? (ICollection<string>)new HashSet<string>(context.Cache.GetStaleAssets())
                : null;

            await m_GuidIndex.BuildAsync(metaFiles, changedFiles, ct);

            // Step 2: Parse GUID references from stale parseable assets (background, parallel)
            var stalePaths = context.Cache.GetStaleAssets()
                .Where(p => PCPGuidParser.IsGuidParseable(p))
                .ToList();

            var results = new ConcurrentDictionary<string, HashSet<string>>();

            await PCPThreading.ParallelForEachAsync(stalePaths, async (path, token) =>
            {
                try
                {
                    var guids = await PCPGuidParser.ParseReferencesAsync(path, token);
                    var resolvedPaths = m_GuidIndex.ResolveAll(guids);
                    results[path] = resolvedPaths;
                }
                catch (IOException) { /* file deleted mid-scan */ }
                catch (System.UnauthorizedAccessException) { /* permission denied */ }
            }, PCPThreading.DefaultConcurrency, ct);

            // Step 3: Update graph edges (background)
            foreach (var (asset, deps) in results)
            {
                ct.ThrowIfCancellationRequested();
                UpdateEdges(asset, deps);
            }

            // Also register non-parseable assets (binaries like .png, .fbx)
            // They are referenced BY other assets but don't reference anything themselves
            var allAssets = await context.GetAllProjectAssetsAsync(ct);
            foreach (var asset in allAssets)
                m_AllAssets[asset] = 0;

            // Step 4: BFS reachability (background)
            ct.ThrowIfCancellationRequested();
            var roots = CollectRoots(context);
            ComputeReachability(roots);

            m_IsBuilt = true;
            SaveToDisk();
        }
    }
}
