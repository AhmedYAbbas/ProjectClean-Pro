namespace PCPObfuscator;

/// <summary>
/// Generates deterministic, collision-free obfuscated names with category prefixes.
/// </summary>
public sealed class NameGenerator
{
    private readonly Dictionary<string, int> _counters = new();
    private readonly Dictionary<string, string> _renameMap = new();

    private static readonly Dictionary<string, string> s_Prefixes = new()
    {
        ["method"]    = "_m",
        ["field"]     = "_f",
        ["local"]     = "_v",
        ["param"]     = "_p",
        ["type"]      = "_t",
        ["property"]  = "_r",
    };

    /// <summary>
    /// Gets or creates an obfuscated name for the given original name and category.
    /// Same original+category always returns the same obfuscated name.
    /// </summary>
    public string GetOrCreate(string originalName, string category)
    {
        string key = $"{category}:{originalName}";
        if (_renameMap.TryGetValue(key, out string? existing))
            return existing;

        string prefix = s_Prefixes.GetValueOrDefault(category, "_x");
        if (!_counters.TryGetValue(category, out int counter))
            counter = 0;

        string obfuscated = $"{prefix}{counter}";
        _counters[category] = counter + 1;
        _renameMap[key] = obfuscated;
        return obfuscated;
    }

    /// <summary>
    /// Looks up an existing mapping without creating one.
    /// </summary>
    public bool TryGet(string originalName, string category, out string obfuscated)
    {
        return _renameMap.TryGetValue($"{category}:{originalName}", out obfuscated!);
    }

    /// <summary>
    /// Returns the total number of identifiers renamed.
    /// </summary>
    public int TotalRenamed => _renameMap.Count;

    /// <summary>
    /// Resets all mappings. Used between scoped contexts (e.g., per-method locals).
    /// </summary>
    public void ResetCategory(string category)
    {
        var keysToRemove = _renameMap.Keys.Where(k => k.StartsWith(category + ":")).ToList();
        foreach (var key in keysToRemove)
            _renameMap.Remove(key);
        _counters.Remove(category);
    }
}
