using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Semi-transparent overlay that displays scan progress, a status label, and a cancel button.
    /// Positioned absolutely to cover its parent element entirely.
    /// </summary>
#if UNITY_2023_2_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class PCPProgressOverlay : VisualElement
    {
#if !UNITY_2023_2_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PCPProgressOverlay, UxmlTraits> { }
#endif

        // USS class names
        public const string UssClassName = "pcp-progress-overlay";
        public const string ContainerUssClassName = "pcp-progress-overlay__container";
        public const string BarBackgroundUssClassName = "pcp-progress-overlay__bar-bg";
        public const string BarFillUssClassName = "pcp-progress-overlay__bar-fill";
        public const string PercentLabelUssClassName = "pcp-progress-overlay__percent";
        public const string OperationLabelUssClassName = "pcp-progress-overlay__operation";
        public const string CancelButtonUssClassName = "pcp-progress-overlay__cancel";
        public const string VisibleUssClassName = "pcp-progress-overlay--visible";

        // Controls
        private readonly VisualElement _container;
        private readonly VisualElement _barBackground;
        private readonly VisualElement _barFill;
        private readonly Label _percentLabel;
        private readonly Label _operationLabel;
        private readonly Button _cancelButton;

        // Backing fields
        private float _progress;
        private string _label = string.Empty;

        /// <summary>Raised when the user clicks the cancel button.</summary>
        public event Action onCancel;

        /// <summary>
        /// Normalized progress value (0 to 1).
        /// </summary>
        public float Progress
        {
            get => _progress;
            set
            {
                _progress = Mathf.Clamp01(value);
                UpdateProgressBar();
            }
        }

        /// <summary>
        /// Text describing the current operation (displayed below the progress bar).
        /// </summary>
        public string Label
        {
            get => _label;
            set
            {
                _label = value ?? string.Empty;
                _operationLabel.text = _label;
            }
        }

        /// <summary>
        /// Controls overlay visibility. When false, the overlay is hidden via display:none.
        /// </summary>
        public bool IsVisible
        {
            get => resolvedStyle.display == DisplayStyle.Flex;
            set
            {
                if (value)
                    Show();
                else
                    Hide();
            }
        }

        public PCPProgressOverlay()
        {
            AddToClassList(UssClassName);

            // Overlay covers entire parent
            style.position = Position.Absolute;
            style.left = 0;
            style.right = 0;
            style.top = 0;
            style.bottom = 0;
            style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            // High z-order so it sits above all sibling content
            // (UIToolkit doesn't have z-index but position:absolute + being last child works)

            // Start hidden
            style.display = DisplayStyle.None;

            // ----- Central container -----
            _container = new VisualElement
            {
                name = "pcp-progress-container"
            };
            _container.AddToClassList(ContainerUssClassName);
            _container.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            _container.style.borderTopLeftRadius = 6;
            _container.style.borderTopRightRadius = 6;
            _container.style.borderBottomLeftRadius = 6;
            _container.style.borderBottomRightRadius = 6;
            _container.style.paddingLeft = 24;
            _container.style.paddingRight = 24;
            _container.style.paddingTop = 20;
            _container.style.paddingBottom = 20;
            _container.style.minWidth = 360;
            _container.style.maxWidth = 480;
            _container.style.alignItems = Align.Center;
            Add(_container);

            // ----- Percent label -----
            _percentLabel = new Label("0%")
            {
                name = "pcp-progress-percent"
            };
            _percentLabel.AddToClassList(PercentLabelUssClassName);
            _percentLabel.style.fontSize = 24;
            _percentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _percentLabel.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
            _percentLabel.style.marginBottom = 12;
            _percentLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _container.Add(_percentLabel);

            // ----- Progress bar background -----
            _barBackground = new VisualElement
            {
                name = "pcp-progress-bar-bg"
            };
            _barBackground.AddToClassList(BarBackgroundUssClassName);
            _barBackground.style.width = Length.Percent(100);
            _barBackground.style.height = 8;
            _barBackground.style.backgroundColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            _barBackground.style.borderTopLeftRadius = 4;
            _barBackground.style.borderTopRightRadius = 4;
            _barBackground.style.borderBottomLeftRadius = 4;
            _barBackground.style.borderBottomRightRadius = 4;
            _barBackground.style.overflow = Overflow.Hidden;
            _barBackground.style.marginBottom = 12;
            _container.Add(_barBackground);

            // ----- Progress bar fill -----
            _barFill = new VisualElement
            {
                name = "pcp-progress-bar-fill"
            };
            _barFill.AddToClassList(BarFillUssClassName);
            _barFill.style.width = Length.Percent(0);
            _barFill.style.height = Length.Percent(100);
            _barFill.style.backgroundColor = new Color(0.337f, 0.612f, 0.839f, 1f);
            _barFill.style.borderTopLeftRadius = 4;
            _barFill.style.borderTopRightRadius = 4;
            _barFill.style.borderBottomLeftRadius = 4;
            _barFill.style.borderBottomRightRadius = 4;
            _barBackground.Add(_barFill);

            // ----- Operation label -----
            _operationLabel = new Label(string.Empty)
            {
                name = "pcp-progress-operation"
            };
            _operationLabel.AddToClassList(OperationLabelUssClassName);
            _operationLabel.style.fontSize = 12;
            _operationLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            _operationLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _operationLabel.style.marginBottom = 16;
            _operationLabel.style.whiteSpace = WhiteSpace.Normal;
            _operationLabel.style.maxWidth = Length.Percent(100);
            _container.Add(_operationLabel);

            // ----- Cancel button -----
            _cancelButton = new Button(OnCancelClicked)
            {
                text = "Cancel",
                name = "pcp-progress-cancel"
            };
            _cancelButton.AddToClassList(CancelButtonUssClassName);
            _cancelButton.style.paddingLeft = 20;
            _cancelButton.style.paddingRight = 20;
            _cancelButton.style.paddingTop = 6;
            _cancelButton.style.paddingBottom = 6;
            _cancelButton.style.borderTopLeftRadius = 3;
            _cancelButton.style.borderTopRightRadius = 3;
            _cancelButton.style.borderBottomLeftRadius = 3;
            _cancelButton.style.borderBottomRightRadius = 3;
            _container.Add(_cancelButton);
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Shows the overlay with optional initial progress and label.
        /// </summary>
        public void Show(float initialProgress = 0f, string operationLabel = "")
        {
            _progress = Mathf.Clamp01(initialProgress);
            _label = operationLabel ?? string.Empty;
            UpdateProgressBar();
            _operationLabel.text = _label;

            style.display = DisplayStyle.Flex;
            AddToClassList(VisibleUssClassName);
            // Bring to front by re-parenting as last child
            BringToFront();
        }

        /// <summary>
        /// Hides the overlay and resets progress.
        /// </summary>
        public void Hide()
        {
            style.display = DisplayStyle.None;
            RemoveFromClassList(VisibleUssClassName);
            _progress = 0f;
            UpdateProgressBar();
            _operationLabel.text = string.Empty;
        }

        // ----------------------------------------------------------------
        // Internal
        // ----------------------------------------------------------------

        private void UpdateProgressBar()
        {
            float pct = _progress * 100f;
            _barFill.style.width = Length.Percent(pct);
            _percentLabel.text = $"{Mathf.RoundToInt(pct)}%";
        }

        private void OnCancelClicked()
        {
            onCancel?.Invoke();
        }

    }
}
