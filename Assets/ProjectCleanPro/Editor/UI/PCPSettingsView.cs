using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Settings view providing controls for scan configuration, ignore rules,
    /// safe-delete options, module-specific settings, and module accent colors.
    /// All changes are auto-saved to <see cref="PCPSettings"/>.
    /// </summary>
    public sealed class PCPSettingsView : VisualElement
    {
        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private PCPSettings m_Settings;
        private VisualElement m_IgnoreRulesList;
        private VisualElement m_ScanRootsList;

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPSettingsView()
        {
            style.flexGrow = 1;

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("pcp-scroll-view");
            Add(scrollView);

            var container = scrollView.contentContainer;
            container.style.paddingTop = 16;
            container.style.paddingBottom = 16;
            container.style.paddingLeft = 24;
            container.style.paddingRight = 24;

            m_Settings = PCPContext.Settings;

            // Header
            var header = new Label("Settings");
            header.AddToClassList("pcp-label-header");
            container.Add(header);

            // Section 1: Scan Configuration
            BuildScanConfigSection(container);

            AddSeparator(container);

            // Section 2: Custom Scan Roots
            BuildCustomScanRootsSection(container);

            AddSeparator(container);

            // Section 3: Ignore Rules
            BuildIgnoreRulesSection(container);

            AddSeparator(container);

            // Section 4: Safe Delete
            BuildSafeDeleteSection(container);

            AddSeparator(container);

            // Section 5: Module Settings
            BuildModuleSettingsSection(container);

            AddSeparator(container);

            // Section 6: Module Colors
            BuildModuleColorsSection(container);

            AddSeparator(container);

            // Section 7: Actions
            BuildActionsSection(container);
        }

        // --------------------------------------------------------------------
        // Section 1: Scan Configuration
        // --------------------------------------------------------------------

        private void BuildScanConfigSection(VisualElement parent)
        {
            var section = CreateSection("Scan Configuration");
            parent.Add(section);

            AddToggle(section, "Include all scenes (not just Build Settings)",
                m_Settings.includeAllScenes,
                val => { m_Settings.includeAllScenes = val; SaveSettings(); });

            AddToggle(section, "Include Addressable entries",
                m_Settings.includeAddressables,
                val => { m_Settings.includeAddressables = val; SaveSettings(); });

            AddToggle(section, "Include AssetBundle entries",
                m_Settings.includeAssetBundles,
                val => { m_Settings.includeAssetBundles = val; SaveSettings(); });

            AddToggle(section, "Scan Editor/ folder assets",
                m_Settings.scanEditorAssets,
                val => { m_Settings.scanEditorAssets = val; SaveSettings(); });
        }

        // --------------------------------------------------------------------
        // Section 2: Custom Scan Roots
        // --------------------------------------------------------------------

        private void BuildCustomScanRootsSection(VisualElement parent)
        {
            var section = CreateSection("Custom Scan Roots");
            parent.Add(section);

            var description = new Label("Additional folders to include as scan entry points.");
            description.AddToClassList("pcp-label-caption");
            description.style.marginBottom = 8;
            section.Add(description);

            m_ScanRootsList = new VisualElement();
            m_ScanRootsList.style.marginBottom = 8;
            section.Add(m_ScanRootsList);

            RefreshScanRoots();

            var addBtn = new Button(AddScanRoot);
            addBtn.text = "+ Add Scan Root";
            addBtn.AddToClassList("pcp-button-secondary");
            addBtn.style.alignSelf = Align.FlexStart;
            section.Add(addBtn);
        }

        private void RefreshScanRoots()
        {
            m_ScanRootsList.Clear();

            for (int i = 0; i < m_Settings.customScanRoots.Count; i++)
            {
                int index = i;
                string rootPath = m_Settings.customScanRoots[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;

                var pathField = new TextField();
                pathField.value = rootPath;
                pathField.style.flexGrow = 1;
                pathField.RegisterValueChangedCallback(evt =>
                {
                    if (index < m_Settings.customScanRoots.Count)
                    {
                        m_Settings.customScanRoots[index] = evt.newValue;
                        SaveSettings();
                    }
                });
                row.Add(pathField);

                var browseBtn = new Button(() =>
                {
                    string folder = EditorUtility.OpenFolderPanel("Select Scan Root", "Assets", "");
                    if (!string.IsNullOrEmpty(folder))
                    {
                        // Convert absolute to project-relative
                        string dataPath = Application.dataPath;
                        if (folder.StartsWith(dataPath))
                            folder = "Assets" + folder.Substring(dataPath.Length);

                        if (index < m_Settings.customScanRoots.Count)
                        {
                            m_Settings.customScanRoots[index] = folder;
                            SaveSettings();
                            RefreshScanRoots();
                        }
                    }
                });
                browseBtn.text = "...";
                browseBtn.style.width = 30;
                browseBtn.style.marginLeft = 4;
                row.Add(browseBtn);

                var removeBtn = new Button(() =>
                {
                    if (index < m_Settings.customScanRoots.Count)
                    {
                        m_Settings.customScanRoots.RemoveAt(index);
                        SaveSettings();
                        RefreshScanRoots();
                    }
                });
                removeBtn.text = "\u2716";
                removeBtn.style.width = 26;
                removeBtn.style.marginLeft = 4;
                removeBtn.style.color = new Color(0.957f, 0.278f, 0.278f);
                row.Add(removeBtn);

                m_ScanRootsList.Add(row);
            }
        }

        private void AddScanRoot()
        {
            m_Settings.customScanRoots.Add("Assets/");
            SaveSettings();
            RefreshScanRoots();
        }

        // --------------------------------------------------------------------
        // Section 3: Ignore Rules
        // --------------------------------------------------------------------

        private void BuildIgnoreRulesSection(VisualElement parent)
        {
            var section = CreateSection("Ignore Rules");
            parent.Add(section);

            var description = new Label("Rules that exclude assets from scan results.");
            description.AddToClassList("pcp-label-caption");
            description.style.marginBottom = 8;
            section.Add(description);

            m_IgnoreRulesList = new VisualElement();
            m_IgnoreRulesList.style.marginBottom = 8;
            section.Add(m_IgnoreRulesList);

            RefreshIgnoreRules();

            var addBtn = new Button(AddIgnoreRule);
            addBtn.text = "+ Add Rule";
            addBtn.AddToClassList("pcp-button-secondary");
            addBtn.style.alignSelf = Align.FlexStart;
            section.Add(addBtn);
        }

        private void RefreshIgnoreRules()
        {
            m_IgnoreRulesList.Clear();

            for (int i = 0; i < m_Settings.ignoreRules.Count; i++)
            {
                int index = i;
                PCPIgnoreRule rule = m_Settings.ignoreRules[i];

                var ruleRow = new VisualElement();
                ruleRow.AddToClassList("pcp-card");
                ruleRow.style.paddingTop = 8;
                ruleRow.style.paddingBottom = 8;
                ruleRow.style.paddingLeft = 12;
                ruleRow.style.paddingRight = 12;
                ruleRow.style.marginTop = 2;
                ruleRow.style.marginBottom = 2;

                // Top row: enabled toggle + type dropdown + remove button
                var topRow = new VisualElement();
                topRow.style.flexDirection = FlexDirection.Row;
                topRow.style.alignItems = Align.Center;
                topRow.style.marginBottom = 4;

                var enabledToggle = new Toggle();
                enabledToggle.value = rule.enabled;
                enabledToggle.style.marginRight = 8;
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    rule.enabled = evt.newValue;
                    SaveSettings();
                });
                topRow.Add(enabledToggle);

                var typeDropdown = new EnumField("Type", rule.type);
                typeDropdown.style.flexGrow = 1;
                typeDropdown.style.minWidth = 120;
                typeDropdown.RegisterValueChangedCallback(evt =>
                {
                    rule.type = (PCPIgnoreType)evt.newValue;
                    SaveSettings();
                });
                topRow.Add(typeDropdown);

                var removeBtn = new Button(() =>
                {
                    if (index < m_Settings.ignoreRules.Count)
                    {
                        m_Settings.ignoreRules.RemoveAt(index);
                        SaveSettings();
                        RefreshIgnoreRules();
                    }
                });
                removeBtn.text = "\u2716";
                removeBtn.style.width = 26;
                removeBtn.style.marginLeft = 8;
                removeBtn.style.color = new Color(0.957f, 0.278f, 0.278f);
                topRow.Add(removeBtn);

                ruleRow.Add(topRow);

                // Bottom row: pattern + comment
                var bottomRow = new VisualElement();
                bottomRow.style.flexDirection = FlexDirection.Row;
                bottomRow.style.alignItems = Align.Center;

                var patternField = new TextField("Pattern");
                patternField.value = rule.pattern;
                patternField.style.flexGrow = 1;
                patternField.style.marginRight = 8;
                patternField.RegisterValueChangedCallback(evt =>
                {
                    rule.pattern = evt.newValue;
                    SaveSettings();
                });
                bottomRow.Add(patternField);

                var commentField = new TextField("Comment");
                commentField.value = rule.comment;
                commentField.style.flexGrow = 1;
                commentField.RegisterValueChangedCallback(evt =>
                {
                    rule.comment = evt.newValue;
                    SaveSettings();
                });
                bottomRow.Add(commentField);

                ruleRow.Add(bottomRow);

                m_IgnoreRulesList.Add(ruleRow);
            }
        }

        private void AddIgnoreRule()
        {
            var rule = new PCPIgnoreRule
            {
                type = PCPIgnoreType.PathPrefix,
                pattern = "Assets/",
                comment = "",
                enabled = true
            };
            m_Settings.ignoreRules.Add(rule);
            SaveSettings();
            RefreshIgnoreRules();
        }

        // --------------------------------------------------------------------
        // Section 4: Safe Delete
        // --------------------------------------------------------------------

        private void BuildSafeDeleteSection(VisualElement parent)
        {
            var section = CreateSection("Safe Delete");
            parent.Add(section);

            AddToggle(section, "Archive assets before deleting",
                m_Settings.archiveBeforeDelete,
                val => { m_Settings.archiveBeforeDelete = val; SaveSettings(); });

            AddToggle(section, "Use 'git rm' when in a Git repository",
                m_Settings.useGitRm,
                val => { m_Settings.useGitRm = val; SaveSettings(); });

            AddToggle(section, "Null out references to deleted assets",
                m_Settings.nullOutReferencesOnDelete,
                val => { m_Settings.nullOutReferencesOnDelete = val; SaveSettings(); });

            // Retention days
            var retentionRow = new VisualElement();
            retentionRow.style.flexDirection = FlexDirection.Row;
            retentionRow.style.alignItems = Align.Center;
            retentionRow.style.marginTop = 4;

            var retentionLabel = new Label("Archive retention (days)");
            retentionLabel.style.minWidth = 200;
            retentionLabel.AddToClassList("pcp-label-body");
            retentionRow.Add(retentionLabel);

            var retentionField = new IntegerField();
            retentionField.value = m_Settings.archiveRetentionDays;
            retentionField.style.width = 80;
            retentionField.RegisterValueChangedCallback(evt =>
            {
                m_Settings.archiveRetentionDays = Mathf.Max(1, evt.newValue);
                SaveSettings();
            });
            retentionRow.Add(retentionField);

            section.Add(retentionRow);
        }

        // --------------------------------------------------------------------
        // Section 5: Module Settings
        // --------------------------------------------------------------------

        private void BuildModuleSettingsSection(VisualElement parent)
        {
            var section = CreateSection("Module Settings");
            parent.Add(section);

            // Dependency graph max depth
            var depthRow = new VisualElement();
            depthRow.style.flexDirection = FlexDirection.Row;
            depthRow.style.alignItems = Align.Center;
            depthRow.style.marginBottom = 8;

            var depthLabel = new Label("Dependency graph max depth");
            depthLabel.style.minWidth = 200;
            depthLabel.AddToClassList("pcp-label-body");
            depthRow.Add(depthLabel);

            var depthSlider = new SliderInt(1, 10);
            depthSlider.value = m_Settings.dependencyGraphMaxDepth;
            depthSlider.style.flexGrow = 1;
            depthSlider.style.minWidth = 120;
            depthSlider.showInputField = true;
            depthSlider.RegisterValueChangedCallback(evt =>
            {
                m_Settings.dependencyGraphMaxDepth = evt.newValue;
                SaveSettings();
            });
            depthRow.Add(depthSlider);

            section.Add(depthRow);

            // Shader analyzer check pipeline
            AddToggle(section, "Check shaders for pipeline compatibility",
                m_Settings.shaderAnalyzerCheckPipeline,
                val => { m_Settings.shaderAnalyzerCheckPipeline = val; SaveSettings(); });

            // Duplicate compare import settings
            AddToggle(section, "Compare import settings for duplicates",
                m_Settings.duplicateCompareImportSettings,
                val => { m_Settings.duplicateCompareImportSettings = val; SaveSettings(); });
        }

        // --------------------------------------------------------------------
        // Section 6: Module Colors
        // --------------------------------------------------------------------

        private void BuildModuleColorsSection(VisualElement parent)
        {
            var section = CreateSection("Module Accent Colors");
            parent.Add(section);

            string[] colorNames = new[]
            {
                "Unused Assets",
                "Missing References",
                "Shader Analyzer",
                "Duplicate Detector",
                "Texture Optimizer",
                "Dependency Graph",
                "Audio Analyzer",
                "Build Report",
            };

            for (int i = 0; i < colorNames.Length && i < m_Settings.moduleColors.Length; i++)
            {
                int colorIndex = i;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;

                // Color swatch preview
                var swatch = new VisualElement();
                swatch.style.width = 16;
                swatch.style.height = 16;
                swatch.style.borderTopLeftRadius = 3;
                swatch.style.borderTopRightRadius = 3;
                swatch.style.borderBottomLeftRadius = 3;
                swatch.style.borderBottomRightRadius = 3;
                swatch.style.marginRight = 8;
                swatch.style.backgroundColor = m_Settings.moduleColors[colorIndex];
                row.Add(swatch);

                var colorField = new ColorField(colorNames[i]);
                colorField.value = m_Settings.moduleColors[colorIndex];
                colorField.style.flexGrow = 1;
                colorField.RegisterValueChangedCallback(evt =>
                {
                    if (colorIndex < m_Settings.moduleColors.Length)
                    {
                        m_Settings.moduleColors[colorIndex] = evt.newValue;
                        swatch.style.backgroundColor = evt.newValue;
                        SaveSettings();
                    }
                });
                row.Add(colorField);

                section.Add(row);
            }
        }

        // --------------------------------------------------------------------
        // Section 7: Actions
        // --------------------------------------------------------------------

        private void BuildActionsSection(VisualElement parent)
        {
            var section = CreateSection("Actions");
            parent.Add(section);

            var actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.marginTop = 8;

            var resetBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog(
                    "Reset to Defaults",
                    "This will reset all ProjectCleanPro settings to their default values. Continue?",
                    "Reset", "Cancel"))
                {
                    ResetToDefaults();
                }
            });
            resetBtn.text = "Reset to Defaults";
            resetBtn.AddToClassList("pcp-button-danger");
            resetBtn.style.marginRight = 8;
            actionsRow.Add(resetBtn);

            var clearCacheBtn = new Button(() =>
            {
                if (EditorUtility.DisplayDialog(
                    "Clear Cache",
                    "This will clear the incremental scan cache. The next scan will process all assets. Continue?",
                    "Clear", "Cancel"))
                {
                    PCPContext.ScanCache.Clear();
                    Debug.Log("[ProjectCleanPro] Scan cache cleared.");
                }
            });
            clearCacheBtn.text = "Clear Cache";
            clearCacheBtn.AddToClassList("pcp-button-secondary");
            actionsRow.Add(clearCacheBtn);

            section.Add(actionsRow);
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private VisualElement CreateSection(string title)
        {
            var section = new VisualElement();
            section.style.marginBottom = 8;

            var header = new Label(title);
            header.AddToClassList("pcp-label-subheader");
            header.style.marginBottom = 8;
            section.Add(header);

            return section;
        }

        private void AddToggle(VisualElement parent, string label, bool initialValue, Action<bool> onChanged)
        {
            var toggle = new Toggle(label);
            toggle.value = initialValue;
            toggle.style.marginBottom = 4;
            toggle.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            parent.Add(toggle);
        }

        private void AddSeparator(VisualElement parent)
        {
            var sep = new VisualElement();
            sep.AddToClassList("pcp-separator");
            parent.Add(sep);
        }

        private void SaveSettings()
        {
            if (m_Settings != null)
                m_Settings.Save();
        }

        private void ResetToDefaults()
        {
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
            m_Settings.customScanRoots.Clear();

            SaveSettings();

            // Rebuild the entire view
            Clear();
            var fresh = new PCPSettingsView();
            while (fresh.childCount > 0)
                Add(fresh[0]);

            Debug.Log("[ProjectCleanPro] Settings reset to defaults.");
        }
    }
}
