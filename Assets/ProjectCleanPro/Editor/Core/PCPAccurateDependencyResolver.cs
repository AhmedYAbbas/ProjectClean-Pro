using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Accurate mode: uses AssetDatabase.GetDependencies for all assets.
    /// Main-thread work is frame-budgeted via the scheduler.
    /// Graph building and BFS run on background threads.
    /// </summary>
    internal sealed class PCPAccurateDependencyResolver : PCPDependencyResolverBase
    {
        public override async Task BuildGraphAsync(PCPScanContext context, CancellationToken ct)
        {
            var scheduler = context.Scheduler;
            var cache = context.Cache;

            // Try loading from disk first
            if (!m_IsBuilt)
                LoadFromDisk();

            // Phase 1: Remove edges for deleted assets (background)
            var allPaths = await context.GetAllProjectAssetsAsync(ct);
            await scheduler.ScheduleBackground(async ct2 =>
            {
                var currentPathSet = new HashSet<string>(allPaths);
                var toRemove = m_AllAssets.Keys
                    .Where(a => !currentPathSet.Contains(a))
                    .ToList();
                foreach (var path in toRemove)
                    RemoveAssetEdges(path);
            }, ct);

            // Phase 2: Query deps on main thread, frame-budgeted (only stale assets)
            var stalePaths = cache.GetStaleAssets()
                .Where(p => !p.EndsWith(".meta"))
                .ToList();

            if (stalePaths.Count > 0)
            {
                var depsPerAsset = await scheduler.BatchOnMainThread(
                    stalePaths,
                    path => AssetDatabase.GetDependencies(path, false),
                    ct);

                // Phase 3: Update graph (background)
                await scheduler.ScheduleBackground(async ct2 =>
                {
                    for (int i = 0; i < stalePaths.Count; i++)
                    {
                        ct2.ThrowIfCancellationRequested();
                        UpdateEdges(stalePaths[i], depsPerAsset[i]);
                    }
                }, ct);
            }

            // Phase 4: BFS reachability (background)
            await scheduler.ScheduleBackground(async ct2 =>
            {
                var roots = CollectRoots(context);
                ComputeReachability(roots);
            }, ct);

            m_IsBuilt = true;
            SaveToDisk();
        }
    }
}
