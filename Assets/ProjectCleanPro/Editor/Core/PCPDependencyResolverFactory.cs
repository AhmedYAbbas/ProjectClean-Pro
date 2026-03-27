using System;

namespace ProjectCleanPro.Editor.Core
{
    internal static class PCPDependencyResolverFactory
    {
        public static IPCPDependencyResolver Create(PCPScanMode mode)
        {
            // TODO: Uncomment when resolver implementations exist (Tasks 7-9)
            // return mode switch
            // {
            //     PCPScanMode.Accurate => new PCPAccurateDependencyResolver(),
            //     PCPScanMode.Balanced => new PCPBalancedDependencyResolver(new PCPGuidIndex()),
            //     PCPScanMode.Fast => new PCPFastDependencyResolver(new PCPGuidIndex()),
            //     _ => throw new ArgumentOutOfRangeException(nameof(mode))
            // };
            throw new NotImplementedException($"Resolver for {mode} not yet implemented");
        }
    }
}
