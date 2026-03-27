namespace ProjectCleanPro.Editor.Core
{
    /// <summary>
    /// Controls the trade-off between scan accuracy and speed.
    /// Accurate: AssetDatabase for all deps (slowest, most accurate).
    /// Balanced: GUID parsing + AssetDatabase for complex types.
    /// Fast: Pure GUID parsing, no AssetDatabase for deps (fastest, least accurate).
    /// </summary>
    public enum PCPScanMode
    {
        Accurate = 0,
        Balanced = 1,
        Fast = 2
    }
}
