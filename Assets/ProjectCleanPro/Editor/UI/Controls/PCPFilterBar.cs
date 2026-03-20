using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Immutable snapshot of the current filter state, emitted by <see cref="PCPFilterBar"/>
    /// whenever any filter control changes.
    /// </summary>
    public sealed class PCPFilterState
    {
        public string searchText = string.Empty;
        public HashSet<string> activeTypes = new HashSet<string>();
        /// <summary>
        /// Status filter string matching <see cref="PCPRowData.status"/> (case-insensitive).
        /// Null means "show all".
        /// </summary>
        public string statusFilter;
        public long? minSize;
        public long? maxSize;

        /// <summary>
        /// Returns true if no filters are applied (default state).
        /// </summary>
        public bool IsDefault =>
            string.IsNullOrEmpty(searchText) &&
            (activeTypes == null || activeTypes.Count == 0) &&
            statusFilter == null &&
            !minSize.HasValue &&
            !maxSize.HasValue;

        public PCPFilterState Clone()
        {
            return new PCPFilterState
            {
                searchText = searchText,
                activeTypes = new HashSet<string>(activeTypes),
                statusFilter = statusFilter,
                minSize = minSize,
                maxSize = maxSize
            };
        }
    }

    /// <summary>
    /// Filter bar control containing search field, type filter chips, and severity dropdown.
    /// Emits <see cref="onFilterChanged"/> whenever any filter value changes.
    /// </summary>
#if UNITY_2023_2_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class PCPFilterBar : VisualElement
    {
#if !UNITY_2023_2_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PCPFilterBar, UxmlTraits> { }
#endif

        // USS class names
        public const string UssClassName = "pcp-filter-bar";
        public const string SearchFieldUssClassName = "pcp-filter-bar__search";
        public const string ChipContainerUssClassName = "pcp-filter-bar__chips";
        public const string ChipUssClassName = "pcp-filter-bar__chip";
        public const string ChipActiveUssClassName = "pcp-filter-bar__chip--active";
        public const string SeverityDropdownUssClassName = "pcp-filter-bar__severity";
        public const string ClearButtonUssClassName = "pcp-filter-bar__clear";

        // Active/inactive chip colors (inline styles override USS for guaranteed visibility)
        private static readonly Color k_ChipActiveBg     = new Color(0.337f, 0.612f, 0.839f, 1f);
        private static readonly Color k_ChipActiveText   = Color.white;
        private static readonly Color k_ChipActiveBorder = new Color(0.337f, 0.612f, 0.839f, 1f);
        private static readonly Color k_ChipInactiveBg     = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color k_ChipInactiveText   = new Color(0.66f, 0.66f, 0.66f, 1f);
        private static readonly Color k_ChipInactiveBorder = new Color(0.33f, 0.33f, 0.33f, 1f);

        // All available type chips
        private static readonly string[] k_TypeChips = new[]
        {
            "All", "Texture", "Material", "Mesh", "Audio", "Script", "Prefab", "Scene", "Other"
        };

        // Internal state
        private readonly TextField _searchField;
        private readonly VisualElement _chipContainer;
        private PopupField<string> _statusDropdown;
        private readonly VisualElement _statusDropdownContainer;
        private readonly Button _clearButton;
        private readonly Label _activeFiltersLabel;
        private readonly Dictionary<string, Button> _chipButtons = new Dictionary<string, Button>();
        private readonly HashSet<string> _activeTypes = new HashSet<string>();
        private string _statusAllLabel = "All Statuses";
        private bool _suppressEvents;

        /// <summary>Raised whenever any filter value changes.</summary>
        public event Action<PCPFilterState> onFilterChanged;

        /// <summary>The current search text.</summary>
        public string SearchText
        {
            get => _searchField.value;
            set => _searchField.value = value ?? string.Empty;
        }

        /// <summary>Read-only snapshot of currently active type filters.</summary>
        public IReadOnlyCollection<string> ActiveTypes => _activeTypes;

        /// <summary>
        /// Replaces the status dropdown choices with the given values.
        /// Each view should call this to provide its own set of status labels.
        /// </summary>
        /// <param name="allLabel">Label for the "show all" option (e.g. "All Severities").</param>
        /// <param name="choices">Status strings that match <see cref="PCPRowData.status"/> values.</param>
        public void SetStatusChoices(string allLabel, params string[] choices)
        {
            BuildStatusDropdown(allLabel, choices);
        }

        public PCPFilterBar()
        {
            AddToClassList(UssClassName);
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            style.alignItems = Align.Center;
            style.paddingLeft = 4;
            style.paddingRight = 4;
            style.paddingTop = 4;
            style.paddingBottom = 4;

            // ----- Search field -----
            _searchField = new TextField
            {
                name = "pcp-filter-search",
                value = string.Empty
            };
            _searchField.AddToClassList(SearchFieldUssClassName);
            _searchField.style.minWidth = 150;
            _searchField.style.maxWidth = 300;
            _searchField.style.marginRight = 8;
            _searchField.textEdition.placeholder = "Search by name or path...";
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            Add(_searchField);

            // ----- Type filter chips -----
            _chipContainer = new VisualElement
            {
                name = "pcp-filter-chips"
            };
            _chipContainer.AddToClassList(ChipContainerUssClassName);
            _chipContainer.style.flexDirection = FlexDirection.Row;
            _chipContainer.style.flexWrap = Wrap.Wrap;
            _chipContainer.style.alignItems = Align.Center;
            _chipContainer.style.marginRight = 8;

            foreach (string chipType in k_TypeChips)
            {
                var chip = CreateChip(chipType);
                _chipButtons[chipType] = chip;
                _chipContainer.Add(chip);
            }

            Add(_chipContainer);

            // ----- Status dropdown (container allows replacement via SetStatusChoices) -----
            _statusDropdownContainer = new VisualElement();
            _statusDropdownContainer.style.marginRight = 8;
            Add(_statusDropdownContainer);

            // Default: severity-based choices
            BuildStatusDropdown("All Severities", new[] { "ERROR", "WARNING", "INFO" });

            // ----- Clear button -----
            _clearButton = new Button(Reset)
            {
                text = "Clear Filters",
                name = "pcp-filter-clear"
            };
            _clearButton.AddToClassList(ClearButtonUssClassName);
            _clearButton.style.marginLeft = 4;
            Add(_clearButton);

            // ----- Active filters label -----
            _activeFiltersLabel = new Label();
            _activeFiltersLabel.style.fontSize = 10;
            _activeFiltersLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            _activeFiltersLabel.style.marginLeft = 8;
            _activeFiltersLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            Add(_activeFiltersLabel);

            // Apply initial chip styles ("All" active)
            RefreshAllChipStyles();

            UpdateActiveFiltersLabel();
        }

        /// <summary>
        /// Creates a single type-filter chip button.
        /// </summary>
        private Button CreateChip(string chipType)
        {
            var chip = new Button(() => OnChipClicked(chipType))
            {
                text = chipType
            };
            chip.AddToClassList(ChipUssClassName);
            chip.style.paddingLeft = 6;
            chip.style.paddingRight = 6;
            chip.style.paddingTop = 2;
            chip.style.paddingBottom = 2;
            chip.style.marginRight = 2;
            chip.style.marginBottom = 2;
            chip.style.borderTopLeftRadius = 10;
            chip.style.borderTopRightRadius = 10;
            chip.style.borderBottomLeftRadius = 10;
            chip.style.borderBottomRightRadius = 10;
            chip.style.fontSize = 11;
            return chip;
        }

        // ----------------------------------------------------------------
        // Status dropdown builder
        // ----------------------------------------------------------------

        private void BuildStatusDropdown(string allLabel, string[] choices)
        {
            _statusAllLabel = allLabel;
            _statusDropdownContainer.Clear();

            var dropdownChoices = new List<string> { allLabel };
            if (choices != null)
            {
                foreach (var c in choices)
                    dropdownChoices.Add(c);
            }

            _statusDropdown = new PopupField<string>(
                dropdownChoices, 0,
                formatSelectedValueCallback: val => val,
                formatListItemCallback: val => val
            )
            {
                name = "pcp-filter-severity"
            };
            _statusDropdown.AddToClassList(SeverityDropdownUssClassName);
            _statusDropdown.style.minWidth = 120;
            _statusDropdown.RegisterValueChangedCallback(OnStatusChanged);
            _statusDropdownContainer.Add(_statusDropdown);
        }

        // ----------------------------------------------------------------
        // Event handlers
        // ----------------------------------------------------------------

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            if (!_suppressEvents)
                NotifyFilterChanged();
        }

        private void OnStatusChanged(ChangeEvent<string> evt)
        {
            if (!_suppressEvents)
                NotifyFilterChanged();
        }

        private void OnChipClicked(string chipType)
        {
            if (chipType == "All")
            {
                _activeTypes.Clear();
            }
            else
            {
                if (_activeTypes.Contains(chipType))
                    _activeTypes.Remove(chipType);
                else
                    _activeTypes.Add(chipType);
            }

            RefreshAllChipStyles();
            NotifyFilterChanged();
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Resets all filters to their default (unfiltered) state.
        /// </summary>
        public void Reset()
        {
            _suppressEvents = true;

            _searchField.value = string.Empty;
            _activeTypes.Clear();
            RefreshAllChipStyles();
            _statusDropdown.value = _statusAllLabel;

            _suppressEvents = false;
            NotifyFilterChanged();
        }

        /// <summary>
        /// Returns a snapshot of the current filter state.
        /// </summary>
        public PCPFilterState GetFilterState()
        {
            var state = new PCPFilterState
            {
                searchText = _searchField.value ?? string.Empty,
                activeTypes = new HashSet<string>(_activeTypes)
            };

            string selected = _statusDropdown.value;
            state.statusFilter = selected == _statusAllLabel ? null : selected;

            return state;
        }

        /// <summary>
        /// Programmatically sets the active type filters.
        /// </summary>
        public void SetActiveTypes(IEnumerable<string> types)
        {
            _activeTypes.Clear();

            if (types != null)
            {
                foreach (var t in types)
                {
                    if (_chipButtons.ContainsKey(t))
                        _activeTypes.Add(t);
                }
            }

            RefreshAllChipStyles();
            NotifyFilterChanged();
        }

        // ----------------------------------------------------------------
        // Internal
        // ----------------------------------------------------------------

        private void ApplyChipStyle(Button chip, bool active)
        {
            if (active)
            {
                chip.style.backgroundColor = k_ChipActiveBg;
                chip.style.color = k_ChipActiveText;
                chip.style.borderTopColor = k_ChipActiveBorder;
                chip.style.borderBottomColor = k_ChipActiveBorder;
                chip.style.borderLeftColor = k_ChipActiveBorder;
                chip.style.borderRightColor = k_ChipActiveBorder;
                chip.style.borderTopWidth = 1;
                chip.style.borderBottomWidth = 1;
                chip.style.borderLeftWidth = 1;
                chip.style.borderRightWidth = 1;
            }
            else
            {
                chip.style.backgroundColor = k_ChipInactiveBg;
                chip.style.color = k_ChipInactiveText;
                chip.style.borderTopColor = k_ChipInactiveBorder;
                chip.style.borderBottomColor = k_ChipInactiveBorder;
                chip.style.borderLeftColor = k_ChipInactiveBorder;
                chip.style.borderRightColor = k_ChipInactiveBorder;
                chip.style.borderTopWidth = 1;
                chip.style.borderBottomWidth = 1;
                chip.style.borderLeftWidth = 1;
                chip.style.borderRightWidth = 1;
            }
        }

        private void RefreshAllChipStyles()
        {
            foreach (var kvp in _chipButtons)
            {
                bool isActive = kvp.Key == "All"
                    ? _activeTypes.Count == 0
                    : _activeTypes.Contains(kvp.Key);
                ApplyChipStyle(kvp.Value, isActive);
            }
        }

        private void NotifyFilterChanged()
        {
            UpdateActiveFiltersLabel();
            onFilterChanged?.Invoke(GetFilterState());
        }

        private void UpdateActiveFiltersLabel()
        {
            var parts = new List<string>();

            if (_activeTypes.Count > 0)
                parts.Add("Type: " + string.Join(", ", _activeTypes));

            string statusVal = _statusDropdown.value;
            if (statusVal != _statusAllLabel)
                parts.Add("Status: " + statusVal);

            string search = _searchField.value;
            if (!string.IsNullOrEmpty(search))
                parts.Add("Search: \"" + search + "\"");

            if (parts.Count == 0)
            {
                _activeFiltersLabel.text = string.Empty;
                _activeFiltersLabel.style.display = DisplayStyle.None;
            }
            else
            {
                _activeFiltersLabel.text = "Active: " + string.Join("  |  ", parts);
                _activeFiltersLabel.style.display = DisplayStyle.Flex;
            }
        }
    }
}
