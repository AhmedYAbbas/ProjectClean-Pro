using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Data carrier for a single row in <see cref="PCPResultListView"/>.
    /// Populated by the converter function supplied via <see cref="PCPResultListView.SetData"/>.
    /// </summary>
    public struct PCPRowData
    {
        public bool selected;
        public Texture2D icon;
        public string name;
        public string path;
        public string type;
        public long sizeBytes;
        public string status;
        public Color statusColor;
        public string guid;
    }

    /// <summary>
    /// Virtualized, sortable, multi-column list view for displaying scan results.
    /// Uses Unity's <see cref="ListView"/> internally for efficient rendering of
    /// large data sets. Supports search filtering, multi-select, and column sorting.
    /// </summary>
#if UNITY_2023_2_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class PCPResultListView : VisualElement
    {
#if !UNITY_2023_2_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PCPResultListView, UxmlTraits> { }
#endif

        // USS class names
        public const string UssClassName = "pcp-result-list";
        public const string HeaderUssClassName = "pcp-result-list__header";
        public const string HeaderCellUssClassName = "pcp-result-list__header-cell";
        public const string HeaderCellActiveUssClassName = "pcp-result-list__header-cell--active";
        public const string RowUssClassName = "pcp-result-list__row";
        public const string CellUssClassName = "pcp-result-list__cell";
        public const string CheckboxCellUssClassName = "pcp-result-list__cell--checkbox";
        public const string IconCellUssClassName = "pcp-result-list__cell--icon";
        public const string NameCellUssClassName = "pcp-result-list__cell--name";
        public const string PathCellUssClassName = "pcp-result-list__cell--path";
        public const string TypeCellUssClassName = "pcp-result-list__cell--type";
        public const string SizeCellUssClassName = "pcp-result-list__cell--size";
        public const string StatusCellUssClassName = "pcp-result-list__cell--status";

        // Column indices
        private const int ColCheckbox = 0;
        private const int ColIcon     = 1;
        private const int ColName     = 2;
        private const int ColPath     = 3;
        private const int ColType     = 4;
        private const int ColSize     = 5;
        private const int ColStatus   = 6;

        // Default column definitions (widths are mutable per instance)
        private readonly ColumnDef[] _columns = new[]
        {
            new ColumnDef("",       30,  false), // checkbox
            new ColumnDef("",       28,  false), // icon
            new ColumnDef("Name",   180, true),
            new ColumnDef("Path",   260, true),
            new ColumnDef("Type",   90,  true),
            new ColumnDef("Size",   80,  true),
            new ColumnDef("Status", 90,  true)
        };

        private struct ColumnDef
        {
            public string title;
            public float width;
            public bool sortable;

            public ColumnDef(string title, float width, bool sortable)
            {
                this.title = title;
                this.width = width;
                this.sortable = sortable;
            }
        }

        // Minimum column width when dragging
        private const float MinColumnWidth = 30f;

        // Internal controls
        private readonly VisualElement _headerRow;
        private readonly ListView _listView;
        private readonly Label _emptyLabel;

        // Resize drag state
        private int _resizingColIndex = -1;
        private float _resizeDragStartX;
        private float _resizeStartWidth;

        // Data
        private IList _rawItemsSource;
        private Func<object, PCPRowData> _converter;
        private List<int> _filteredIndices = new List<int>();
        private List<PCPRowData> _filteredRows = new List<PCPRowData>();
        private readonly HashSet<int> _selectedRawIndices = new HashSet<int>();

        // Sorting
        private string _sortColumn = string.Empty;
        private bool _sortAscending = true;

        // Filtering
        private string _searchFilter = string.Empty;
        private HashSet<string> _typeFilter;
        private string _statusFilter;

        private static readonly string[] k_KnownTypeChips =
            { "Texture", "Material", "Mesh", "Audio", "Script", "Prefab", "Scene" };

        // Events
        /// <summary>Raised when the selection changes. Provides the list of selected raw indices.</summary>
        public event Action<IReadOnlyList<int>> onSelectionChanged;


        // ----------------------------------------------------------------
        // Properties
        // ----------------------------------------------------------------

        /// <summary>
        /// Raw items source. Set via <see cref="SetData"/> together with a converter.
        /// </summary>
        public IList ItemsSource => _rawItemsSource;

        /// <summary>
        /// Text filter applied to name and path columns. Setting this re-filters the list.
        /// </summary>
        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                _searchFilter = value ?? string.Empty;
                RebuildFilteredList();
            }
        }

        /// <summary>
        /// Applies all three filters together and rebuilds the list once.
        /// </summary>
        public void ApplyFilters(string searchText, HashSet<string> typeFilter, string statusFilter)
        {
            _searchFilter = searchText ?? string.Empty;
            _typeFilter = typeFilter;
            _statusFilter = statusFilter;
            RebuildFilteredList();
        }

        /// <summary>Current sort column name.</summary>
        public string SortColumn => _sortColumn;

        /// <summary>Current sort direction.</summary>
        public bool SortAscending => _sortAscending;

        /// <summary>
        /// Indices (into the raw items source) of currently selected rows.
        /// </summary>
        public IReadOnlyCollection<int> SelectedIndices => _selectedRawIndices;

        // ----------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------

        public PCPResultListView()
        {
            AddToClassList(UssClassName);
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // ----- Column header row -----
            _headerRow = new VisualElement { name = "pcp-result-header" };
            _headerRow.AddToClassList(HeaderUssClassName);
            _headerRow.style.flexDirection = FlexDirection.Row;
            _headerRow.style.alignItems = Align.Center;
            _headerRow.style.minHeight = 24;
            _headerRow.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            _headerRow.style.borderBottomWidth = 1;
            _headerRow.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            _headerRow.style.paddingLeft = 4;
            _headerRow.style.paddingRight = 4;
            _headerRow.style.flexShrink = 0;

            for (int i = 0; i < _columns.Length; i++)
            {
                var col = _columns[i];
                var headerCell = CreateHeaderCell(col, i);
                _headerRow.Add(headerCell);
            }

            Add(_headerRow);

            // ----- Select-all checkbox in the header -----
            var selectAllToggle = new Toggle { name = "pcp-result-select-all" };
            selectAllToggle.style.marginLeft = 0;
            selectAllToggle.style.marginRight = 0;
            selectAllToggle.RegisterValueChangedCallback(OnSelectAllChanged);
            // Add toggle into the first header wrapper's content area
            if (_headerRow.childCount > 0)
            {
                var firstWrapper = _headerRow[0];
                var firstContent = firstWrapper.childCount > 0 ? firstWrapper[0] : firstWrapper;
                firstContent.Add(selectAllToggle);
            }

            // ----- ListView (virtualized) -----
            _listView = new ListView
            {
                name = "pcp-result-listview",
#if UNITY_2021_2_OR_NEWER
                fixedItemHeight = 26,
#else
                itemHeight = 26,
#endif
                selectionType = SelectionType.Multiple,
#if UNITY_2021_2_OR_NEWER
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
#endif
            };
            _listView.style.flexGrow = 1;
            _listView.makeItem = MakeItem;
            _listView.bindItem = BindItem;
#if UNITY_2022_2_OR_NEWER
            _listView.selectionChanged += OnListViewSelectionChanged;
#else
            _listView.onSelectionChange += OnListViewSelectionChanged;
#endif
            Add(_listView);

            // ----- Empty state label -----
            _emptyLabel = new Label("No results to display.")
            {
                name = "pcp-result-empty"
            };
            _emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _emptyLabel.style.fontSize = 13;
            _emptyLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            _emptyLabel.style.position = Position.Absolute;
            _emptyLabel.style.left = 0;
            _emptyLabel.style.right = 0;
            _emptyLabel.style.top = 40;
            _emptyLabel.style.bottom = 0;
            _emptyLabel.style.display = DisplayStyle.None;
            Add(_emptyLabel);

        }

        // ----------------------------------------------------------------
        // Header
        // ----------------------------------------------------------------

        private VisualElement CreateHeaderCell(ColumnDef col, int colIndex)
        {
            // Wrapper holds the cell content + optional resize handle
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Row;
            wrapper.style.alignItems = Align.Stretch;
            wrapper.style.width = col.width;
            wrapper.style.minWidth = col.width;
            wrapper.style.overflow = Overflow.Hidden;

            // Content area (label + sort indicator)
            var content = new VisualElement();
            content.AddToClassList(HeaderCellUssClassName);
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            content.style.flexGrow = 1;
            content.style.paddingLeft = 4;
            content.style.paddingRight = 4;
            content.style.overflow = Overflow.Hidden;

            if (!string.IsNullOrEmpty(col.title))
            {
                var label = new Label(col.title);
                label.style.fontSize = 11;
                label.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                content.Add(label);

                // Sort indicator
                var sortIndicator = new Label("")
                {
                    name = $"sort-indicator-{colIndex}"
                };
                sortIndicator.style.fontSize = 10;
                sortIndicator.style.marginLeft = 2;
                sortIndicator.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                content.Add(sortIndicator);

                if (col.sortable)
                {
                    content.style.cursor = new UnityEngine.UIElements.Cursor();
                    int capturedIndex = colIndex;
                    content.RegisterCallback<ClickEvent>(evt => OnHeaderClicked(capturedIndex));
                }
            }

            wrapper.Add(content);

            // Resize handle on the right edge (only for named/data columns, not checkbox/icon)
            if (!string.IsNullOrEmpty(col.title))
            {
                var handle = CreateResizeHandle(colIndex);
                wrapper.Add(handle);
            }

            // For the "Name" column and "Path" column, allow them to grow
            if (col.title == "Name" || col.title == "Path")
            {
                wrapper.style.flexGrow = 1;
                wrapper.style.flexShrink = 1;
            }

            return wrapper;
        }

        private VisualElement CreateResizeHandle(int colIndex)
        {
            var handle = new VisualElement();
            handle.name = $"resize-handle-{colIndex}";
            handle.style.width = 6;
            handle.style.minWidth = 6;
            handle.style.flexShrink = 0;
            handle.style.cursor = new UnityEngine.UIElements.Cursor();
            // Visible divider line
            handle.style.borderRightWidth = 1;
            handle.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            int captured = colIndex;

            handle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _resizingColIndex = captured;
                _resizeDragStartX = evt.mousePosition.x;
                _resizeStartWidth = _columns[captured].width;
                handle.CaptureMouse();
                evt.StopPropagation();
            });

            handle.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (_resizingColIndex != captured || !handle.HasMouseCapture()) return;
                float delta = evt.mousePosition.x - _resizeDragStartX;
                float newWidth = Mathf.Max(MinColumnWidth, _resizeStartWidth + delta);
                _columns[captured].width = newWidth;
                ApplyHeaderColumnWidth(captured);
                RefreshListView();
                evt.StopPropagation();
            });

            handle.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (_resizingColIndex != captured) return;
                _resizingColIndex = -1;
                if (handle.HasMouseCapture())
                    handle.ReleaseMouse();
                handle.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
                handle.style.borderRightWidth = 1;
                evt.StopPropagation();
            });

            // Highlight handle on hover
            handle.RegisterCallback<MouseEnterEvent>(evt =>
            {
                handle.style.borderRightColor = new Color(0.337f, 0.612f, 0.839f, 1f);
                handle.style.borderRightWidth = 2;
            });

            handle.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                if (_resizingColIndex != captured)
                {
                    handle.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
                    handle.style.borderRightWidth = 1;
                }
            });

            return handle;
        }

        private void OnHeaderClicked(int colIndex)
        {
            var col = _columns[colIndex];
            if (!col.sortable) return;

            if (_sortColumn == col.title)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = col.title;
                _sortAscending = true;
            }

            UpdateSortIndicators();
            RebuildFilteredList();
        }

        private void UpdateSortIndicators()
        {
            for (int i = 0; i < _columns.Length; i++)
            {
                var indicator = _headerRow.Q<Label>($"sort-indicator-{i}");
                if (indicator == null) continue;

                if (_columns[i].title == _sortColumn)
                {
                    indicator.text = _sortAscending ? "\u25B2" : "\u25BC";
                    indicator.parent?.AddToClassList(HeaderCellActiveUssClassName);
                }
                else
                {
                    indicator.text = "";
                    indicator.parent?.RemoveFromClassList(HeaderCellActiveUssClassName);
                }
            }
        }

        // ----------------------------------------------------------------
        // Row creation / binding
        // ----------------------------------------------------------------

        private VisualElement MakeItem()
        {
            var row = new VisualElement { name = "pcp-result-row" };
            row.AddToClassList(RowUssClassName);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.height = 26;

            // 0: Checkbox
            var toggle = new Toggle { name = "row-checkbox" };
            toggle.AddToClassList(CheckboxCellUssClassName);
            toggle.style.width = _columns[0].width;
            toggle.style.minWidth = _columns[0].width;
            toggle.style.marginLeft = 0;
            toggle.style.marginRight = 0;
            row.Add(toggle);

            // 1: Icon
            var icon = new Image { name = "row-icon" };
            icon.AddToClassList(IconCellUssClassName);
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginLeft = 4;
            icon.style.marginRight = 4;
            icon.scaleMode = ScaleMode.ScaleToFit;
            var iconCell = new VisualElement();
            iconCell.style.width = _columns[1].width;
            iconCell.style.minWidth = _columns[1].width;
            iconCell.style.alignItems = Align.Center;
            iconCell.style.justifyContent = Justify.Center;
            iconCell.Add(icon);
            row.Add(iconCell);

            // 2: Name
            var nameLabel = new Label { name = "row-name" };
            nameLabel.AddToClassList(NameCellUssClassName);
            nameLabel.AddToClassList(CellUssClassName);
            nameLabel.style.width = _columns[2].width;
            nameLabel.style.minWidth = _columns[2].width;
            nameLabel.style.flexGrow = 1;
            nameLabel.style.flexShrink = 1;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.paddingLeft = 4;
            nameLabel.style.fontSize = 12;
            row.Add(nameLabel);

            // 3: Path
            var pathLabel = new Label { name = "row-path" };
            pathLabel.AddToClassList(PathCellUssClassName);
            pathLabel.AddToClassList(CellUssClassName);
            pathLabel.style.width = _columns[3].width;
            pathLabel.style.minWidth = _columns[3].width;
            pathLabel.style.flexGrow = 1;
            pathLabel.style.flexShrink = 1;
            pathLabel.style.overflow = Overflow.Hidden;
            pathLabel.style.textOverflow = TextOverflow.Ellipsis;
            pathLabel.style.paddingLeft = 4;
            pathLabel.style.fontSize = 11;
            pathLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            row.Add(pathLabel);

            // 4: Type
            var typeLabel = new Label { name = "row-type" };
            typeLabel.AddToClassList(TypeCellUssClassName);
            typeLabel.AddToClassList(CellUssClassName);
            typeLabel.style.width = _columns[4].width;
            typeLabel.style.minWidth = _columns[4].width;
            typeLabel.style.overflow = Overflow.Hidden;
            typeLabel.style.textOverflow = TextOverflow.Ellipsis;
            typeLabel.style.paddingLeft = 4;
            typeLabel.style.fontSize = 11;
            row.Add(typeLabel);

            // 5: Size
            var sizeLabel = new Label { name = "row-size" };
            sizeLabel.AddToClassList(SizeCellUssClassName);
            sizeLabel.AddToClassList(CellUssClassName);
            sizeLabel.style.width = _columns[5].width;
            sizeLabel.style.minWidth = _columns[5].width;
            sizeLabel.style.overflow = Overflow.Hidden;
            sizeLabel.style.textOverflow = TextOverflow.Ellipsis;
            sizeLabel.style.paddingLeft = 4;
            sizeLabel.style.fontSize = 11;
            sizeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            sizeLabel.style.paddingRight = 8;
            row.Add(sizeLabel);

            // 6: Status badge
            var statusBadge = new PCPBadge { name = "row-status" };
            statusBadge.AddToClassList(StatusCellUssClassName);
            statusBadge.style.width = _columns[6].width;
            statusBadge.style.minWidth = _columns[6].width;
            row.Add(statusBadge);

            return row;
        }

        private void BindItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _filteredRows.Count)
                return;

            var row = _filteredRows[index];
            int rawIndex = _filteredIndices[index];

            // Apply current column widths (supports runtime resizing)
            ApplyRowColumnWidths(element);

            // Checkbox
            var toggle = element.Q<Toggle>("row-checkbox");
            if (toggle != null)
            {
                // Unregister previous handlers to avoid stacking
                toggle.UnregisterValueChangedCallback(OnRowToggleChanged);
                toggle.userData = rawIndex;
                toggle.SetValueWithoutNotify(_selectedRawIndices.Contains(rawIndex));
                toggle.RegisterValueChangedCallback(OnRowToggleChanged);
            }

            // Icon
            var icon = element.Q<Image>("row-icon");
            if (icon != null)
                icon.image = row.icon;

            // Name
            var nameLabel = element.Q<Label>("row-name");
            if (nameLabel != null)
            {
                nameLabel.text = row.name ?? string.Empty;
                nameLabel.tooltip = row.name ?? string.Empty;
            }

            // Path
            var pathLabel = element.Q<Label>("row-path");
            if (pathLabel != null)
            {
                pathLabel.text = row.path ?? string.Empty;
                pathLabel.tooltip = row.path ?? string.Empty;
            }

            // Type
            var typeLabel = element.Q<Label>("row-type");
            if (typeLabel != null)
                typeLabel.text = row.type ?? string.Empty;

            // Size
            var sizeLabel = element.Q<Label>("row-size");
            if (sizeLabel != null)
                sizeLabel.text = PCPAssetInfo.FormatBytes(row.sizeBytes);

            // Status badge
            var statusBadge = element.Q<PCPBadge>("row-status");
            if (statusBadge != null)
                statusBadge.SetBadge(row.status ?? string.Empty, row.statusColor);

            // Double-click handler: ping asset in project
            element.UnregisterCallback<MouseDownEvent>(OnRowDoubleClick);
            element.userData = row;
            element.RegisterCallback<MouseDownEvent>(OnRowDoubleClick);
        }

        // ----------------------------------------------------------------
        // Event handlers
        // ----------------------------------------------------------------

        private void OnRowToggleChanged(ChangeEvent<bool> evt)
        {
            if (evt.target is Toggle toggle && toggle.userData is int rawIndex)
            {
                if (evt.newValue)
                    _selectedRawIndices.Add(rawIndex);
                else
                    _selectedRawIndices.Remove(rawIndex);

                onSelectionChanged?.Invoke(_selectedRawIndices.ToList());
            }
        }

        private void OnRowDoubleClick(MouseDownEvent evt)
        {
            if (evt.clickCount == 2 && evt.target is VisualElement ve)
            {
                // Walk up to find the row element with userData
                var current = ve;
                while (current != null && !(current.userData is PCPRowData))
                    current = current.parent;

                if (current?.userData is PCPRowData rowData && !string.IsNullOrEmpty(rowData.guid))
                {
                    string path = AssetDatabase.GUIDToAssetPath(rowData.guid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (obj != null)
                            EditorGUIUtility.PingObject(obj);
                    }
                }
                else if (current?.userData is PCPRowData rd2 && !string.IsNullOrEmpty(rd2.path))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rd2.path);
                    if (obj != null)
                        EditorGUIUtility.PingObject(obj);
                }
            }
        }

        private void OnSelectAllChanged(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
            {
                // Select all filtered items
                foreach (int rawIndex in _filteredIndices)
                    _selectedRawIndices.Add(rawIndex);
            }
            else
            {
                // Deselect all
                _selectedRawIndices.Clear();
            }

            RefreshListView();
            onSelectionChanged?.Invoke(_selectedRawIndices.ToList());
        }

        private void OnListViewSelectionChanged(IEnumerable<object> selection)
        {
            // Sync ListView selection with our checkbox-based selection
            // This handles keyboard-based selection
        }


        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Sets the data source and a converter function that transforms each item
        /// into a <see cref="PCPRowData"/> for display.
        /// </summary>
        /// <param name="items">The raw data items (any IList).</param>
        /// <param name="converter">Function to convert each item to row data.</param>
        public void SetData(IList items, Func<object, PCPRowData> converter)
        {
            _rawItemsSource = items;
            _converter = converter;
            _selectedRawIndices.Clear();
            RebuildFilteredList();
        }

        /// <summary>
        /// Re-filters, re-sorts, and refreshes the list view.
        /// Call after external data changes.
        /// </summary>
        public void Refresh()
        {
            RebuildFilteredList();
        }

        /// <summary>
        /// Returns the raw items at the currently selected (checked) indices.
        /// </summary>
        public List<object> GetSelectedItems()
        {
            var result = new List<object>();
            if (_rawItemsSource == null) return result;

            foreach (int rawIndex in _selectedRawIndices)
            {
                if (rawIndex >= 0 && rawIndex < _rawItemsSource.Count)
                    result.Add(_rawItemsSource[rawIndex]);
            }
            return result;
        }

        /// <summary>
        /// Returns the <see cref="PCPRowData"/> for all currently selected (checked) rows.
        /// </summary>
        public List<PCPRowData> GetSelectedRowData()
        {
            var result = new List<PCPRowData>();
            if (_rawItemsSource == null || _converter == null) return result;

            foreach (int rawIndex in _selectedRawIndices)
            {
                if (rawIndex >= 0 && rawIndex < _rawItemsSource.Count)
                    result.Add(_converter(_rawItemsSource[rawIndex]));
            }
            return result;
        }

        /// <summary>
        /// Clears all checkbox selections.
        /// </summary>
        public void ClearSelection()
        {
            _selectedRawIndices.Clear();
            RefreshListView();
            onSelectionChanged?.Invoke(new List<int>());
        }

        /// <summary>
        /// Sets the width for a specific column by index. Updates header and refreshes rows.
        /// Column indices: 0=Checkbox, 1=Icon, 2=Name, 3=Path, 4=Type, 5=Size, 6=Status.
        /// </summary>
        public void SetColumnWidth(int columnIndex, float width)
        {
            if (columnIndex < 0 || columnIndex >= _columns.Length) return;
            _columns[columnIndex].width = width;
            ApplyHeaderColumnWidth(columnIndex);
            RefreshListView();
        }

        /// <summary>
        /// Sets widths for all visible data columns at once (Name, Path, Type, Size, Status).
        /// Pass -1 for any column to keep its current width.
        /// </summary>
        public void SetColumnWidths(float name = -1, float path = -1, float type = -1, float size = -1, float status = -1)
        {
            if (name   >= 0) _columns[ColName].width   = name;
            if (path   >= 0) _columns[ColPath].width    = path;
            if (type   >= 0) _columns[ColType].width    = type;
            if (size   >= 0) _columns[ColSize].width    = size;
            if (status >= 0) _columns[ColStatus].width  = status;

            for (int i = 0; i < _columns.Length; i++)
                ApplyHeaderColumnWidth(i);

            RefreshListView();
        }

        private void ApplyHeaderColumnWidth(int colIndex)
        {
            if (colIndex < 0 || colIndex >= _headerRow.childCount) return;
            var cell = _headerRow[colIndex];
            float w = _columns[colIndex].width;
            cell.style.width = w;
            cell.style.minWidth = w;
        }

        private void ApplyRowColumnWidths(VisualElement row)
        {
            // Row children order: checkbox(0), iconCell(1), name(2), path(3), type(4), size(5), status(6)
            int childCount = row.childCount;
            for (int i = 0; i < _columns.Length && i < childCount; i++)
            {
                var child = row[i];
                float w = _columns[i].width;
                child.style.width = w;
                child.style.minWidth = w;
            }
        }

        /// <summary>
        /// Selects (checks) all currently visible (filtered) rows.
        /// </summary>
        public void SelectAll()
        {
            foreach (int rawIndex in _filteredIndices)
                _selectedRawIndices.Add(rawIndex);
            RefreshListView();
            onSelectionChanged?.Invoke(_selectedRawIndices.ToList());
        }

        // ----------------------------------------------------------------
        // Filtering & Sorting
        // ----------------------------------------------------------------

        private void RebuildFilteredList()
        {
            _filteredIndices.Clear();
            _filteredRows.Clear();

            if (_rawItemsSource == null || _converter == null)
            {
                ApplyToListView();
                return;
            }

            // Build row data and apply all filters
            string filterLower = _searchFilter.ToLowerInvariant();
            bool hasSearchFilter = !string.IsNullOrEmpty(filterLower);
            bool hasTypeFilter = _typeFilter != null && _typeFilter.Count > 0;
            bool hasOtherChip = hasTypeFilter && _typeFilter.Contains("Other");

            for (int i = 0; i < _rawItemsSource.Count; i++)
            {
                var row = _converter(_rawItemsSource[i]);

                // Search filter (name / path)
                if (hasSearchFilter)
                {
                    string nameLower = (row.name ?? string.Empty).ToLowerInvariant();
                    string pathLower = (row.path ?? string.Empty).ToLowerInvariant();
                    if (!nameLower.Contains(filterLower) && !pathLower.Contains(filterLower))
                        continue;
                }

                // Type chip filter
                if (hasTypeFilter)
                {
                    string rowType = row.type ?? string.Empty;
                    bool matchesNamedChip = false;
                    foreach (string chip in _typeFilter)
                    {
                        if (chip == "Other") continue;
                        if (rowType.IndexOf(chip, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matchesNamedChip = true;
                            break;
                        }
                    }

                    bool matchesOther = false;
                    if (hasOtherChip && !matchesNamedChip)
                    {
                        bool matchesAnyKnown = false;
                        foreach (string known in k_KnownTypeChips)
                        {
                            if (rowType.IndexOf(known, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matchesAnyKnown = true;
                                break;
                            }
                        }
                        matchesOther = !matchesAnyKnown;
                    }

                    if (!matchesNamedChip && !matchesOther)
                        continue;
                }

                // Status filter (exact match against row status text)
                if (_statusFilter != null)
                {
                    string rowStatus = row.status ?? string.Empty;
                    if (!string.Equals(rowStatus, _statusFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                _filteredIndices.Add(i);
                _filteredRows.Add(row);
            }

            // Apply sorting
            if (!string.IsNullOrEmpty(_sortColumn) && _filteredRows.Count > 1)
            {
                SortFilteredList();
            }

            ApplyToListView();
        }

        private void SortFilteredList()
        {
            // Build a paired list so we can sort indices alongside rows
            var paired = new List<(int rawIndex, PCPRowData row)>();
            for (int i = 0; i < _filteredRows.Count; i++)
                paired.Add((_filteredIndices[i], _filteredRows[i]));

            Comparison<(int rawIndex, PCPRowData row)> comparison = null;

            switch (_sortColumn)
            {
                case "Name":
                    comparison = (a, b) => string.Compare(
                        a.row.name ?? "", b.row.name ?? "", StringComparison.OrdinalIgnoreCase);
                    break;
                case "Path":
                    comparison = (a, b) => string.Compare(
                        a.row.path ?? "", b.row.path ?? "", StringComparison.OrdinalIgnoreCase);
                    break;
                case "Type":
                    comparison = (a, b) => string.Compare(
                        a.row.type ?? "", b.row.type ?? "", StringComparison.OrdinalIgnoreCase);
                    break;
                case "Size":
                    comparison = (a, b) => a.row.sizeBytes.CompareTo(b.row.sizeBytes);
                    break;
                case "Status":
                    comparison = (a, b) => string.Compare(
                        a.row.status ?? "", b.row.status ?? "", StringComparison.OrdinalIgnoreCase);
                    break;
            }

            if (comparison != null)
            {
                paired.Sort(comparison);
                if (!_sortAscending)
                    paired.Reverse();

                _filteredIndices.Clear();
                _filteredRows.Clear();
                foreach (var p in paired)
                {
                    _filteredIndices.Add(p.rawIndex);
                    _filteredRows.Add(p.row);
                }
            }
        }

        private void ApplyToListView()
        {
            // Provide the filtered rows as the list source
            _listView.itemsSource = _filteredRows as IList;
            RefreshListView();

            // Show/hide empty state
            bool isEmpty = _filteredRows.Count == 0;
            _emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Cross-version helper: RefreshItems() was added in 2021.2, replacing Refresh().
        /// </summary>
        private void RefreshListView()
        {
#if UNITY_2021_2_OR_NEWER
            _listView.RefreshItems();
#else
            _listView.Refresh();
#endif
        }
    }
}
