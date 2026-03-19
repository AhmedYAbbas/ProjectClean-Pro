using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// The kind of matching an ignore rule performs.
    /// </summary>
    public enum PCPIgnoreType
    {
        /// <summary>Asset path starts with the pattern (folder prefix).</summary>
        PathPrefix,

        /// <summary>Asset path matches the pattern exactly.</summary>
        PathExact,

        /// <summary>Asset path matches a .NET regular expression.</summary>
        Regex,

        /// <summary>The main asset type name equals the pattern (e.g. "Texture2D").</summary>
        AssetType,

        /// <summary>The asset carries a Unity label matching the pattern.</summary>
        Label,

        /// <summary>The asset resides anywhere under the given folder path.</summary>
        Folder,
    }

    /// <summary>
    /// A single ignore rule entry persisted in <see cref="PCPSettings"/>.
    /// </summary>
    [Serializable]
    public sealed class PCPIgnoreRule
    {
        [Tooltip("How this rule matches assets.")]
        public PCPIgnoreType type = PCPIgnoreType.PathPrefix;

        [Tooltip("The pattern to match (interpretation depends on Type).")]
        public string pattern = string.Empty;

        [Tooltip("Optional human-readable comment explaining why this rule exists.")]
        public string comment = string.Empty;

        [Tooltip("Whether this rule is active.")]
        public bool enabled = true;
    }

    /// <summary>
    /// Evaluates a set of <see cref="PCPIgnoreRule"/> entries against asset paths
    /// to determine whether an asset should be excluded from scan results.
    /// </summary>
    public sealed class PCPIgnoreRules
    {
        /// <summary>
        /// The special Unity label that forces an asset to be kept regardless of rules.
        /// </summary>
        public const string KeepLabel = "pcp-keep";

        // Cache compiled regexes keyed by their pattern string.
        private readonly Dictionary<string, Regex> m_RegexCache =
            new Dictionary<string, Regex>(StringComparer.Ordinal);

        /// <summary>
        /// Returns <c>true</c> if <paramref name="assetPath"/> should be ignored
        /// (i.e. excluded from scan results) according to the supplied rules.
        /// <para>
        /// An asset with the <c>pcp-keep</c> Unity label is always ignored
        /// (treated as intentionally kept) regardless of the rules array.
        /// </para>
        /// </summary>
        /// <param name="assetPath">Full asset path (e.g. "Assets/Textures/bg.png").</param>
        /// <param name="rules">The set of rules to evaluate.</param>
        /// <returns><c>true</c> if the asset should be excluded.</returns>
        public bool IsIgnored(string assetPath, PCPIgnoreRule[] rules)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            // Fast-path: check the pcp-keep label.
            if (HasKeepLabel(assetPath))
                return true;

            if (rules == null || rules.Length == 0)
                return false;

            for (int i = 0; i < rules.Length; i++)
            {
                PCPIgnoreRule rule = rules[i];
                if (rule == null || !rule.enabled || string.IsNullOrEmpty(rule.pattern))
                    continue;

                if (Matches(assetPath, rule))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Overload accepting a <see cref="List{T}"/> for convenience.
        /// </summary>
        public bool IsIgnored(string assetPath, List<PCPIgnoreRule> rules)
        {
            if (rules == null || rules.Count == 0)
                return HasKeepLabel(assetPath);

            // Avoid allocating a temporary array by inlining the logic.
            if (string.IsNullOrEmpty(assetPath))
                return false;

            if (HasKeepLabel(assetPath))
                return true;

            for (int i = 0; i < rules.Count; i++)
            {
                PCPIgnoreRule rule = rules[i];
                if (rule == null || !rule.enabled || string.IsNullOrEmpty(rule.pattern))
                    continue;

                if (Matches(assetPath, rule))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Convenience overload that evaluates against the rules in <see cref="PCPContext.Settings"/>.
        /// This is the most commonly used entry point from scan modules.
        /// </summary>
        /// <param name="assetPath">Full asset path (e.g. "Assets/Textures/bg.png").</param>
        /// <returns><c>true</c> if the asset should be excluded.</returns>
        public bool IsIgnored(string assetPath)
        {
            PCPSettings settings = PCPContext.Settings;
            if (settings == null)
                return HasKeepLabel(assetPath);
            return IsIgnored(assetPath, settings.ignoreRules);
        }

        /// <summary>
        /// Clears the compiled-regex cache. Call when rules change.
        /// </summary>
        public void ClearCache()
        {
            m_RegexCache.Clear();
        }

        // ----------------------------------------------------------------
        // Matching
        // ----------------------------------------------------------------

        private bool Matches(string assetPath, PCPIgnoreRule rule)
        {
            switch (rule.type)
            {
                case PCPIgnoreType.PathPrefix:
                    return assetPath.StartsWith(rule.pattern, StringComparison.OrdinalIgnoreCase);

                case PCPIgnoreType.PathExact:
                    return string.Equals(assetPath, rule.pattern, StringComparison.OrdinalIgnoreCase);

                case PCPIgnoreType.Regex:
                    return MatchesRegex(assetPath, rule.pattern);

                case PCPIgnoreType.AssetType:
                    return MatchesAssetType(assetPath, rule.pattern);

                case PCPIgnoreType.Label:
                    return HasLabel(assetPath, rule.pattern);

                case PCPIgnoreType.Folder:
                    return MatchesFolder(assetPath, rule.pattern);

                default:
                    return false;
            }
        }

        private bool MatchesRegex(string assetPath, string pattern)
        {
            try
            {
                if (!m_RegexCache.TryGetValue(pattern, out Regex regex))
                {
                    regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    m_RegexCache[pattern] = regex;
                }
                return regex.IsMatch(assetPath);
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern; treat as non-match and log once.
                Debug.LogWarning($"[ProjectCleanPro] Invalid regex ignore pattern: {pattern}");
                // Store a never-matching regex so we don't log repeatedly.
                m_RegexCache[pattern] = new Regex(@"(?!)");
                return false;
            }
        }

        private static bool MatchesAssetType(string assetPath, string typeName)
        {
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (assetType == null)
                return false;
            return string.Equals(assetType.Name, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(assetType.FullName, typeName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasLabel(string assetPath, string label)
        {
            string[] labels = AssetDatabase.GetLabels(
                AssetDatabase.LoadMainAssetAtPath(assetPath));
            if (labels == null)
                return false;
            for (int i = 0; i < labels.Length; i++)
            {
                if (string.Equals(labels[i], label, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool MatchesFolder(string assetPath, string folder)
        {
            // Normalize: ensure the folder pattern ends with '/'.
            string normalized = folder.EndsWith("/") ? folder : folder + "/";
            return assetPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase);
        }

        // ----------------------------------------------------------------
        // Keep label
        // ----------------------------------------------------------------

        private static bool HasKeepLabel(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                return false;

            string[] labels = AssetDatabase.GetLabels(asset);
            if (labels == null)
                return false;

            for (int i = 0; i < labels.Length; i++)
            {
                if (string.Equals(labels[i], KeepLabel, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
