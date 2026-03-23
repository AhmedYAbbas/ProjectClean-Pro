using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Implemented by module views that support adding selected assets
    /// to the ignore list. The base class automatically adds an
    /// "Ignore Selected" button when this interface is detected.
    /// </summary>
    public interface IPCPIgnorableView
    {
        /// <summary>
        /// Returns the asset paths currently selected for ignoring.
        /// </summary>
        IReadOnlyList<string> GetSelectedPaths();

        /// <summary>
        /// Clears the current selection state after an ignore operation.
        /// </summary>
        void ClearSelection();
    }
}
