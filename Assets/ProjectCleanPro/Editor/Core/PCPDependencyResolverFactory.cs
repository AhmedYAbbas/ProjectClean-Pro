namespace ProjectCleanPro.Editor.Core
{
    public static class PCPDependencyResolverFactory
    {
        public static IPCPDependencyResolver Create()
        {
            return new PCPAccurateDependencyResolver();
        }
    }
}
