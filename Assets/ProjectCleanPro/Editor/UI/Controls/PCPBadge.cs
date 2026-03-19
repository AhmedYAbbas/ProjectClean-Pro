using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// A small, rounded status badge label used throughout ProjectCleanPro to indicate
    /// asset status (UNUSED, ERROR, WARNING, etc.).
    /// </summary>
#if UNITY_2023_2_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class PCPBadge : Label
    {
#if !UNITY_2023_2_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PCPBadge, UxmlTraits> { }
#endif

        // USS class names
        public const string UssClassName = "pcp-badge";
        public const string ErrorUssClassName = "pcp-badge--error";
        public const string WarningUssClassName = "pcp-badge--warning";
        public const string InfoUssClassName = "pcp-badge--info";
        public const string SuccessUssClassName = "pcp-badge--success";
        public const string UnusedUssClassName = "pcp-badge--unused";
        public const string IgnoredUssClassName = "pcp-badge--ignored";

        // Predefined colors
        private static readonly Color k_ErrorColor = new Color(0.957f, 0.278f, 0.278f, 1f);   // #f44747
        private static readonly Color k_WarningColor = new Color(0.800f, 0.655f, 0.000f, 1f);  // #cca700
        private static readonly Color k_InfoColor = new Color(0.337f, 0.612f, 0.839f, 1f);     // #569cd6
        private static readonly Color k_SuccessColor = new Color(0.416f, 0.600f, 0.333f, 1f);  // #6a9955
        private static readonly Color k_UnusedColor = new Color(0.753f, 0.224f, 0.169f, 1f);   // #c0392b
        private static readonly Color k_IgnoredColor = new Color(0.498f, 0.549f, 0.553f, 1f);  // #7f8c8d
        private static readonly Color k_InUseColor = new Color(0.416f, 0.600f, 0.333f, 1f);    // #6a9955

        public PCPBadge() : this(string.Empty, Color.gray) { }

        public PCPBadge(string text, Color backgroundColor)
        {
            this.text = text;
            AddToClassList(UssClassName);
            ApplyBaseStyle(backgroundColor);
        }

        /// <summary>
        /// Applies the base inline style shared by all badges.
        /// USS classes drive theme-aware overrides; inline styles provide fallback.
        /// </summary>
        private void ApplyBaseStyle(Color bgColor)
        {
            style.paddingLeft = 4;
            style.paddingRight = 4;
            style.paddingTop = 1;
            style.paddingBottom = 1;
            style.borderTopLeftRadius = 3;
            style.borderTopRightRadius = 3;
            style.borderBottomLeftRadius = 3;
            style.borderBottomRightRadius = 3;
            style.fontSize = 10;
            style.color = Color.white;
            style.backgroundColor = bgColor;
            style.unityTextAlign = TextAnchor.MiddleCenter;
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.flexShrink = 0;
        }

        /// <summary>
        /// Updates the badge text and background color.
        /// </summary>
        public void SetBadge(string label, Color bgColor)
        {
            text = label;
            style.backgroundColor = bgColor;
        }

        // ----------------------------------------------------------------
        // Factory methods
        // ----------------------------------------------------------------

        public static PCPBadge Error(string label = "ERROR")
        {
            var badge = new PCPBadge(label, k_ErrorColor);
            badge.AddToClassList(ErrorUssClassName);
            return badge;
        }

        public static PCPBadge Warning(string label = "WARNING")
        {
            var badge = new PCPBadge(label, k_WarningColor);
            badge.AddToClassList(WarningUssClassName);
            return badge;
        }

        public static PCPBadge Info(string label = "INFO")
        {
            var badge = new PCPBadge(label, k_InfoColor);
            badge.AddToClassList(InfoUssClassName);
            return badge;
        }

        public static PCPBadge Success(string label = "OK")
        {
            var badge = new PCPBadge(label, k_SuccessColor);
            badge.AddToClassList(SuccessUssClassName);
            return badge;
        }

        public static PCPBadge Unused(string label = "UNUSED")
        {
            var badge = new PCPBadge(label, k_UnusedColor);
            badge.AddToClassList(UnusedUssClassName);
            return badge;
        }

        public static PCPBadge Ignored(string label = "IGNORED")
        {
            var badge = new PCPBadge(label, k_IgnoredColor);
            badge.AddToClassList(IgnoredUssClassName);
            return badge;
        }

        public static PCPBadge InUse(string label = "IN USE")
        {
            var badge = new PCPBadge(label, k_InUseColor);
            badge.AddToClassList(SuccessUssClassName);
            return badge;
        }

        /// <summary>
        /// Creates a badge from a <see cref="PCPSeverity"/> value.
        /// </summary>
        public static PCPBadge FromSeverity(PCPSeverity severity)
        {
            switch (severity)
            {
                case PCPSeverity.Error: return Error();
                case PCPSeverity.Warning: return Warning();
                case PCPSeverity.Info: return Info();
                default: return Info();
            }
        }
    }
}
