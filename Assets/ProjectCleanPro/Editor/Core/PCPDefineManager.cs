using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Validates and logs the active ProjectCleanPro scripting defines on domain reload.
    /// The actual defines (PCP_ADDRESSABLES, PCP_URP, PCP_HDRP, PCP_SHADERGRAPH) are
    /// declared via versionDefines in the assembly definition so that Unity manages them
    /// automatically based on which packages are installed. This class provides a central
    /// place to query define availability at runtime and to log the active configuration.
    /// </summary>
    [InitializeOnLoad]
    public static class PCPDefineManager
    {
        // ----------------------------------------------------------------
        // Constants — must match the versionDefines entries in the .asmdef
        // ----------------------------------------------------------------

        public const string Define_Addressables = "PCP_ADDRESSABLES";
        public const string Define_URP          = "PCP_URP";
        public const string Define_HDRP         = "PCP_HDRP";
        public const string Define_ShaderGraph  = "PCP_SHADERGRAPH";

        // ----------------------------------------------------------------
        // Availability flags — set at compile time by #if guards
        // ----------------------------------------------------------------

#if PCP_ADDRESSABLES
        public static bool AddressablesAvailable => true;
#else
        public static bool AddressablesAvailable => false;
#endif

#if PCP_URP
        public static bool URPAvailable => true;
#else
        public static bool URPAvailable => false;
#endif

#if PCP_HDRP
        public static bool HDRPAvailable => true;
#else
        public static bool HDRPAvailable => false;
#endif

#if PCP_SHADERGRAPH
        public static bool ShaderGraphAvailable => true;
#else
        public static bool ShaderGraphAvailable => false;
#endif

        // ----------------------------------------------------------------
        // Domain-reload initializer
        // ----------------------------------------------------------------

        static PCPDefineManager()
        {
            // Log the active integration state once on domain reload so that
            // developers can quickly see what optional packages were detected.
            if (SessionState.GetBool("PCP_DefinesLogged", false))
                return;

            SessionState.SetBool("PCP_DefinesLogged", true);

            Debug.Log(
                "[ProjectCleanPro] Active integrations — " +
                $"Addressables: {Flag(AddressablesAvailable)}, " +
                $"URP: {Flag(URPAvailable)}, " +
                $"HDRP: {Flag(HDRPAvailable)}, " +
                $"ShaderGraph: {Flag(ShaderGraphAvailable)}");
        }

        // ----------------------------------------------------------------
        // Public helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns a summary string of all active PCP integration flags, suitable
        /// for inclusion in exported reports or diagnostic output.
        /// </summary>
        public static string GetDefineSummary()
        {
            return
                $"Addressables={Flag(AddressablesAvailable)} " +
                $"URP={Flag(URPAvailable)} " +
                $"HDRP={Flag(HDRPAvailable)} " +
                $"ShaderGraph={Flag(ShaderGraphAvailable)}";
        }

        // ----------------------------------------------------------------
        // Private helpers
        // ----------------------------------------------------------------

        private static string Flag(bool value) => value ? "ON" : "OFF";
    }
}
