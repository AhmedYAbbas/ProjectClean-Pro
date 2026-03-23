namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Implemented by module views that support exporting their results.
    /// The base class automatically adds an "Export" button when this
    /// interface is detected.
    /// </summary>
    public interface IPCPExportableView
    {
        /// <summary>
        /// The module key used by <see cref="PCPReportExporter.CreateModuleSubset"/>
        /// to export only this module's data. Return null to export the full scan result.
        /// </summary>
        string ModuleExportKey { get; }
    }
}
