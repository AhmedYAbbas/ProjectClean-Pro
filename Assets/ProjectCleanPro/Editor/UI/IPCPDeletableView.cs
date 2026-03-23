using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Implemented by module views that support deleting selected assets.
    /// The base class automatically adds a "Delete Selected" button when
    /// this interface is detected.
    /// </summary>
    public interface IPCPDeletableView
    {
        /// <summary>
        /// Returns the asset paths currently selected for deletion.
        /// </summary>
        IReadOnlyList<string> GetSelectedPaths();

        /// <summary>
        /// Clears the current selection state after a delete or ignore operation.
        /// </summary>
        void ClearSelection();
    }
}
