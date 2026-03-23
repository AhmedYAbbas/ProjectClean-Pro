using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// A single node in the treemap data hierarchy.
    /// </summary>
    public class PCPTreemapNode
    {
        /// <summary>Display name for this node (folder or file name).</summary>
        public string name;

        /// <summary>Size in bytes (leaf nodes) or aggregate size (parent nodes).</summary>
        public long size;

        /// <summary>Color used to fill this node's rectangle.</summary>
        public Color color;

        /// <summary>Child nodes. Empty for leaf nodes.</summary>
        public List<PCPTreemapNode> children = new List<PCPTreemapNode>();

        /// <summary>Project-relative path for this node.</summary>
        public string path;

        /// <summary>True if this node has children that can be drilled into.</summary>
        public bool HasChildren => children != null && children.Count > 0;
    }

    /// <summary>
    /// Custom-drawn treemap visualization using generateVisualContent.
    /// Implements squarified layout for optimal aspect ratios, breadcrumb
    /// navigation for drill-down, and hover tooltips.
    /// </summary>
#if UNITY_2023_2_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class PCPTreemapView : VisualElement
    {
#if !UNITY_2023_2_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PCPTreemapView, UxmlTraits> { }
#endif

        // USS class names
        public const string UssClassName = "pcp-treemap";
        public const string BreadcrumbUssClassName = "pcp-treemap__breadcrumb";

        // --------------------------------------------------------------------
        // Internal types
        // --------------------------------------------------------------------

        private struct LayoutRect
        {
            public Rect rect;
            public PCPTreemapNode node;
        }

        // Each Painter2D rect (fill + stroke) tessellates into ~20-30 vertices.
        // UI Toolkit enforces a 65535 vertex limit per VisualElement, so we cap
        // the number of rects to stay well within that budget.
        private const int k_MaxDrawnRects = 1500;
        private const float k_MinRectPixels = 3f;

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private PCPTreemapNode m_Root;
        private PCPTreemapNode m_CurrentNode;
        private readonly Stack<PCPTreemapNode> m_BreadcrumbStack = new Stack<PCPTreemapNode>();
        private readonly List<LayoutRect> m_LayoutRects = new List<LayoutRect>();
        private readonly VisualElement m_BreadcrumbBar;
        private readonly VisualElement m_DrawArea;
        private int m_HoveredIndex = -1;

        /// <summary>Raised when a node is clicked. Provides the clicked node.</summary>
        public event Action<PCPTreemapNode> onNodeClicked;

        // --------------------------------------------------------------------
        // Construction
        // --------------------------------------------------------------------

        public PCPTreemapView()
        {
            AddToClassList(UssClassName);
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // Breadcrumb navigation bar
            m_BreadcrumbBar = new VisualElement();
            m_BreadcrumbBar.AddToClassList(BreadcrumbUssClassName);
            m_BreadcrumbBar.style.flexDirection = FlexDirection.Row;
            m_BreadcrumbBar.style.flexWrap = Wrap.Wrap;
            m_BreadcrumbBar.style.alignItems = Align.Center;
            m_BreadcrumbBar.style.paddingLeft = 4;
            m_BreadcrumbBar.style.paddingRight = 4;
            m_BreadcrumbBar.style.paddingTop = 4;
            m_BreadcrumbBar.style.paddingBottom = 4;
            m_BreadcrumbBar.style.minHeight = 24;
            m_BreadcrumbBar.style.flexShrink = 0;
            m_BreadcrumbBar.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            Add(m_BreadcrumbBar);

            // Draw area for the treemap
            m_DrawArea = new VisualElement();
            m_DrawArea.style.flexGrow = 1;
            m_DrawArea.generateVisualContent += OnGenerateVisualContent;
            m_DrawArea.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            m_DrawArea.RegisterCallback<ClickEvent>(OnClick);
            m_DrawArea.RegisterCallback<GeometryChangedEvent>(evt => m_DrawArea.MarkDirtyRepaint());
            Add(m_DrawArea);
        }

        // --------------------------------------------------------------------
        // Public API
        // --------------------------------------------------------------------

        /// <summary>
        /// Sets the root data node and resets the view to the top level.
        /// </summary>
        public void SetData(PCPTreemapNode root)
        {
            m_Root = root;
            m_CurrentNode = root;
            m_BreadcrumbStack.Clear();
            RebuildBreadcrumbs();
            RecalculateLayout();
            m_DrawArea.MarkDirtyRepaint();
        }

        // --------------------------------------------------------------------
        // Breadcrumb navigation
        // --------------------------------------------------------------------

        private void RebuildBreadcrumbs()
        {
            m_BreadcrumbBar.Clear();

            // Build list from stack (bottom to top) + current
            var trail = new List<PCPTreemapNode>(m_BreadcrumbStack);
            trail.Reverse();
            trail.Add(m_CurrentNode);

            for (int i = 0; i < trail.Count; i++)
            {
                if (i > 0)
                {
                    var sep = new Label(" > ");
                    sep.style.fontSize = 11;
                    sep.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                    m_BreadcrumbBar.Add(sep);
                }

                bool isLast = i == trail.Count - 1;
                var node = trail[i];
                int depth = i;

                var crumb = new Button(() => NavigateTo(depth));
                crumb.text = node?.name ?? "(root)";
                crumb.style.paddingLeft = 4;
                crumb.style.paddingRight = 4;
                crumb.style.paddingTop = 1;
                crumb.style.paddingBottom = 1;
                crumb.style.fontSize = 11;
                crumb.style.borderTopWidth = 0;
                crumb.style.borderBottomWidth = 0;
                crumb.style.borderLeftWidth = 0;
                crumb.style.borderRightWidth = 0;
                crumb.style.backgroundColor = Color.clear;

                if (isLast)
                {
                    crumb.style.color = new Color(0.831f, 0.831f, 0.831f, 1f);
                    crumb.style.unityFontStyleAndWeight = FontStyle.Bold;
                    crumb.SetEnabled(false);
                }
                else
                {
                    crumb.style.color = new Color(0.337f, 0.612f, 0.839f, 1f);
                }

                m_BreadcrumbBar.Add(crumb);
            }

            // Size label
            if (m_CurrentNode != null)
            {
                var sizeLabel = new Label($"  ({PCPAssetInfo.FormatBytes(m_CurrentNode.size)})");
                sizeLabel.style.fontSize = 11;
                sizeLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
                m_BreadcrumbBar.Add(sizeLabel);
            }
        }

        private void NavigateTo(int depth)
        {
            // Pop back to the desired depth
            var trail = new List<PCPTreemapNode>(m_BreadcrumbStack);
            trail.Reverse();

            m_BreadcrumbStack.Clear();
            for (int i = 0; i < depth; i++)
            {
                if (i < trail.Count)
                    m_BreadcrumbStack.Push(trail[i]);
            }

            // The node at the target depth becomes current
            if (depth < trail.Count)
            {
                m_CurrentNode = trail[depth];
            }
            else if (depth == 0 && m_Root != null)
            {
                m_CurrentNode = m_Root;
            }

            // Reverse the stack to maintain correct order
            var temp = new List<PCPTreemapNode>(m_BreadcrumbStack);
            m_BreadcrumbStack.Clear();
            for (int i = temp.Count - 1; i >= 0; i--)
                m_BreadcrumbStack.Push(temp[i]);

            RebuildBreadcrumbs();
            RecalculateLayout();
            m_DrawArea.MarkDirtyRepaint();
        }

        private void DrillDown(PCPTreemapNode node)
        {
            if (node == null || !node.HasChildren)
                return;

            m_BreadcrumbStack.Push(m_CurrentNode);
            m_CurrentNode = node;
            RebuildBreadcrumbs();
            RecalculateLayout();
            m_DrawArea.MarkDirtyRepaint();
        }

        // --------------------------------------------------------------------
        // Layout calculation - squarified treemap
        // --------------------------------------------------------------------

        private void RecalculateLayout()
        {
            m_LayoutRects.Clear();
            m_HoveredIndex = -1;

            if (m_CurrentNode == null || !m_CurrentNode.HasChildren)
                return;

            float width = m_DrawArea.resolvedStyle.width;
            float height = m_DrawArea.resolvedStyle.height;

            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0)
                return;

            // Sort children by size descending for squarified layout
            var sorted = new List<PCPTreemapNode>(m_CurrentNode.children);
            sorted.Sort((a, b) => b.size.CompareTo(a.size));

            // Filter out zero-size nodes
            sorted.RemoveAll(n => n.size <= 0);

            if (sorted.Count == 0)
                return;

            // If there are too many nodes, merge the smallest ones into an
            // "Other" bucket so we don't exceed the vertex limit.
            if (sorted.Count > k_MaxDrawnRects)
            {
                long otherSize = 0;
                for (int i = k_MaxDrawnRects - 1; i < sorted.Count; i++)
                    otherSize += sorted[i].size;

                sorted.RemoveRange(k_MaxDrawnRects - 1, sorted.Count - (k_MaxDrawnRects - 1));

                if (otherSize > 0)
                {
                    sorted.Add(new PCPTreemapNode
                    {
                        name = $"Other ({sorted.Count} more)",
                        size = otherSize,
                        color = new Color(0.35f, 0.35f, 0.35f, 1f),
                        path = string.Empty
                    });
                }
            }

            var area = new Rect(0, 0, width, height);
            SquarifyLayout(sorted, area);
        }

        private void SquarifyLayout(List<PCPTreemapNode> nodes, Rect bounds)
        {
            if (nodes.Count == 0 || bounds.width <= 0 || bounds.height <= 0)
                return;

            long totalSize = 0;
            foreach (var n in nodes)
                totalSize += n.size;

            if (totalSize <= 0)
                return;

            float totalArea = bounds.width * bounds.height;
            var currentRow = new List<PCPTreemapNode>();
            float remainingArea = totalArea;
            long remainingSize = totalSize;
            var remainingBounds = bounds;

            int nodeIndex = 0;
            while (nodeIndex < nodes.Count)
            {
                currentRow.Clear();
                bool isVertical = remainingBounds.width >= remainingBounds.height;
                float sideLength = isVertical ? remainingBounds.height : remainingBounds.width;

                if (sideLength <= 0)
                    break;

                // Greedy: add nodes to row while aspect ratio improves
                float bestWorst = float.MaxValue;
                long rowSize = 0;

                while (nodeIndex < nodes.Count)
                {
                    var candidate = nodes[nodeIndex];
                    long candidateSize = candidate.size;
                    long newRowSize = rowSize + candidateSize;

                    // Calculate areas for the row items
                    float rowArea = (remainingArea * newRowSize) / remainingSize;
                    float rowLength = rowArea / sideLength;

                    if (rowLength <= 0)
                    {
                        nodeIndex++;
                        continue;
                    }

                    // Calculate worst aspect ratio with the new item
                    float worstAR = 0f;
                    long checkSize = 0;
                    for (int i = 0; i < currentRow.Count; i++)
                    {
                        checkSize += currentRow[i].size;
                    }
                    checkSize += candidateSize;

                    for (int i = 0; i < currentRow.Count; i++)
                    {
                        float itemArea = (rowArea * currentRow[i].size) / newRowSize;
                        float itemLength = itemArea / rowLength;
                        float ar = itemLength > rowLength
                            ? itemLength / rowLength
                            : rowLength / itemLength;
                        if (ar > worstAR)
                            worstAR = ar;
                    }

                    // Candidate's aspect ratio
                    {
                        float itemArea = (rowArea * candidateSize) / newRowSize;
                        float itemLength = itemArea / rowLength;
                        float ar = itemLength > rowLength
                            ? itemLength / rowLength
                            : rowLength / itemLength;
                        if (ar > worstAR)
                            worstAR = ar;
                    }

                    if (currentRow.Count > 0 && worstAR > bestWorst)
                    {
                        // Adding this node makes it worse; finalize the current row
                        break;
                    }

                    currentRow.Add(candidate);
                    rowSize = newRowSize;
                    bestWorst = worstAR;
                    nodeIndex++;
                }

                // Lay out the current row
                if (currentRow.Count > 0 && rowSize > 0)
                {
                    float rowArea = (remainingArea * rowSize) / remainingSize;
                    float rowLength = rowArea / sideLength;

                    float offset = 0f;

                    foreach (var node in currentRow)
                    {
                        float nodeArea = (rowArea * node.size) / rowSize;
                        float nodeLength = (sideLength > 0) ? nodeArea / rowLength : 0;

                        Rect nodeRect;
                        if (isVertical)
                        {
                            nodeRect = new Rect(
                                remainingBounds.x,
                                remainingBounds.y + offset,
                                rowLength,
                                nodeLength);
                        }
                        else
                        {
                            nodeRect = new Rect(
                                remainingBounds.x + offset,
                                remainingBounds.y,
                                nodeLength,
                                rowLength);
                        }

                        m_LayoutRects.Add(new LayoutRect { rect = nodeRect, node = node });
                        offset += nodeLength;
                    }

                    // Shrink remaining bounds
                    remainingArea -= rowArea;
                    remainingSize -= rowSize;

                    if (isVertical)
                    {
                        remainingBounds = new Rect(
                            remainingBounds.x + rowLength,
                            remainingBounds.y,
                            remainingBounds.width - rowLength,
                            remainingBounds.height);
                    }
                    else
                    {
                        remainingBounds = new Rect(
                            remainingBounds.x,
                            remainingBounds.y + rowLength,
                            remainingBounds.width,
                            remainingBounds.height - rowLength);
                    }
                }
            }
        }

        // --------------------------------------------------------------------
        // Rendering
        // --------------------------------------------------------------------

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            RecalculateLayout();

            if (m_LayoutRects.Count == 0)
                return;

            var painter = mgc.painter2D;
            const float padding = 1f;

            for (int i = 0; i < m_LayoutRects.Count; i++)
            {
                var lr = m_LayoutRects[i];
                var rect = lr.rect;

                // Inset slightly for padding between cells
                var inset = new Rect(
                    rect.x + padding,
                    rect.y + padding,
                    Mathf.Max(0, rect.width - padding * 2),
                    Mathf.Max(0, rect.height - padding * 2));

                if (inset.width < k_MinRectPixels || inset.height < k_MinRectPixels)
                    continue;

                // Fill color - slightly brighter if hovered
                Color fillColor = lr.node.color;
                if (i == m_HoveredIndex)
                {
                    fillColor = new Color(
                        Mathf.Min(1f, fillColor.r + 0.15f),
                        Mathf.Min(1f, fillColor.g + 0.15f),
                        Mathf.Min(1f, fillColor.b + 0.15f),
                        fillColor.a);
                }

                // Draw filled rectangle
                painter.fillColor = fillColor;
                painter.BeginPath();
                painter.MoveTo(new Vector2(inset.x, inset.y));
                painter.LineTo(new Vector2(inset.xMax, inset.y));
                painter.LineTo(new Vector2(inset.xMax, inset.yMax));
                painter.LineTo(new Vector2(inset.x, inset.yMax));
                painter.ClosePath();
                painter.Fill();

                // Draw border
                painter.strokeColor = new Color(0f, 0f, 0f, 0.4f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(inset.x, inset.y));
                painter.LineTo(new Vector2(inset.xMax, inset.y));
                painter.LineTo(new Vector2(inset.xMax, inset.yMax));
                painter.LineTo(new Vector2(inset.x, inset.yMax));
                painter.ClosePath();
                painter.Stroke();
            }
        }

        // --------------------------------------------------------------------
        // Interaction
        // --------------------------------------------------------------------

        private void OnMouseMove(MouseMoveEvent evt)
        {
            int newIndex = HitTest(evt.localMousePosition);
            if (newIndex != m_HoveredIndex)
            {
                m_HoveredIndex = newIndex;
                m_DrawArea.MarkDirtyRepaint();

                if (m_HoveredIndex >= 0 && m_HoveredIndex < m_LayoutRects.Count)
                {
                    var node = m_LayoutRects[m_HoveredIndex].node;
                    m_DrawArea.tooltip = $"{node.name}\n{PCPAssetInfo.FormatBytes(node.size)}";
                }
                else
                {
                    m_DrawArea.tooltip = string.Empty;
                }
            }
        }

        private void OnClick(ClickEvent evt)
        {
            int index = HitTest(evt.localPosition);
            if (index < 0 || index >= m_LayoutRects.Count)
                return;

            var node = m_LayoutRects[index].node;
            onNodeClicked?.Invoke(node);

            if (node.HasChildren)
            {
                DrillDown(node);
            }
        }

        private int HitTest(Vector2 localPos)
        {
            for (int i = 0; i < m_LayoutRects.Count; i++)
            {
                if (m_LayoutRects[i].rect.Contains(localPos))
                    return i;
            }
            return -1;
        }
    }
}
