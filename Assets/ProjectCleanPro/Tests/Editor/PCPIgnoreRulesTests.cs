using System.Collections.Generic;
using NUnit.Framework;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPIgnoreRules"/> covering all rule types,
    /// edge cases, null/empty inputs, regex caching, and the pcp-keep label.
    /// </summary>
    [TestFixture]
    public sealed class PCPIgnoreRulesTests
    {
        private PCPIgnoreRules m_Rules;

        [SetUp]
        public void SetUp()
        {
            m_Rules = new PCPIgnoreRules();
        }

        [TearDown]
        public void TearDown()
        {
            m_Rules.ClearCache();
        }

        // ================================================================
        // 1. NULL / EMPTY INPUTS
        // ================================================================

        [Test]
        public void IsIgnored_NullPath_ReturnsFalse()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathPrefix, "Assets/") };
            Assert.IsFalse(m_Rules.IsIgnored(null, rules));
        }

        [Test]
        public void IsIgnored_EmptyPath_ReturnsFalse()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathPrefix, "Assets/") };
            Assert.IsFalse(m_Rules.IsIgnored("", rules));
        }

        [Test]
        public void IsIgnored_NullRulesArray_ReturnsFalse()
        {
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/bg.png", (PCPIgnoreRule[])null));
        }

        [Test]
        public void IsIgnored_EmptyRulesArray_ReturnsFalse()
        {
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/bg.png", new PCPIgnoreRule[0]));
        }

        [Test]
        public void IsIgnored_NullRulesList_ReturnsFalse()
        {
            // List overload with null: returns HasKeepLabel check. For a non-existent
            // asset it returns false because AssetDatabase.LoadMainAssetAtPath returns null.
            Assert.IsFalse(m_Rules.IsIgnored("Assets/NonExistent/file.png", (List<PCPIgnoreRule>)null));
        }

        [Test]
        public void IsIgnored_EmptyRulesList_ReturnsFalse()
        {
            Assert.IsFalse(m_Rules.IsIgnored("Assets/NonExistent/file.png", new List<PCPIgnoreRule>()));
        }

        // ================================================================
        // 2. PATH PREFIX RULES
        // ================================================================

        [Test]
        public void PathPrefix_MatchesWhenPathStartsWithPattern()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathPrefix, "Assets/Textures/") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        [Test]
        public void PathPrefix_DoesNotMatchDifferentPrefix()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathPrefix, "Assets/Textures/") };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Materials/floor.mat", rules));
        }

        [Test]
        public void PathPrefix_IsCaseInsensitive()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathPrefix, "assets/textures/") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        [Test]
        public void PathPrefix_ExactPrefixMatch()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathPrefix, "Assets/Textures/bg.png") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        // ================================================================
        // 3. PATH EXACT RULES
        // ================================================================

        [Test]
        public void PathExact_MatchesExactPath()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathExact, "Assets/Textures/bg.png") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        [Test]
        public void PathExact_DoesNotMatchDifferentPath()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathExact, "Assets/Textures/bg.png") };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/fg.png", rules));
        }

        [Test]
        public void PathExact_IsCaseInsensitive()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathExact, "assets/textures/BG.PNG") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        [Test]
        public void PathExact_DoesNotMatchPrefix()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.PathExact, "Assets/Textures/") };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        // ================================================================
        // 4. REGEX RULES
        // ================================================================

        [Test]
        public void Regex_MatchesPattern()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"\.bak$") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Textures/bg.png.bak", rules));
        }

        [Test]
        public void Regex_DoesNotMatchWhenNoMatch()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"\.bak$") };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        [Test]
        public void Regex_IsCaseInsensitive()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"textures") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/TEXTURES/bg.png", rules));
        }

        [Test]
        public void Regex_InvalidPattern_ReturnsFalse_DoesNotThrow()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"[invalid") };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        [Test]
        public void Regex_InvalidPattern_CachesNeverMatchingRegex()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"[invalid") };

            // First call logs the warning and caches.
            m_Rules.IsIgnored("Assets/test.png", rules);

            // Second call should also return false without throwing.
            Assert.IsFalse(m_Rules.IsIgnored("Assets/other.png", rules));
        }

        [Test]
        public void Regex_ComplexPattern_Works()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"Assets/Temp_\d+/.*\.tmp$") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Temp_42/data.tmp", rules));
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Temp_42/data.png", rules));
        }

        // ================================================================
        // 5. FOLDER RULES
        // ================================================================

        [Test]
        public void Folder_MatchesWithTrailingSlash()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Folder, "Assets/ThirdParty/") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/ThirdParty/lib.dll", rules));
        }

        [Test]
        public void Folder_MatchesWithoutTrailingSlash_NormalizesAutomatically()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Folder, "Assets/ThirdParty") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/ThirdParty/lib.dll", rules));
        }

        [Test]
        public void Folder_DoesNotMatchDifferentFolder()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Folder, "Assets/ThirdParty") };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/MyCode/script.cs", rules));
        }

        [Test]
        public void Folder_IsCaseInsensitive()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Folder, "assets/thirdparty") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/ThirdParty/lib.dll", rules));
        }

        [Test]
        public void Folder_DoesNotMatchPartialFolderName()
        {
            // "Assets/Third" should NOT match "Assets/ThirdParty/lib.dll"
            // because normalized becomes "Assets/Third/" which is not a prefix of "Assets/ThirdParty/lib.dll"
            var rules = new[] { MakeRule(PCPIgnoreType.Folder, "Assets/Third") };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/ThirdParty/lib.dll", rules));
        }

        // ================================================================
        // 6. DISABLED RULES
        // ================================================================

        [Test]
        public void DisabledRule_IsSkipped()
        {
            var rule = MakeRule(PCPIgnoreType.PathPrefix, "Assets/");
            rule.enabled = false;
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/bg.png", new[] { rule }));
        }

        [Test]
        public void NullRuleInArray_IsSkipped()
        {
            var rules = new PCPIgnoreRule[] { null, MakeRule(PCPIgnoreType.PathPrefix, "Assets/Skip/") };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Skip/file.png", rules));
        }

        [Test]
        public void RuleWithEmptyPattern_IsSkipped()
        {
            var rule = MakeRule(PCPIgnoreType.PathPrefix, "");
            Assert.IsFalse(m_Rules.IsIgnored("Assets/anything.png", new[] { rule }));
        }

        // ================================================================
        // 7. MULTIPLE RULES
        // ================================================================

        [Test]
        public void MultipleRules_FirstMatchWins()
        {
            var rules = new[]
            {
                MakeRule(PCPIgnoreType.PathPrefix, "Assets/NoMatch/"),
                MakeRule(PCPIgnoreType.PathExact, "Assets/Textures/bg.png"),
            };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        [Test]
        public void MultipleRules_NoMatch_ReturnsFalse()
        {
            var rules = new[]
            {
                MakeRule(PCPIgnoreType.PathPrefix, "Assets/Foo/"),
                MakeRule(PCPIgnoreType.PathExact, "Assets/Bar/baz.png"),
                MakeRule(PCPIgnoreType.Regex, @"\.tmp$"),
            };
            Assert.IsFalse(m_Rules.IsIgnored("Assets/Textures/bg.png", rules));
        }

        // ================================================================
        // 8. LIST OVERLOAD
        // ================================================================

        [Test]
        public void ListOverload_MatchesSameAsArrayOverload()
        {
            var rules = new List<PCPIgnoreRule>
            {
                MakeRule(PCPIgnoreType.PathPrefix, "Assets/Ignored/")
            };
            Assert.IsTrue(m_Rules.IsIgnored("Assets/Ignored/file.png", rules));
        }

        [Test]
        public void ListOverload_EmptyPath_ReturnsFalse()
        {
            var rules = new List<PCPIgnoreRule>
            {
                MakeRule(PCPIgnoreType.PathPrefix, "Assets/")
            };
            Assert.IsFalse(m_Rules.IsIgnored("", rules));
        }

        // ================================================================
        // 9. CACHE CLEARING
        // ================================================================

        [Test]
        public void ClearCache_DoesNotThrow()
        {
            // Populate the cache with a regex.
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"\.png$") };
            m_Rules.IsIgnored("Assets/test.png", rules);

            Assert.DoesNotThrow(() => m_Rules.ClearCache());
        }

        [Test]
        public void ClearCache_RegexStillWorksAfterClearing()
        {
            var rules = new[] { MakeRule(PCPIgnoreType.Regex, @"\.png$") };
            m_Rules.IsIgnored("Assets/test.png", rules);
            m_Rules.ClearCache();

            // Should re-compile and still work.
            Assert.IsTrue(m_Rules.IsIgnored("Assets/test.png", rules));
        }

        // ================================================================
        // 10. KEEP LABEL CONSTANT
        // ================================================================

        [Test]
        public void KeepLabel_HasExpectedValue()
        {
            Assert.AreEqual("pcp-keep", PCPIgnoreRules.KeepLabel);
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static PCPIgnoreRule MakeRule(PCPIgnoreType type, string pattern)
        {
            return new PCPIgnoreRule
            {
                type = type,
                pattern = pattern,
                enabled = true,
                comment = ""
            };
        }
    }
}
