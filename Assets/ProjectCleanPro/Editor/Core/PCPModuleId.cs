namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Typed identifier for every ProjectCleanPro analysis module.
    /// Replaces magic strings with compile-time safe, O(1) array-indexable values.
    /// </summary>
    public enum PCPModuleId : byte
    {
        Unused       = 0,
        Missing      = 1,
        Duplicates   = 2,
        Dependencies = 3,
        Packages     = 4,
        Shaders      = 5,
        Size         = 6,
    }
}
