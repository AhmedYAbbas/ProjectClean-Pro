using System;

namespace ProjectCleanPro.Editor.Core
{
    internal static class PCPDependencyResolverFactory
    {
        public static IPCPDependencyResolver Create(PCPScanMode mode)
        {
            return mode switch
            {
                PCPScanMode.Accurate => new PCPAccurateDependencyResolver(),
                PCPScanMode.Balanced => new PCPBalancedDependencyResolver(new PCPGuidIndex()),
                PCPScanMode.Fast => new PCPFastDependencyResolver(new PCPGuidIndex()),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }
    }
}
