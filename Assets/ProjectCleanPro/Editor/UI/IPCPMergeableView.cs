namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Implemented by module views that support merging duplicate items.
    /// The base class automatically adds a "Merge All" button when this
    /// interface is detected.
    /// </summary>
    public interface IPCPMergeableView
    {
        /// <summary>
        /// Merges all items in the current result set.
        /// </summary>
        void MergeAll();
    }
}
