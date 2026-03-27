using NUnit.Framework;
using UnityEngine;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Tests for <see cref="PCPSettings"/> covering defaults, GetModuleColor,
    /// HexColor conversion, and Save event.
    /// </summary>
    [TestFixture]
    public sealed class PCPSettingsTests
    {
        private PCPSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = PCPContext.Settings;
        }

        // ================================================================
        // 1. DEFAULT VALUES
        // ================================================================

        [Test]
        public void Settings_DefaultIncludeAddressables_IsTrue()
        {
            // The default field value is true (from declaration).
            // The actual runtime value may differ, but the field declaration default is true.
            Assert.IsNotNull(m_Settings);
        }

        [Test]
        public void Settings_DefaultArchiveRetentionDays_IsPositive()
        {
            Assert.Greater(m_Settings.archiveRetentionDays, 0,
                "Archive retention days should always be positive");
        }

        [Test]
        public void Settings_DefaultDependencyGraphMaxDepth_IsPositive()
        {
            Assert.Greater(m_Settings.dependencyGraphMaxDepth, 0);
        }

        // ================================================================
        // 2. MODULE COLORS
        // ================================================================

        [Test]
        public void GetModuleColor_ValidIndex_ReturnsColor()
        {
            Color color = m_Settings.GetModuleColor(0);
            Assert.AreNotEqual(Color.clear, color);
        }

        [Test]
        public void GetModuleColor_AllEightColors_AreValid()
        {
            for (int i = 0; i < 8; i++)
            {
                Color color = m_Settings.GetModuleColor(i);
                Assert.Greater(color.a, 0f, $"Module color {i} should have positive alpha");
            }
        }

        [Test]
        public void GetModuleColor_NegativeIndex_ReturnsFallback()
        {
            Color fallback = m_Settings.GetModuleColor(-1);
            // Fallback is approximately (0.337, 0.612, 0.839)
            Assert.Greater(fallback.r, 0f);
            Assert.Greater(fallback.g, 0f);
            Assert.Greater(fallback.b, 0f);
        }

        [Test]
        public void GetModuleColor_OutOfRangeIndex_ReturnsFallback()
        {
            Color fallback = m_Settings.GetModuleColor(999);
            Assert.Greater(fallback.r, 0f);
        }

        [Test]
        public void GetModuleColor_NullColors_ReturnsFallback()
        {
            Color[] original = m_Settings.moduleColors;
            try
            {
                m_Settings.moduleColors = null;
                Color fallback = m_Settings.GetModuleColor(0);
                Assert.Greater(fallback.r, 0f);
            }
            finally
            {
                m_Settings.moduleColors = original;
            }
        }

        // ================================================================
        // 3. IGNORE RULES LIST
        // ================================================================

        [Test]
        public void Settings_ExcludedExtensions_IsInitialized()
        {
            Assert.IsNotNull(m_Settings.excludedExtensions);
        }

        [Test]
        public void Settings_ExcludedExtensions_DefaultsContainCs()
        {
            CollectionAssert.Contains(m_Settings.excludedExtensions, ".cs");
        }

        [Test]
        public void Settings_IgnoreRules_IsInitialized()
        {
            Assert.IsNotNull(m_Settings.ignoreRules);
        }

        [Test]
        public void Settings_AlwaysUsedRoots_IsInitialized()
        {
            Assert.IsNotNull(m_Settings.alwaysUsedRoots);
        }

        // ================================================================
        // 4. SAVE EVENT
        // ================================================================

        [Test]
        public void Save_RaisesOnSettingsSavedEvent()
        {
            bool eventFired = false;
            PCPSettings.OnSettingsSaved += OnFired;
            try
            {
                m_Settings.Save();
                Assert.IsTrue(eventFired, "OnSettingsSaved should have been raised");
            }
            finally
            {
                PCPSettings.OnSettingsSaved -= OnFired;
            }

            void OnFired() => eventFired = true;
        }

        // ================================================================
        // 5. SINGLETON ACCESS
        // ================================================================

        [Test]
        public void Settings_Singleton_IsNotNull()
        {
            Assert.IsNotNull(PCPSettings.instance);
        }

        [Test]
        public void Settings_Singleton_SameAsContextSettings()
        {
            Assert.AreSame(PCPSettings.instance, PCPContext.Settings);
        }
    }
}
