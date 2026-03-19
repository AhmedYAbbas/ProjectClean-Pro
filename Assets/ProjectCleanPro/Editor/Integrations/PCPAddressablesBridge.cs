using System.Collections.Generic;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Non-conditional bridge for Addressable Asset System integration.
    /// This class is always compiled regardless of whether the Addressables package
    /// is installed. It safely delegates to <see cref="PCPAddressablesSupport"/>
    /// when the <c>PCP_ADDRESSABLES</c> scripting define is set, and returns
    /// sensible defaults otherwise.
    /// </summary>
    public static class PCPAddressablesBridge
    {
        /// <summary>
        /// Whether the Addressables integration is available in this build.
        /// Returns <c>true</c> only when the <c>PCP_ADDRESSABLES</c> define is active
        /// and the Addressables package is installed.
        /// </summary>
        public static bool HasAddressables
        {
            get
            {
#if PCP_ADDRESSABLES
                return PCPAddressablesSupport.IsAvailable();
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Returns all asset paths registered in Addressable groups.
        /// Delegates to <see cref="PCPAddressablesSupport.GetAddressableRoots"/>
        /// when available; returns an empty list otherwise.
        /// </summary>
        public static List<string> GetRoots()
        {
#if PCP_ADDRESSABLES
            return PCPAddressablesSupport.GetAddressableRoots();
#else
            return new List<string>();
#endif
        }

        /// <summary>
        /// Checks whether the asset at the given path belongs to any Addressable group.
        /// Delegates to <see cref="PCPAddressablesSupport.IsAddressable"/> when available;
        /// returns <c>false</c> otherwise.
        /// </summary>
        /// <param name="path">A project-relative asset path.</param>
        public static bool IsAddressable(string path)
        {
#if PCP_ADDRESSABLES
            return PCPAddressablesSupport.IsAddressable(path);
#else
            return false;
#endif
        }
    }
}
