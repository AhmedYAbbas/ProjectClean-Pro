using System;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Top-level toolbar control containing scan actions, export, and a search field.
    /// Button states toggle automatically when <see cref="IsScanning"/> changes.
    /// </summary>
#if UNITY_2023_2_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class PCPToolbar : VisualElement
    {
#if !UNITY_2023_2_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PCPToolbar, UxmlTraits> { }
#endif

        // USS class names
        public const string UssClassName = "pcp-toolbar";
        public const string ButtonUssClassName = "pcp-toolbar__button";
        public const string PrimaryButtonUssClassName = "pcp-toolbar__button--primary";
        public const string SeparatorUssClassName = "pcp-toolbar__separator";
        public const string SearchFieldUssClassName = "pcp-toolbar__search";

        // Controls
        private readonly Button _scanAllButton;
        private readonly Button _scanModuleButton;
        private readonly Button _exportButton;
        private readonly TextField _searchField;

        private bool _isScanning;

        /// <summary>Raised when the user clicks "Scan All" (or "Cancel" during a scan).</summary>
        public event Action onScanAll;

        /// <summary>Raised when the user clicks "Scan Module".</summary>
        public event Action onScanModule;

        /// <summary>Raised when the user clicks "Export Report".</summary>
        public event Action onExport;

        /// <summary>Raised when the search text changes.</summary>
        public event Action<string> onSearchChanged;

        /// <summary>
        /// Gets or sets whether a scan is in progress. Setting this property
        /// updates button text and enabled states accordingly.
        /// </summary>
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (_isScanning == value)
                    return;
                _isScanning = value;
                UpdateButtonStates();
            }
        }

        /// <summary>
        /// Gets or sets the search field text.
        /// </summary>
        public string SearchText
        {
            get => _searchField.value;
            set => _searchField.value = value ?? string.Empty;
        }

        public PCPToolbar()
        {
            AddToClassList(UssClassName);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.paddingLeft = 4;
            style.paddingRight = 4;
            style.paddingTop = 4;
            style.paddingBottom = 4;
            style.borderBottomWidth = 1;
            style.borderBottomColor = new UnityEngine.Color(0.235f, 0.235f, 0.235f, 1f);

            // ----- Scan All button (primary) -----
            _scanAllButton = new Button(OnScanAllClicked)
            {
                text = "Scan All",
                name = "pcp-toolbar-scan-all"
            };
            _scanAllButton.AddToClassList(ButtonUssClassName);
            _scanAllButton.AddToClassList(PrimaryButtonUssClassName);
            ApplyPrimaryButtonStyle(_scanAllButton);
            Add(_scanAllButton);

            // ----- Scan Module button -----
            _scanModuleButton = new Button(OnScanModuleClicked)
            {
                text = "Scan Module",
                name = "pcp-toolbar-scan-module"
            };
            _scanModuleButton.AddToClassList(ButtonUssClassName);
            ApplyButtonStyle(_scanModuleButton);
            Add(_scanModuleButton);

            // ----- Separator -----
            var separator = new VisualElement
            {
                name = "pcp-toolbar-separator"
            };
            separator.AddToClassList(SeparatorUssClassName);
            separator.style.width = 1;
            separator.style.height = 20;
            separator.style.backgroundColor = new UnityEngine.Color(0.235f, 0.235f, 0.235f, 1f);
            separator.style.marginLeft = 6;
            separator.style.marginRight = 6;
            Add(separator);

            // ----- Export Report button -----
            _exportButton = new Button(OnExportClicked)
            {
                text = "Export Report",
                name = "pcp-toolbar-export"
            };
            _exportButton.AddToClassList(ButtonUssClassName);
            ApplyButtonStyle(_exportButton);
            Add(_exportButton);

            // ----- Spacer -----
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            Add(spacer);

            // ----- Search field -----
            _searchField = new TextField
            {
                name = "pcp-toolbar-search",
                value = string.Empty
            };
            _searchField.AddToClassList(SearchFieldUssClassName);
            _searchField.style.minWidth = 180;
            _searchField.style.maxWidth = 300;
#if UNITY_2022_1_OR_NEWER
            _searchField.textEdition.placeholder = "Search results...";
#else
            _searchField.tooltip = "Search results...";
#endif
            _searchField.RegisterValueChangedCallback(evt => onSearchChanged?.Invoke(evt.newValue));
            Add(_searchField);
        }

        // ----------------------------------------------------------------
        // Button style helpers
        // ----------------------------------------------------------------

        private static void ApplyButtonStyle(Button button)
        {
            button.style.marginLeft = 2;
            button.style.marginRight = 2;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
            button.style.borderTopLeftRadius = 3;
            button.style.borderTopRightRadius = 3;
            button.style.borderBottomLeftRadius = 3;
            button.style.borderBottomRightRadius = 3;
        }

        private static void ApplyPrimaryButtonStyle(Button button)
        {
            ApplyButtonStyle(button);
            button.style.backgroundColor = new UnityEngine.Color(0.204f, 0.396f, 0.647f, 1f);
            button.style.color = UnityEngine.Color.white;
            button.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        }

        // ----------------------------------------------------------------
        // Click handlers
        // ----------------------------------------------------------------

        private void OnScanAllClicked()
        {
            onScanAll?.Invoke();
        }

        private void OnScanModuleClicked()
        {
            onScanModule?.Invoke();
        }

        private void OnExportClicked()
        {
            onExport?.Invoke();
        }

        // ----------------------------------------------------------------
        // State management
        // ----------------------------------------------------------------

        private void UpdateButtonStates()
        {
            if (_isScanning)
            {
                _scanAllButton.text = "Cancel";
                _scanAllButton.style.backgroundColor =
                    new UnityEngine.Color(0.753f, 0.224f, 0.169f, 1f);
                _scanModuleButton.SetEnabled(false);
                _exportButton.SetEnabled(false);
            }
            else
            {
                _scanAllButton.text = "Scan All";
                _scanAllButton.style.backgroundColor =
                    new UnityEngine.Color(0.204f, 0.396f, 0.647f, 1f);
                _scanModuleButton.SetEnabled(true);
                _exportButton.SetEnabled(true);
            }
        }
    }
}
