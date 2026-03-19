using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Module header control displaying a colored accent bar, icon, module name,
    /// finding count, total size, and a scan button.
    /// </summary>
#if UNITY_2023_2_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class PCPModuleHeader : VisualElement
    {
#if !UNITY_2023_2_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PCPModuleHeader, UxmlTraits> { }
#endif

        // USS class names
        public const string UssClassName = "pcp-module-header";
        public const string AccentBarUssClassName = "pcp-module-header__accent";
        public const string ContentUssClassName = "pcp-module-header__content";
        public const string IconLabelUssClassName = "pcp-module-header__icon";
        public const string NameLabelUssClassName = "pcp-module-header__name";
        public const string StatsContainerUssClassName = "pcp-module-header__stats";
        public const string FindingCountUssClassName = "pcp-module-header__findings";
        public const string TotalSizeUssClassName = "pcp-module-header__size";
        public const string ScanButtonUssClassName = "pcp-module-header__scan-btn";
        public const string ScanningUssClassName = "pcp-module-header--scanning";

        // Controls
        private readonly VisualElement _accentBar;
        private readonly Label _iconLabel;
        private readonly Label _nameLabel;
        private readonly Label _findingCountLabel;
        private readonly Label _totalSizeLabel;
        private readonly Button _scanButton;

        // Backing fields
        private string _moduleName = string.Empty;
        private string _icon = string.Empty;
        private Color _accentColor = Color.gray;
        private int _findingCount;
        private long _totalSize;
        private bool _isScanning;

        /// <summary>Raised when the user clicks the module's scan button.</summary>
        public event Action onScan;

        // ----------------------------------------------------------------
        // Properties
        // ----------------------------------------------------------------

        public string ModuleName
        {
            get => _moduleName;
            set
            {
                _moduleName = value ?? string.Empty;
                _nameLabel.text = _moduleName;
            }
        }

        public string Icon
        {
            get => _icon;
            set
            {
                _icon = value ?? string.Empty;
                _iconLabel.text = _icon;
            }
        }

        public Color AccentColor
        {
            get => _accentColor;
            set
            {
                _accentColor = value;
                _accentBar.style.backgroundColor = _accentColor;
            }
        }

        public int FindingCount
        {
            get => _findingCount;
            set
            {
                _findingCount = value;
                _findingCountLabel.text = _findingCount == 0
                    ? "No findings"
                    : $"{_findingCount} finding{(_findingCount != 1 ? "s" : "")}";
            }
        }

        public long TotalSize
        {
            get => _totalSize;
            set
            {
                _totalSize = value;
                _totalSizeLabel.text = PCPAssetInfo.FormatBytes(_totalSize);
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (_isScanning == value)
                    return;
                _isScanning = value;
                UpdateScanState();
            }
        }

        // ----------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------

        public PCPModuleHeader()
        {
            AddToClassList(UssClassName);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.paddingTop = 8;
            style.paddingBottom = 8;
            style.paddingLeft = 0;
            style.paddingRight = 8;
            style.borderBottomWidth = 1;
            style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);

            // ----- Accent bar (left border) -----
            _accentBar = new VisualElement { name = "pcp-module-accent" };
            _accentBar.AddToClassList(AccentBarUssClassName);
            _accentBar.style.width = 4;
            _accentBar.style.alignSelf = Align.Stretch;
            _accentBar.style.backgroundColor = _accentColor;
            _accentBar.style.borderTopLeftRadius = 2;
            _accentBar.style.borderBottomLeftRadius = 2;
            _accentBar.style.marginRight = 8;
            Add(_accentBar);

            // ----- Content area -----
            var content = new VisualElement { name = "pcp-module-content" };
            content.AddToClassList(ContentUssClassName);
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            content.style.flexGrow = 1;
            Add(content);

            // Icon
            _iconLabel = new Label(_icon) { name = "pcp-module-icon" };
            _iconLabel.AddToClassList(IconLabelUssClassName);
            _iconLabel.style.fontSize = 18;
            _iconLabel.style.marginRight = 6;
            _iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _iconLabel.style.minWidth = 24;
            content.Add(_iconLabel);

            // Module name
            _nameLabel = new Label(_moduleName) { name = "pcp-module-name" };
            _nameLabel.AddToClassList(NameLabelUssClassName);
            _nameLabel.style.fontSize = 14;
            _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nameLabel.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
            _nameLabel.style.marginRight = 12;
            content.Add(_nameLabel);

            // Stats container
            var stats = new VisualElement { name = "pcp-module-stats" };
            stats.AddToClassList(StatsContainerUssClassName);
            stats.style.flexDirection = FlexDirection.Row;
            stats.style.alignItems = Align.Center;
            stats.style.flexGrow = 1;
            content.Add(stats);

            _findingCountLabel = new Label("No findings") { name = "pcp-module-findings" };
            _findingCountLabel.AddToClassList(FindingCountUssClassName);
            _findingCountLabel.style.fontSize = 12;
            _findingCountLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            _findingCountLabel.style.marginRight = 12;
            stats.Add(_findingCountLabel);

            _totalSizeLabel = new Label(PCPAssetInfo.FormatBytes(0)) { name = "pcp-module-size" };
            _totalSizeLabel.AddToClassList(TotalSizeUssClassName);
            _totalSizeLabel.style.fontSize = 12;
            _totalSizeLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            stats.Add(_totalSizeLabel);

            // ----- Scan button -----
            _scanButton = new Button(OnScanClicked)
            {
                text = "Scan",
                name = "pcp-module-scan-btn"
            };
            _scanButton.AddToClassList(ScanButtonUssClassName);
            _scanButton.style.paddingLeft = 12;
            _scanButton.style.paddingRight = 12;
            _scanButton.style.paddingTop = 4;
            _scanButton.style.paddingBottom = 4;
            _scanButton.style.borderTopLeftRadius = 3;
            _scanButton.style.borderTopRightRadius = 3;
            _scanButton.style.borderBottomLeftRadius = 3;
            _scanButton.style.borderBottomRightRadius = 3;
            _scanButton.style.marginLeft = 8;
            Add(_scanButton);
        }

        /// <summary>
        /// Convenience constructor that sets all properties in one call.
        /// </summary>
        public PCPModuleHeader(string moduleName, string icon, Color accentColor) : this()
        {
            ModuleName = moduleName;
            Icon = icon;
            AccentColor = accentColor;
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Populates the header from an <see cref="IPCPModule"/> instance.
        /// </summary>
        public void SetFromModule(IPCPModule module)
        {
            if (module == null) return;
            ModuleName = module.DisplayName;
            Icon = module.Icon;
            AccentColor = module.AccentColor;
            FindingCount = module.FindingCount;
            TotalSize = module.TotalSizeBytes;
            IsScanning = module.IsScanning;
        }

        // ----------------------------------------------------------------
        // Internal
        // ----------------------------------------------------------------

        private void OnScanClicked()
        {
            onScan?.Invoke();
        }

        private void UpdateScanState()
        {
            if (_isScanning)
            {
                _scanButton.text = "Cancel";
                _scanButton.style.backgroundColor = new Color(0.753f, 0.224f, 0.169f, 1f);
                _scanButton.style.color = Color.white;
                AddToClassList(ScanningUssClassName);
            }
            else
            {
                _scanButton.text = "Scan";
                _scanButton.style.backgroundColor = StyleKeyword.Null;
                _scanButton.style.color = StyleKeyword.Null;
                RemoveFromClassList(ScanningUssClassName);
            }
        }
    }
}
