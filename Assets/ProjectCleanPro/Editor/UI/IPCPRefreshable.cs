namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Implemented by views that need to refresh their display
    /// when scan results change or the view becomes active.
    /// </summary>
    public interface IPCPRefreshable
    {
        void Refresh();
    }
}
