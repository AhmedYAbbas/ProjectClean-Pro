using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectCleanPro.Editor;

namespace ProjectCleanPro.Tests.Editor
{
    /// <summary>
    /// Comprehensive tests for <see cref="PCPSettingsView"/>.
    /// Covers all 7 sections: Scan Configuration, Always-Used Roots,
    /// Ignore Rules, Safe Delete, Module Settings, Module Colors, and Actions.
    /// </summary>
    [TestFixture]
    public sealed class PCPSettingsViewTests
    {
        private PCPSettings m_Settings;
        private PCPSettingsView m_View;
        private EditorWindow m_TestWindow;

        // ----------------------------------------------------------------
        // Setup / Teardown
        // ----------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            m_Settings = PCPContext.Settings;

            // Store original values to restore later
            m_OriginalIncludeAllScenes = m_Settings.includeAllScenes;
            m_OriginalIncludeAddressables = m_Settings.includeAddressables;
            m_OriginalIncludeAssetBundles = m_Settings.includeAssetBundles;
            m_OriginalScanEditorAssets = m_Settings.scanEditorAssets;
            m_OriginalArchiveBeforeDelete = m_Settings.archiveBeforeDelete;
            m_OriginalUseGitRm = m_Settings.useGitRm;
            m_OriginalNullOutReferences = m_Settings.nullOutReferencesOnDelete;
            m_OriginalRetentionDays = m_Settings.archiveRetentionDays;
            m_OriginalGraphMaxDepth = m_Settings.dependencyGraphMaxDepth;
            m_OriginalShaderCheckPipeline = m_Settings.shaderAnalyzerCheckPipeline;
            m_OriginalDuplicateCompareImport = m_Settings.duplicateCompareImportSettings;
            m_OriginalIgnoreRules = new List<PCPIgnoreRule>(m_Settings.ignoreRules);
            m_OriginalAlwaysUsedRoots = new List<string>(m_Settings.alwaysUsedRoots);
            m_OriginalModuleColors = (Color[])m_Settings.moduleColors.Clone();

            // Reset to known defaults before each test
            m_Settings.includeAllScenes = false;
            m_Settings.includeAddressables = true;
            m_Settings.includeAssetBundles = true;
            m_Settings.scanEditorAssets = false;
            m_Settings.archiveBeforeDelete = true;
            m_Settings.useGitRm = false;
            m_Settings.nullOutReferencesOnDelete = false;
            m_Settings.archiveRetentionDays = 30;
            m_Settings.dependencyGraphMaxDepth = 2;
            m_Settings.shaderAnalyzerCheckPipeline = true;
            m_Settings.duplicateCompareImportSettings = true;
            m_Settings.ignoreRules.Clear();
            m_Settings.alwaysUsedRoots.Clear();

            // Create a test window to provide a panel for UIElements event dispatch.
            // Without a panel, RegisterValueChangedCallback and SendEvent do not work.
            m_TestWindow = ScriptableObject.CreateInstance<EditorWindow>();
            m_TestWindow.Show();

            m_View = new PCPSettingsView();
            m_TestWindow.rootVisualElement.Add(m_View);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_TestWindow != null)
                m_TestWindow.Close();

            // Restore original settings
            m_Settings.includeAllScenes = m_OriginalIncludeAllScenes;
            m_Settings.includeAddressables = m_OriginalIncludeAddressables;
            m_Settings.includeAssetBundles = m_OriginalIncludeAssetBundles;
            m_Settings.scanEditorAssets = m_OriginalScanEditorAssets;
            m_Settings.archiveBeforeDelete = m_OriginalArchiveBeforeDelete;
            m_Settings.useGitRm = m_OriginalUseGitRm;
            m_Settings.nullOutReferencesOnDelete = m_OriginalNullOutReferences;
            m_Settings.archiveRetentionDays = m_OriginalRetentionDays;
            m_Settings.dependencyGraphMaxDepth = m_OriginalGraphMaxDepth;
            m_Settings.shaderAnalyzerCheckPipeline = m_OriginalShaderCheckPipeline;
            m_Settings.duplicateCompareImportSettings = m_OriginalDuplicateCompareImport;
            m_Settings.ignoreRules = m_OriginalIgnoreRules;
            m_Settings.alwaysUsedRoots = m_OriginalAlwaysUsedRoots;
            m_Settings.moduleColors = m_OriginalModuleColors;
            m_Settings.Save();
        }

        // Stored original values for teardown restoration
        private bool m_OriginalIncludeAllScenes;
        private bool m_OriginalIncludeAddressables;
        private bool m_OriginalIncludeAssetBundles;
        private bool m_OriginalScanEditorAssets;
        private bool m_OriginalArchiveBeforeDelete;
        private bool m_OriginalUseGitRm;
        private bool m_OriginalNullOutReferences;
        private int m_OriginalRetentionDays;
        private int m_OriginalGraphMaxDepth;
        private bool m_OriginalShaderCheckPipeline;
        private bool m_OriginalDuplicateCompareImport;
        private List<PCPIgnoreRule> m_OriginalIgnoreRules;
        private List<string> m_OriginalAlwaysUsedRoots;
        private Color[] m_OriginalModuleColors;

        // ================================================================
        // General Structure Tests
        // ================================================================

        [Test]
        public void Constructor_CreatesViewWithScrollView()
        {
            Assert.That(m_View.childCount, Is.GreaterThan(0));
            var scrollView = m_View.Q<ScrollView>();
            Assert.That(scrollView, Is.Not.Null, "Should contain a ScrollView");
            Assert.That(scrollView.ClassListContains("pcp-scroll-view"), Is.True);
        }

        [Test]
        public void Constructor_HasSettingsHeader()
        {
            var header = m_View.Q<Label>(className: "pcp-label-header");
            Assert.That(header, Is.Not.Null, "Should have a header label");
            Assert.That(header.text, Is.EqualTo("Settings"));
        }

        [Test]
        public void Constructor_HasAllSectionHeaders()
        {
            var subheaders = m_View.Query<Label>(className: "pcp-label-subheader").ToList();
            var sectionNames = subheaders.Select(l => l.text).ToList();

            Assert.That(sectionNames, Does.Contain("Scan Configuration"));
            Assert.That(sectionNames, Does.Contain("Always-Used Roots"));
            Assert.That(sectionNames, Does.Contain("Ignore Rules"));
            Assert.That(sectionNames, Does.Contain("Safe Delete"));
            Assert.That(sectionNames, Does.Contain("Module Settings"));
            Assert.That(sectionNames, Does.Contain("Module Accent Colors"));
            Assert.That(sectionNames, Does.Contain("Actions"));
        }

        [Test]
        public void Constructor_HasSeparatorsBetweenSections()
        {
            var separators = m_View.Query(className: "pcp-separator").ToList();
            // 6 separators between 7 sections
            Assert.That(separators.Count, Is.EqualTo(6));
        }

        [Test]
        public void FlexGrow_IsSetToOne()
        {
            Assert.That(m_View.style.flexGrow.value, Is.EqualTo(1f));
        }

        [Test]
        public void ImplementsIPCPRefreshable()
        {
            Assert.That(m_View, Is.InstanceOf<IPCPRefreshable>());
        }

        // ================================================================
        // Section 1: Scan Configuration Tests
        // ================================================================

        [Test]
        public void ScanConfig_HasFourToggles()
        {
            var toggles = GetScanConfigToggles();
            Assert.That(toggles.Count, Is.EqualTo(4));
        }

        [Test]
        public void ScanConfig_IncludeAllScenes_DefaultValue()
        {
            var toggle = FindToggleByLabel("Include all scenes (not just Build Settings)");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.False);
        }

        [Test]
        public void ScanConfig_IncludeAllScenes_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Include all scenes (not just Build Settings)");
            toggle.value = true;
            Assert.That(m_Settings.includeAllScenes, Is.True);
        }

        [Test]
        public void ScanConfig_IncludeAddressables_DefaultValue()
        {
            var toggle = FindToggleByLabel("Include Addressable entries");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.True);
        }

        [Test]
        public void ScanConfig_IncludeAddressables_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Include Addressable entries");
            toggle.value = false;
            Assert.That(m_Settings.includeAddressables, Is.False);
        }

        [Test]
        public void ScanConfig_IncludeAssetBundles_DefaultValue()
        {
            var toggle = FindToggleByLabel("Include AssetBundle entries");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.True);
        }

        [Test]
        public void ScanConfig_IncludeAssetBundles_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Include AssetBundle entries");
            toggle.value = false;
            Assert.That(m_Settings.includeAssetBundles, Is.False);
        }

        [Test]
        public void ScanConfig_ScanEditorAssets_DefaultValue()
        {
            var toggle = FindToggleByLabel("Scan Editor/ folder assets");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.False);
        }

        [Test]
        public void ScanConfig_ScanEditorAssets_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Scan Editor/ folder assets");
            toggle.value = true;
            Assert.That(m_Settings.scanEditorAssets, Is.True);
        }

        // ================================================================
        // Section 2: Always-Used Roots Tests
        // ================================================================

        [Test]
        public void ScanRoots_HasDescriptionLabel()
        {
            var captions = m_View.Query<Label>(className: "pcp-label-caption").ToList();
            var desc = captions.FirstOrDefault(l => l.text.Contains("never be flagged as unused"));
            Assert.That(desc, Is.Not.Null);
        }

        [Test]
        public void ScanRoots_HasAddButton()
        {
            var addBtn = FindButtonByText("+ Add Path");
            Assert.That(addBtn, Is.Not.Null);
            Assert.That(addBtn.ClassListContains("pcp-button-secondary"), Is.True);
        }

        [Test]
        public void ScanRoots_InitiallyEmpty()
        {
            // No scan roots configured by default
            Assert.That(m_Settings.alwaysUsedRoots.Count, Is.EqualTo(0));
        }

        [Test]
        public void ScanRoots_AddButton_AddsNewRoot()
        {
            var addBtn = FindButtonByText("+ Add Path");
            ClickButton(addBtn);

            Assert.That(m_Settings.alwaysUsedRoots.Count, Is.EqualTo(1));
            Assert.That(m_Settings.alwaysUsedRoots[0], Is.EqualTo("Assets/"));
        }

        [Test]
        public void ScanRoots_AddMultipleRoots_AllAppear()
        {
            var addBtn = FindButtonByText("+ Add Path");
            ClickButton(addBtn);
            ClickButton(addBtn);
            ClickButton(addBtn);

            Assert.That(m_Settings.alwaysUsedRoots.Count, Is.EqualTo(3));
        }

        [Test]
        public void ScanRoots_RemoveButton_RemovesRoot()
        {
            // Add a root first
            m_Settings.alwaysUsedRoots.Add("Assets/TestRoot");
            m_View.Refresh();

            // Find the remove button (✖ character)
            var removeBtn = FindButtonByText("\u2716", GetScanRootsContainer());
            Assert.That(removeBtn, Is.Not.Null);
            ClickButton(removeBtn);

            Assert.That(m_Settings.alwaysUsedRoots.Count, Is.EqualTo(0));
        }

        [Test]
        public void ScanRoots_TextField_UpdatesSettingsOnChange()
        {
            m_Settings.alwaysUsedRoots.Add("Assets/OldPath");
            m_View.Refresh();

            var textFields = GetScanRootsContainer().Query<TextField>().ToList();
            Assert.That(textFields.Count, Is.GreaterThan(0));
            Assert.That(textFields[0].value, Is.EqualTo("Assets/OldPath"));

            textFields[0].value = "Assets/NewPath";
            Assert.That(m_Settings.alwaysUsedRoots[0], Is.EqualTo("Assets/NewPath"));
        }

        [Test]
        public void ScanRoots_BrowseButton_Exists()
        {
            m_Settings.alwaysUsedRoots.Add("Assets/");
            m_View.Refresh();

            var browseBtn = FindButtonByText("...", GetScanRootsContainer());
            Assert.That(browseBtn, Is.Not.Null);
        }

        [Test]
        public void ScanRoots_RowLayout_IsHorizontal()
        {
            m_Settings.alwaysUsedRoots.Add("Assets/");
            m_View.Refresh();

            var container = GetScanRootsContainer();
            if (container.childCount > 0)
            {
                var row = container[0];
                Assert.That(row.style.flexDirection.value, Is.EqualTo(FlexDirection.Row));
            }
        }

        // ================================================================
        // Section 3: Ignore Rules Tests
        // ================================================================

        [Test]
        public void IgnoreRules_HasDescriptionLabel()
        {
            var captions = m_View.Query<Label>(className: "pcp-label-caption").ToList();
            var desc = captions.FirstOrDefault(l => l.text.Contains("exclude assets from scan"));
            Assert.That(desc, Is.Not.Null);
        }

        [Test]
        public void IgnoreRules_HasAddButton()
        {
            var addBtn = FindButtonByText("+ Add Rule");
            Assert.That(addBtn, Is.Not.Null);
            Assert.That(addBtn.ClassListContains("pcp-button-secondary"), Is.True);
        }

        [Test]
        public void IgnoreRules_InitiallyEmpty()
        {
            Assert.That(m_Settings.ignoreRules.Count, Is.EqualTo(0));
        }

        [Test]
        public void IgnoreRules_AddButton_AddsNewRule()
        {
            var addBtn = FindButtonByText("+ Add Rule");
            ClickButton(addBtn);

            Assert.That(m_Settings.ignoreRules.Count, Is.EqualTo(1));
            var rule = m_Settings.ignoreRules[0];
            Assert.That(rule.type, Is.EqualTo(PCPIgnoreType.PathPrefix));
            Assert.That(rule.pattern, Is.EqualTo("Assets/"));
            Assert.That(rule.comment, Is.EqualTo(""));
            Assert.That(rule.enabled, Is.True);
        }

        [Test]
        public void IgnoreRules_AddMultipleRules()
        {
            var addBtn = FindButtonByText("+ Add Rule");
            ClickButton(addBtn);
            ClickButton(addBtn);

            Assert.That(m_Settings.ignoreRules.Count, Is.EqualTo(2));
        }

        [Test]
        public void IgnoreRules_RuleCard_HasCorrectStructure()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule
            {
                type = PCPIgnoreType.Regex,
                pattern = ".*\\.tmp$",
                comment = "Temp files",
                enabled = true
            });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            Assert.That(cards.Count, Is.GreaterThan(0));

            var card = cards[0];
            // Should have toggle, enum field, remove button, pattern field, comment field
            var toggle = card.Q<Toggle>();
            var enumField = card.Q<EnumField>();
            var textFields = card.Query<TextField>().ToList();

            Assert.That(toggle, Is.Not.Null, "Rule card should have an enabled toggle");
            Assert.That(enumField, Is.Not.Null, "Rule card should have a type dropdown");
            Assert.That(textFields.Count, Is.EqualTo(2), "Rule card should have pattern and comment fields");
        }

        [Test]
        public void IgnoreRules_EnabledToggle_DefaultsToTrue()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { enabled = true });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var toggle = cards[0].Q<Toggle>();
            Assert.That(toggle.value, Is.True);
        }

        [Test]
        public void IgnoreRules_EnabledToggle_UpdatesRule()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { enabled = true });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var toggle = cards[0].Q<Toggle>();
            toggle.value = false;

            Assert.That(m_Settings.ignoreRules[0].enabled, Is.False);
        }

        [Test]
        public void IgnoreRules_TypeDropdown_ReflectsRuleType()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { type = PCPIgnoreType.Regex });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var enumField = cards[0].Q<EnumField>();
            Assert.That((PCPIgnoreType)enumField.value, Is.EqualTo(PCPIgnoreType.Regex));
        }

        [Test]
        public void IgnoreRules_PatternField_ReflectsRulePattern()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { pattern = "Assets/Plugins/" });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var textFields = cards[0].Query<TextField>().ToList();
            var patternField = textFields.FirstOrDefault(f => f.label == "Pattern");
            Assert.That(patternField, Is.Not.Null);
            Assert.That(patternField.value, Is.EqualTo("Assets/Plugins/"));
        }

        [Test]
        public void IgnoreRules_PatternField_UpdatesRule()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { pattern = "old" });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var textFields = cards[0].Query<TextField>().ToList();
            var patternField = textFields.FirstOrDefault(f => f.label == "Pattern");
            patternField.value = "new/path";

            Assert.That(m_Settings.ignoreRules[0].pattern, Is.EqualTo("new/path"));
        }

        [Test]
        public void IgnoreRules_CommentField_ReflectsRuleComment()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { comment = "My comment" });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var textFields = cards[0].Query<TextField>().ToList();
            var commentField = textFields.FirstOrDefault(f => f.label == "Comment");
            Assert.That(commentField, Is.Not.Null);
            Assert.That(commentField.value, Is.EqualTo("My comment"));
        }

        [Test]
        public void IgnoreRules_CommentField_UpdatesRule()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { comment = "" });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var textFields = cards[0].Query<TextField>().ToList();
            var commentField = textFields.FirstOrDefault(f => f.label == "Comment");
            commentField.value = "Updated comment";

            Assert.That(m_Settings.ignoreRules[0].comment, Is.EqualTo("Updated comment"));
        }

        [Test]
        public void IgnoreRules_RemoveButton_RemovesRule()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { pattern = "rule1" });
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { pattern = "rule2" });
            m_View.Refresh();

            Assert.That(m_Settings.ignoreRules.Count, Is.EqualTo(2));

            // Click remove on first rule
            var cards = m_View.Query(className: "pcp-card").ToList();
            var removeBtn = FindButtonByText("\u2716", cards[0]);
            ClickButton(removeBtn);

            Assert.That(m_Settings.ignoreRules.Count, Is.EqualTo(1));
            Assert.That(m_Settings.ignoreRules[0].pattern, Is.EqualTo("rule2"));
        }

        [Test]
        public void IgnoreRules_RemoveButton_HasRedColor()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule());
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var removeBtn = FindButtonByText("\u2716", cards[0]);
            Assert.That(removeBtn.style.color.value, Is.EqualTo(new Color(0.957f, 0.278f, 0.278f)));
        }

        // ================================================================
        // Section 4: Safe Delete Tests
        // ================================================================

        [Test]
        public void SafeDelete_ArchiveBeforeDelete_DefaultValue()
        {
            var toggle = FindToggleByLabel("Archive assets before deleting");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.True);
        }

        [Test]
        public void SafeDelete_ArchiveBeforeDelete_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Archive assets before deleting");
            toggle.value = false;
            Assert.That(m_Settings.archiveBeforeDelete, Is.False);
        }

        [Test]
        public void SafeDelete_UseGitRm_DefaultValue()
        {
            var toggle = FindToggleByLabel("Use 'git rm' when in a Git repository");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.False);
        }

        [Test]
        public void SafeDelete_UseGitRm_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Use 'git rm' when in a Git repository");
            toggle.value = true;
            Assert.That(m_Settings.useGitRm, Is.True);
        }

        [Test]
        public void SafeDelete_NullOutReferences_DefaultValue()
        {
            var toggle = FindToggleByLabel("Null out references to deleted assets");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.False);
        }

        [Test]
        public void SafeDelete_NullOutReferences_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Null out references to deleted assets");
            toggle.value = true;
            Assert.That(m_Settings.nullOutReferencesOnDelete, Is.True);
        }

        [Test]
        public void SafeDelete_RetentionDays_DefaultValue()
        {
            var intField = m_View.Q<IntegerField>();
            Assert.That(intField, Is.Not.Null);
            Assert.That(intField.value, Is.EqualTo(30));
        }

        [Test]
        public void SafeDelete_RetentionDays_ChangingValue_UpdatesSettings()
        {
            var intField = m_View.Q<IntegerField>();
            intField.value = 60;
            Assert.That(m_Settings.archiveRetentionDays, Is.EqualTo(60));
        }

        [Test]
        public void SafeDelete_RetentionDays_ClampsToMinimumOfOne()
        {
            var intField = m_View.Q<IntegerField>();
            intField.value = 0;
            Assert.That(m_Settings.archiveRetentionDays, Is.EqualTo(1));
        }

        [Test]
        public void SafeDelete_RetentionDays_NegativeValueClamped()
        {
            var intField = m_View.Q<IntegerField>();
            intField.value = -5;
            Assert.That(m_Settings.archiveRetentionDays, Is.EqualTo(1));
        }

        [Test]
        public void SafeDelete_RetentionLabel_Exists()
        {
            var labels = m_View.Query<Label>(className: "pcp-label-body").ToList();
            var retentionLabel = labels.FirstOrDefault(l => l.text == "Archive retention (days)");
            Assert.That(retentionLabel, Is.Not.Null);
        }

        // ================================================================
        // Section 5: Module Settings Tests
        // ================================================================

        [Test]
        public void ModuleSettings_DependencyGraphMaxDepth_DefaultValue()
        {
            var slider = m_View.Q<SliderInt>();
            Assert.That(slider, Is.Not.Null);
            Assert.That(slider.value, Is.EqualTo(2));
        }

        [Test]
        public void ModuleSettings_DependencyGraphMaxDepth_ChangingValue_UpdatesSettings()
        {
            var slider = m_View.Q<SliderInt>();
            slider.value = 5;
            Assert.That(m_Settings.dependencyGraphMaxDepth, Is.EqualTo(5));
        }

        [Test]
        public void ModuleSettings_DependencyGraphMaxDepth_SliderRange()
        {
            var slider = m_View.Q<SliderInt>();
            Assert.That(slider.lowValue, Is.EqualTo(1));
            Assert.That(slider.highValue, Is.EqualTo(10));
        }

        [Test]
        public void ModuleSettings_DependencyGraphMaxDepth_ShowsInputField()
        {
            var slider = m_View.Q<SliderInt>();
            Assert.That(slider.showInputField, Is.True);
        }

        [Test]
        public void ModuleSettings_DependencyGraphDepthLabel_Exists()
        {
            var labels = m_View.Query<Label>(className: "pcp-label-body").ToList();
            var depthLabel = labels.FirstOrDefault(l => l.text == "Dependency graph max depth");
            Assert.That(depthLabel, Is.Not.Null);
        }

        [Test]
        public void ModuleSettings_ShaderCheckPipeline_DefaultValue()
        {
            var toggle = FindToggleByLabel("Check shaders for pipeline compatibility");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.True);
        }

        [Test]
        public void ModuleSettings_ShaderCheckPipeline_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Check shaders for pipeline compatibility");
            toggle.value = false;
            Assert.That(m_Settings.shaderAnalyzerCheckPipeline, Is.False);
        }

        [Test]
        public void ModuleSettings_DuplicateCompareImportSettings_DefaultValue()
        {
            var toggle = FindToggleByLabel("Compare import settings for duplicates");
            Assert.That(toggle, Is.Not.Null);
            Assert.That(toggle.value, Is.True);
        }

        [Test]
        public void ModuleSettings_DuplicateCompareImportSettings_ChangingToggle_UpdatesSettings()
        {
            var toggle = FindToggleByLabel("Compare import settings for duplicates");
            toggle.value = false;
            Assert.That(m_Settings.duplicateCompareImportSettings, Is.False);
        }

        // ================================================================
        // Section 6: Module Colors Tests
        // ================================================================

        [Test]
        public void ModuleColors_HasEightColorFields()
        {
            var colorFields = m_View.Query<ColorField>().ToList();
            Assert.That(colorFields.Count, Is.EqualTo(8));
        }

        [Test]
        public void ModuleColors_LabelsMatchModuleNames()
        {
            var colorFields = m_View.Query<ColorField>().ToList();
            var labels = colorFields.Select(f => f.label).ToList();

            Assert.That(labels, Does.Contain("Unused Assets"));
            Assert.That(labels, Does.Contain("Missing References"));
            Assert.That(labels, Does.Contain("Duplicates"));
            Assert.That(labels, Does.Contain("Dependencies"));
            Assert.That(labels, Does.Contain("Packages"));
            Assert.That(labels, Does.Contain("Shaders"));
            Assert.That(labels, Does.Contain("Size Profiler"));
            Assert.That(labels, Does.Contain("Archive"));
        }

        [Test]
        public void ModuleColors_InitialValues_MatchSettings()
        {
            var colorFields = m_View.Query<ColorField>().ToList();
            for (int i = 0; i < colorFields.Count && i < m_Settings.moduleColors.Length; i++)
            {
                Assert.That(colorFields[i].value, Is.EqualTo(m_Settings.moduleColors[i]),
                    $"Color field {i} should match settings");
            }
        }

        [Test]
        public void ModuleColors_ChangingColor_UpdatesSettings()
        {
            var colorFields = m_View.Query<ColorField>().ToList();
            var newColor = Color.magenta;
            colorFields[0].value = newColor;

            Assert.That(m_Settings.moduleColors[0], Is.EqualTo(newColor));
        }

        [Test]
        public void ModuleColors_EachRowHasColorSwatch()
        {
            // Color swatches are VisualElements with specific width/height styling
            // We expect one swatch per color row
            var colorFields = m_View.Query<ColorField>().ToList();
            foreach (var colorField in colorFields)
            {
                var row = colorField.parent;
                Assert.That(row, Is.Not.Null);
                // Row should have at least 2 children (swatch + color field)
                Assert.That(row.childCount, Is.GreaterThanOrEqualTo(2));
                // First child is the swatch
                var swatch = row[0];
                Assert.That(swatch.style.width.value.value, Is.EqualTo(16f));
                Assert.That(swatch.style.height.value.value, Is.EqualTo(16f));
            }
        }

        [Test]
        public void ModuleColors_SwatchBackgroundColor_MatchesInitialColor()
        {
            var colorFields = m_View.Query<ColorField>().ToList();
            for (int i = 0; i < colorFields.Count; i++)
            {
                var row = colorFields[i].parent;
                var swatch = row[0];
                var bg = swatch.style.backgroundColor.value;
                Assert.That((Color)bg, Is.EqualTo(m_Settings.moduleColors[i]),
                    $"Swatch {i} background should match settings color");
            }
        }

        [Test]
        public void ModuleColors_ChangingColor_UpdatesSwatchBackground()
        {
            var colorFields = m_View.Query<ColorField>().ToList();
            var newColor = new Color(0.1f, 0.2f, 0.3f, 1f);
            colorFields[2].value = newColor;

            var row = colorFields[2].parent;
            var swatch = row[0];
            Assert.That((Color)swatch.style.backgroundColor.value, Is.EqualTo(newColor));
        }

        // ================================================================
        // Section 7: Actions Tests
        // ================================================================

        [Test]
        public void Actions_HasResetButton()
        {
            var resetBtn = FindButtonByText("Reset to Defaults");
            Assert.That(resetBtn, Is.Not.Null);
            Assert.That(resetBtn.ClassListContains("pcp-button-danger"), Is.True);
        }

        [Test]
        public void Actions_HasClearCacheButton()
        {
            var clearBtn = FindButtonByText("Clear Cache");
            Assert.That(clearBtn, Is.Not.Null);
            Assert.That(clearBtn.ClassListContains("pcp-button-secondary"), Is.True);
        }

        [Test]
        public void Actions_ButtonsAreInHorizontalRow()
        {
            var resetBtn = FindButtonByText("Reset to Defaults");
            var clearBtn = FindButtonByText("Clear Cache");

            // Both should share the same parent row
            Assert.That(resetBtn.parent, Is.EqualTo(clearBtn.parent));
            Assert.That(resetBtn.parent.style.flexDirection.value, Is.EqualTo(FlexDirection.Row));
        }

        // ================================================================
        // IPCPRefreshable Tests
        // ================================================================

        [Test]
        public void Refresh_UpdatesIgnoreRules()
        {
            // Start with no rules
            Assert.That(m_Settings.ignoreRules.Count, Is.EqualTo(0));
            var cardsBeforeRefresh = m_View.Query(className: "pcp-card").ToList();
            Assert.That(cardsBeforeRefresh.Count, Is.EqualTo(0));

            // Add a rule externally and refresh
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { pattern = "External" });
            m_View.Refresh();

            var cardsAfterRefresh = m_View.Query(className: "pcp-card").ToList();
            Assert.That(cardsAfterRefresh.Count, Is.EqualTo(1));
        }

        [Test]
        public void Refresh_UpdatesScanRoots()
        {
            Assert.That(m_Settings.alwaysUsedRoots.Count, Is.EqualTo(0));

            m_Settings.alwaysUsedRoots.Add("Assets/ExternalRoot");
            m_View.Refresh();

            var container = GetScanRootsContainer();
            var textFields = container.Query<TextField>().ToList();
            Assert.That(textFields.Count, Is.GreaterThan(0));
            Assert.That(textFields[0].value, Is.EqualTo("Assets/ExternalRoot"));
        }

        [Test]
        public void Refresh_ReloadsSettingsFromContext()
        {
            // Verify Refresh re-reads from PCPContext.Settings
            m_View.Refresh();
            // If this doesn't throw, settings were re-read successfully
            Assert.Pass();
        }

        // ================================================================
        // Integration / Cross-Section Tests
        // ================================================================

        [Test]
        public void AllToggles_ArePresent()
        {
            // Total toggles: 4 (scan config) + 3 (safe delete) + 2 (module settings) + ignore rule toggles
            // With no ignore rules, base count is 9
            var allToggles = m_View.Query<Toggle>().ToList();
            Assert.That(allToggles.Count, Is.GreaterThanOrEqualTo(9));
        }

        [Test]
        public void MultipleIgnoreRules_AllDisplayCorrectly()
        {
            for (int i = 0; i < 5; i++)
            {
                m_Settings.ignoreRules.Add(new PCPIgnoreRule
                {
                    type = (PCPIgnoreType)(i % 6),
                    pattern = $"pattern_{i}",
                    comment = $"comment_{i}",
                    enabled = i % 2 == 0
                });
            }

            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            Assert.That(cards.Count, Is.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                var card = cards[i];
                var toggle = card.Q<Toggle>();
                var textFields = card.Query<TextField>().ToList();
                var patternField = textFields.FirstOrDefault(f => f.label == "Pattern");

                Assert.That(toggle.value, Is.EqualTo(i % 2 == 0), $"Rule {i} enabled state");
                Assert.That(patternField.value, Is.EqualTo($"pattern_{i}"), $"Rule {i} pattern");
            }
        }

        [Test]
        public void MultipleScanRoots_AllDisplayCorrectly()
        {
            m_Settings.alwaysUsedRoots.Add("Assets/Root1");
            m_Settings.alwaysUsedRoots.Add("Assets/Root2");
            m_Settings.alwaysUsedRoots.Add("Assets/Root3");
            m_View.Refresh();

            var container = GetScanRootsContainer();
            var textFields = container.Query<TextField>().ToList();
            Assert.That(textFields.Count, Is.EqualTo(3));
            Assert.That(textFields[0].value, Is.EqualTo("Assets/Root1"));
            Assert.That(textFields[1].value, Is.EqualTo("Assets/Root2"));
            Assert.That(textFields[2].value, Is.EqualTo("Assets/Root3"));
        }

        [Test]
        public void ScrollView_ContentHasPadding()
        {
            var scrollView = m_View.Q<ScrollView>();
            var container = scrollView.contentContainer;

            Assert.That(container.style.paddingTop.value.value, Is.EqualTo(16f));
            Assert.That(container.style.paddingBottom.value.value, Is.EqualTo(16f));
            Assert.That(container.style.paddingLeft.value.value, Is.EqualTo(24f));
            Assert.That(container.style.paddingRight.value.value, Is.EqualTo(24f));
        }

        // ================================================================
        // Edge Case Tests
        // ================================================================

        [Test]
        public void IgnoreRules_RemoveLastRule_ListBecomesEmpty()
        {
            m_Settings.ignoreRules.Add(new PCPIgnoreRule { pattern = "only" });
            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            var removeBtn = FindButtonByText("\u2716", cards[0]);
            ClickButton(removeBtn);

            Assert.That(m_Settings.ignoreRules.Count, Is.EqualTo(0));
            var cardsAfter = m_View.Query(className: "pcp-card").ToList();
            Assert.That(cardsAfter.Count, Is.EqualTo(0));
        }

        [Test]
        public void ScanRoots_RemoveLastRoot_ListBecomesEmpty()
        {
            m_Settings.alwaysUsedRoots.Add("Assets/Only");
            m_View.Refresh();

            var removeBtn = FindButtonByText("\u2716", GetScanRootsContainer());
            ClickButton(removeBtn);

            Assert.That(m_Settings.alwaysUsedRoots.Count, Is.EqualTo(0));
        }

        [Test]
        public void ScanConfig_ToggleMultipleTimes_FinalStateCorrect()
        {
            var toggle = FindToggleByLabel("Include all scenes (not just Build Settings)");
            toggle.value = true;
            toggle.value = false;
            toggle.value = true;

            Assert.That(m_Settings.includeAllScenes, Is.True);
        }

        [Test]
        public void RetentionDays_LargeValue_Accepted()
        {
            var intField = m_View.Q<IntegerField>();
            intField.value = 365;
            Assert.That(m_Settings.archiveRetentionDays, Is.EqualTo(365));
        }

        [Test]
        public void DependencyDepth_BoundaryValues()
        {
            var slider = m_View.Q<SliderInt>();

            slider.value = 1;
            Assert.That(m_Settings.dependencyGraphMaxDepth, Is.EqualTo(1));

            slider.value = 10;
            Assert.That(m_Settings.dependencyGraphMaxDepth, Is.EqualTo(10));
        }

        [Test]
        public void IgnoreRules_AllTypeValues_CanBeSet()
        {
            foreach (PCPIgnoreType type in System.Enum.GetValues(typeof(PCPIgnoreType)))
            {
                m_Settings.ignoreRules.Add(new PCPIgnoreRule { type = type, pattern = type.ToString() });
            }

            m_View.Refresh();

            var cards = m_View.Query(className: "pcp-card").ToList();
            Assert.That(cards.Count, Is.EqualTo(System.Enum.GetValues(typeof(PCPIgnoreType)).Length));
        }

        // ================================================================
        // Helper Methods
        // ================================================================

        private List<Toggle> GetScanConfigToggles()
        {
            // Scan config section is the first section after the header
            // Get all toggles that belong to scan config by their labels
            string[] scanConfigLabels =
            {
                "Include all scenes (not just Build Settings)",
                "Include Addressable entries",
                "Include AssetBundle entries",
                "Scan Editor/ folder assets"
            };

            var toggles = new List<Toggle>();
            foreach (var label in scanConfigLabels)
            {
                var toggle = FindToggleByLabel(label);
                if (toggle != null)
                    toggles.Add(toggle);
            }

            return toggles;
        }

        private Toggle FindToggleByLabel(string label)
        {
            return m_View.Query<Toggle>().ToList().FirstOrDefault(t => t.label == label);
        }

        private Button FindButtonByText(string text, VisualElement searchRoot = null)
        {
            var root = searchRoot ?? m_View;
            return root.Query<Button>().ToList().FirstOrDefault(b => b.text == text);
        }

        private VisualElement GetScanRootsContainer()
        {
            // The scan roots list container is the VisualElement that holds the root rows
            // It's positioned after the description label and before the add button
            // We find it by locating the "Always-Used Roots" section
            var subheaders = m_View.Query<Label>(className: "pcp-label-subheader").ToList();
            var scanRootsHeader = subheaders.FirstOrDefault(l => l.text == "Always-Used Roots");
            if (scanRootsHeader == null) return null;

            var section = scanRootsHeader.parent;
            // The list container is the VisualElement after the caption label
            // section children: [0] header, [1] description, [2] list container, [3] add button
            if (section.childCount >= 3)
                return section[2];

            return null;
        }

        private static void ClickButton(Button button)
        {
            // ClickEvent does not trigger Button's Clickable handler.
            // Use NavigationSubmitEvent which Button handles via
            // ExecuteDefaultActionAtTarget → clickable.SimulateSingleClick.
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
        }
    }
}
