using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectCleanPro.Editor.Core
{
    public interface IPCPDependencyResolver
    {
        Task BuildGraphAsync(PCPScanContext context, CancellationToken ct);
        IReadOnlyCollection<string> GetReachableAssets();
        int GetDependentCount(string path);
        IReadOnlyCollection<string> GetDependencies(string path);
        IReadOnlyCollection<string> GetDependents(string path);
        bool IsReachable(string path);
        IEnumerable<string> GetAllUnreachable();
        IReadOnlyCollection<string> GetAllAssets();
        int AssetCount { get; }
        int ReachableCount { get; }
        bool IsBuilt { get; }
        void SaveToDisk();
        bool LoadFromDisk();
        void Clear();
    }
}
