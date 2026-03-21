using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using UnityEditor.Experimental.GraphView;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// Node in the dependency graph visualization. Displays an asset name
    /// with a colored header bar indicating asset type.
    /// </summary>
    public sealed class PCPAssetGraphNode : Node
    {
        /// <summary>The project-relative asset path this node represents.</summary>
        public string AssetPath { get; }

        /// <summary>Whether this node is the center/root of the current graph.</summary>
        public bool IsCenter { get; set; }

        public PCPAssetGraphNode(string assetPath, string displayName, Color headerColor)
        {
            AssetPath = assetPath;
            title = displayName;
            style.minWidth = 140;

            // Color the header to indicate asset type
            var titleContainer = this.Q("title");
            if (titleContainer != null)
            {
                titleContainer.style.backgroundColor = headerColor;

                // Use dark text for readability on bright header backgrounds
                var titleLabel = titleContainer.Q<Label>("title-label");
                if (titleLabel != null)
                {
                    titleLabel.style.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                }
            }

            // Add input and output ports (read-only — not interactable)
            var inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input,
                Port.Capacity.Multi, typeof(bool));
            inputPort.portName = "Used By";
            inputPort.SetEnabled(false);
            inputContainer.Add(inputPort);

            var outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output,
                Port.Capacity.Multi, typeof(bool));
            outputPort.portName = "Depends On";
            outputPort.SetEnabled(false);
            outputContainer.Add(outputPort);

            RefreshExpandedState();
            RefreshPorts();
        }
    }

    /// <summary>
    /// GraphView subclass that provides the interactive dependency graph canvas.
    /// Supports zooming, panning, selection, and minimap.
    /// </summary>
    public sealed class PCPDependencyGraphElement : GraphView
    {
        public PCPDependencyGraphElement()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // Background grid
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            style.flexGrow = 1;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            // Read-only view — no new connections allowed.
            return new List<Port>();
        }

        public override EventPropagation DeleteSelection()
        {
            // Read-only view — prevent deletion of nodes and edges.
            return EventPropagation.Stop;
        }
    }

    /// <summary>
    /// View for exploring the asset dependency graph. Contains a toolbar with
    /// an ObjectField for selecting a center asset and depth control, plus a
    /// <see cref="PCPDependencyGraphElement"/> canvas that displays nodes and edges.
    /// </summary>
    public sealed class PCPDependencyGraphView : VisualElement, IPCPRefreshable
    {
        // Asset type color mapping
        private static readonly Color k_TextureColor = new Color(0.400f, 0.600f, 0.800f, 1f);
        private static readonly Color k_MaterialColor = new Color(0.600f, 0.400f, 0.800f, 1f);
        private static readonly Color k_MeshColor = new Color(0.800f, 0.600f, 0.400f, 1f);
        private static readonly Color k_PrefabColor = new Color(0.400f, 0.800f, 0.600f, 1f);
        private static readonly Color k_SceneColor = new Color(0.800f, 0.400f, 0.400f, 1f);
        private static readonly Color k_ScriptColor = new Color(0.700f, 0.700f, 0.400f, 1f);
        private static readonly Color k_AudioColor = new Color(0.800f, 0.500f, 0.700f, 1f);
        private static readonly Color k_ShaderColor = new Color(0.500f, 0.800f, 0.800f, 1f);
        private static readonly Color k_DefaultColor = new Color(0.500f, 0.500f, 0.500f, 1f);
        private static readonly Color k_CircularEdgeColor = new Color(0.957f, 0.278f, 0.278f, 1f);

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------

        private readonly ObjectField m_AssetField;
        private readonly IntegerField m_DepthField;
        private readonly PCPDependencyGraphElement m_GraphView;
        private readonly Label m_StatusLabel;
        private int m_MaxDepth = PCPSettings.instance.dependencyGraphMaxDepth;
        private bool m_SettingsChangedWhileDetached;

        // Track nodes by path for edge creation
        private readonly Dictionary<string, PCPAssetGraphNode> m_NodeMap =
            new Dictionary<string, PCPAssetGraphNode>(StringComparer.Ordinal);

        // Track visited paths for circular dependency detection
        private readonly HashSet<string> m_VisitedPaths =
            new HashSet<string>(StringComparer.Ordinal);

        // Parent → children mapping built during subgraph construction (for layout)
        private readonly Dictionary<string, List<string>> m_ChildrenMap =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPDependencyGraphView()
        {
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // ---- Toolbar ----
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.paddingTop = 4;
            toolbar.style.paddingBottom = 4;
            toolbar.style.backgroundColor = new Color(0.176f, 0.176f, 0.176f, 1f);
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            toolbar.style.flexShrink = 0;

            // Asset selector
            var assetLabel = new Label("Center Asset:");
            assetLabel.style.fontSize = 12;
            assetLabel.style.marginRight = 4;
            toolbar.Add(assetLabel);

            m_AssetField = new ObjectField();
            m_AssetField.objectType = typeof(UnityEngine.Object);
            m_AssetField.style.flexGrow = 1;
            m_AssetField.style.minWidth = 200;
            m_AssetField.style.marginRight = 12;
            m_AssetField.RegisterValueChangedCallback(OnAssetChanged);
            toolbar.Add(m_AssetField);

            // Depth control
            var depthLabel = new Label("Depth:");
            depthLabel.style.fontSize = 12;
            depthLabel.style.marginRight = 4;
            toolbar.Add(depthLabel);

            m_DepthField = new IntegerField();
            m_DepthField.value = m_MaxDepth;
            m_DepthField.style.width = 50;
            m_DepthField.style.marginRight = 8;
            m_DepthField.RegisterValueChangedCallback(evt =>
            {
                m_MaxDepth = Mathf.Clamp(evt.newValue, 1, PCPSettings.instance.dependencyGraphMaxDepth);
                m_DepthField.SetValueWithoutNotify(m_MaxDepth);
            });
            toolbar.Add(m_DepthField);

            // Refresh button
            var refreshBtn = new Button(RefreshGraph) { text = "Refresh" };
            refreshBtn.AddToClassList("pcp-button-secondary");
            refreshBtn.style.paddingLeft = 12;
            refreshBtn.style.paddingRight = 12;
            refreshBtn.style.paddingTop = 4;
            refreshBtn.style.paddingBottom = 4;
            toolbar.Add(refreshBtn);

            Add(toolbar);

            // ---- Status label ----
            m_StatusLabel = new Label("Select an asset to visualize its dependency graph.");
            m_StatusLabel.style.fontSize = 12;
            m_StatusLabel.style.color = new Color(0.502f, 0.502f, 0.502f, 1f);
            m_StatusLabel.style.paddingLeft = 8;
            m_StatusLabel.style.paddingTop = 4;
            m_StatusLabel.style.paddingBottom = 4;
            m_StatusLabel.style.flexShrink = 0;
            Add(m_StatusLabel);

            // ---- Graph view ----
            m_GraphView = new PCPDependencyGraphElement();
            m_GraphView.style.flexGrow = 1;
            Add(m_GraphView);

            // Listen for settings changes.
            // The view is detached on tab switch but the instance stays alive,
            // so we keep the subscription and defer the refresh until re-attached.
            PCPSettings.OnSettingsSaved += OnSettingsChanged;
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (m_SettingsChangedWhileDetached)
            {
                m_SettingsChangedWhileDetached = false;
                ApplySettingsMaxDepth();
            }
        }

        private void OnSettingsChanged()
        {
            if (panel == null)
            {
                // Not currently visible — defer until re-attached.
                m_SettingsChangedWhileDetached = true;
                return;
            }

            ApplySettingsMaxDepth();
        }

        private void ApplySettingsMaxDepth()
        {
            int settingsMax = PCPSettings.instance.dependencyGraphMaxDepth;
            m_MaxDepth = Mathf.Clamp(m_MaxDepth, 1, settingsMax);
            m_DepthField.SetValueWithoutNotify(m_MaxDepth);
            RefreshGraph();
        }

        // --------------------------------------------------------------------
        // Event handlers
        // --------------------------------------------------------------------

        private void OnAssetChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            RefreshGraph();
        }

        // --------------------------------------------------------------------
        // Graph building
        // --------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the graph from the currently selected asset.
        /// </summary>
        public void Refresh() => RefreshGraph();

        public void RefreshGraph()
        {
            ClearGraph();

            var asset = m_AssetField.value;
            if (asset == null)
            {
                m_StatusLabel.text = "Select an asset to visualize its dependency graph.";
                return;
            }

            string centerPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(centerPath))
            {
                m_StatusLabel.text = "Could not resolve asset path.";
                return;
            }

            // Build subgraph from Unity's dependency API
            m_VisitedPaths.Clear();
            BuildSubgraph(centerPath, 0, m_MaxDepth);

            if (m_NodeMap.Count == 0)
            {
                m_StatusLabel.text = "No dependencies found for this asset.";
                return;
            }

            // Mark the center node
            if (m_NodeMap.TryGetValue(centerPath, out var centerNode))
            {
                centerNode.IsCenter = true;
                var titleEl = centerNode.Q("title");
                if (titleEl != null)
                {
                    titleEl.style.borderBottomWidth = 3;
                    titleEl.style.borderBottomColor = new Color(0.337f, 0.612f, 0.839f, 1f);
                }
            }

            // Layout nodes in a left-to-right hierarchical tree
            ArrangeHierarchicalLayout(centerPath);

            m_StatusLabel.text = $"Showing {m_NodeMap.Count} node(s) for \"{centerPath}\" (depth {m_MaxDepth}).";
        }

        private void BuildSubgraph(string assetPath, int currentDepth, int maxDepth)
        {
            if (string.IsNullOrEmpty(assetPath) || currentDepth > maxDepth)
                return;

            if (m_VisitedPaths.Contains(assetPath))
                return;

            m_VisitedPaths.Add(assetPath);

            // Create node if not exists
            if (!m_NodeMap.ContainsKey(assetPath))
            {
                var node = CreateGraphNode(assetPath);
                m_NodeMap[assetPath] = node;
                m_GraphView.AddElement(node);
            }

            if (currentDepth >= maxDepth)
                return;

            // Get direct dependencies
            string[] deps = AssetDatabase.GetDependencies(assetPath, false);
            foreach (string dep in deps)
            {
                if (string.Equals(dep, assetPath, StringComparison.Ordinal))
                    continue;

                if (!dep.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Track parent → child for layout
                if (!m_ChildrenMap.TryGetValue(assetPath, out var childList))
                {
                    childList = new List<string>();
                    m_ChildrenMap[assetPath] = childList;
                }
                childList.Add(dep);

                // Recursively build subgraph
                BuildSubgraph(dep, currentDepth + 1, maxDepth);

                // Create edge
                if (m_NodeMap.TryGetValue(assetPath, out var sourceNode) &&
                    m_NodeMap.TryGetValue(dep, out var targetNode))
                {
                    var sourcePort = sourceNode.Q<Port>("", "output");
                    if (sourcePort == null)
                    {
                        sourcePort = sourceNode.outputContainer.Q<Port>();
                    }

                    var targetPort = targetNode.Q<Port>("", "input");
                    if (targetPort == null)
                    {
                        targetPort = targetNode.inputContainer.Q<Port>();
                    }

                    if (sourcePort != null && targetPort != null)
                    {
                        var edge = sourcePort.ConnectTo(targetPort);
                        edge.capabilities &= ~Capabilities.Deletable;
                        edge.capabilities &= ~Capabilities.Selectable;

                        // Check for circular dependency
                        bool isCircular = IsCircularDependency(assetPath, dep);
                        if (isCircular)
                        {
                            edge.style.color = k_CircularEdgeColor;
                            edge.tooltip = "Circular dependency detected!";
                        }

                        m_GraphView.AddElement(edge);
                    }
                }
            }
        }

        private bool IsCircularDependency(string from, string to)
        {
            // Simple check: does 'to' have a dependency path back to 'from'?
            string[] toDeps = AssetDatabase.GetDependencies(to, false);
            foreach (string dep in toDeps)
            {
                if (string.Equals(dep, from, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private PCPAssetGraphNode CreateGraphNode(string assetPath)
        {
            string displayName = System.IO.Path.GetFileName(assetPath);
            Color headerColor = GetColorForAssetType(assetPath);

            var node = new PCPAssetGraphNode(assetPath, displayName, headerColor);
            node.capabilities &= ~Capabilities.Deletable;
            node.tooltip = assetPath;

            // Double-click to re-center on this asset
            node.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (obj != null)
                    {
                        m_AssetField.value = obj;
                        RefreshGraph();
                    }
                }
            });

            return node;
        }

        // --------------------------------------------------------------------
        // Layout
        // --------------------------------------------------------------------

        /// <summary>
        /// Arranges nodes in a left-to-right hierarchical tree layout.
        /// Each depth level occupies its own column, and children are stacked
        /// vertically beneath their parent to minimize edge crossings.
        /// </summary>
        private void ArrangeHierarchicalLayout(string centerPath)
        {
            if (m_NodeMap.Count == 0)
                return;

            const float columnSpacing = 280f;
            const float rowSpacing = 80f;
            const float startX = 50f;
            const float startY = 50f;

            // BFS to assign each node to a depth layer.
            var depthMap = new Dictionary<string, int>(StringComparer.Ordinal);
            var parentMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(centerPath);
            depthMap[centerPath] = 0;

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                int depth = depthMap[current];

                if (!m_ChildrenMap.TryGetValue(current, out var children))
                    continue;

                foreach (string child in children)
                {
                    if (!m_NodeMap.ContainsKey(child))
                        continue;

                    // Only visit each node once to prevent infinite loops
                    // on circular dependencies.
                    if (depthMap.ContainsKey(child))
                        continue;

                    depthMap[child] = depth + 1;
                    parentMap[child] = current;
                    queue.Enqueue(child);
                }
            }

            // Include orphan nodes (not reachable from root)
            foreach (var kvp in m_NodeMap)
            {
                if (!depthMap.ContainsKey(kvp.Key))
                    depthMap[kvp.Key] = 1;
            }

            // Group nodes by depth
            int maxDepth = 0;
            var layers = new Dictionary<int, List<string>>();
            foreach (var kvp in depthMap)
            {
                if (!layers.ContainsKey(kvp.Value))
                    layers[kvp.Value] = new List<string>();
                layers[kvp.Value].Add(kvp.Key);
                if (kvp.Value > maxDepth)
                    maxDepth = kvp.Value;
            }

            // Order children within each layer so that nodes sharing a parent
            // are grouped together and ordered by their parent's position.
            // This is a simple barycenter heuristic that reduces crossings.
            for (int d = 1; d <= maxDepth; d++)
            {
                if (!layers.ContainsKey(d))
                    continue;

                var layer = layers[d];
                var prevLayer = layers.ContainsKey(d - 1) ? layers[d - 1] : null;

                if (prevLayer != null)
                {
                    // Build index lookup for parent positions
                    var parentIndex = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int i = 0; i < prevLayer.Count; i++)
                        parentIndex[prevLayer[i]] = i;

                    // Sort by parent index so siblings stay together
                    layer.Sort((a, b) =>
                    {
                        parentMap.TryGetValue(a, out string pA);
                        parentMap.TryGetValue(b, out string pB);
                        int idxA = pA != null && parentIndex.ContainsKey(pA) ? parentIndex[pA] : int.MaxValue;
                        int idxB = pB != null && parentIndex.ContainsKey(pB) ? parentIndex[pB] : int.MaxValue;
                        return idxA.CompareTo(idxB);
                    });
                }
            }

            // Position nodes: each depth is a column, nodes stacked vertically.
            // After initial placement, vertically center each parent over its children.
            var positionY = new Dictionary<string, float>(StringComparer.Ordinal);

            // First pass: assign y positions per layer top-to-bottom
            foreach (var kvp in layers)
            {
                var layer = kvp.Value;
                for (int i = 0; i < layer.Count; i++)
                {
                    positionY[layer[i]] = startY + i * rowSpacing;
                }
            }

            // Second pass (bottom-up): shift parents to the vertical center of
            // their children for a cleaner look.
            for (int d = maxDepth - 1; d >= 0; d--)
            {
                if (!layers.ContainsKey(d))
                    continue;

                foreach (string nodePath in layers[d])
                {
                    if (!m_ChildrenMap.TryGetValue(nodePath, out var children) || children.Count == 0)
                        continue;

                    // Collect y positions of children in the graph
                    float minY = float.MaxValue;
                    float maxY = float.MinValue;
                    int childCount = 0;
                    foreach (string child in children)
                    {
                        if (positionY.TryGetValue(child, out float cy))
                        {
                            if (cy < minY) minY = cy;
                            if (cy > maxY) maxY = cy;
                            childCount++;
                        }
                    }

                    if (childCount > 0)
                    {
                        positionY[nodePath] = (minY + maxY) / 2f;
                    }
                }
            }

            // Third pass: resolve vertical overlaps within each layer
            foreach (var kvp in layers)
            {
                var layer = kvp.Value;
                // Sort by current y position
                layer.Sort((a, b) => positionY[a].CompareTo(positionY[b]));

                for (int i = 1; i < layer.Count; i++)
                {
                    float prevBottom = positionY[layer[i - 1]] + rowSpacing;
                    if (positionY[layer[i]] < prevBottom)
                    {
                        positionY[layer[i]] = prevBottom;
                    }
                }
            }

            // Apply positions
            foreach (var kvp in depthMap)
            {
                string path = kvp.Key;
                int depth = kvp.Value;
                float x = startX + depth * columnSpacing;
                float y = positionY.ContainsKey(path) ? positionY[path] : startY;

                if (m_NodeMap.TryGetValue(path, out var node))
                {
                    node.SetPosition(new Rect(x, y, 0, 0));
                }
            }
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void ClearGraph()
        {
            m_NodeMap.Clear();
            m_VisitedPaths.Clear();
            m_ChildrenMap.Clear();

            // Remove all elements from the graph view
            var elements = new List<GraphElement>();
            m_GraphView.graphElements.ForEach(e => elements.Add(e));
            foreach (var element in elements)
            {
                m_GraphView.RemoveElement(element);
            }
        }

        private static Color GetColorForAssetType(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return k_DefaultColor;

            string ext = System.IO.Path.GetExtension(assetPath).ToLowerInvariant();

            switch (ext)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".tif":
                case ".tiff":
                case ".bmp":
                case ".exr":
                case ".hdr":
                    return k_TextureColor;

                case ".mat":
                    return k_MaterialColor;

                case ".fbx":
                case ".obj":
                case ".blend":
                case ".dae":
                case ".3ds":
                case ".mesh":
                    return k_MeshColor;

                case ".prefab":
                    return k_PrefabColor;

                case ".unity":
                    return k_SceneColor;

                case ".cs":
                case ".dll":
                case ".asmdef":
                case ".asmref":
                    return k_ScriptColor;

                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".aiff":
                    return k_AudioColor;

                case ".shader":
                case ".shadergraph":
                case ".cginc":
                case ".hlsl":
                    return k_ShaderColor;

                default:
                    return k_DefaultColor;
            }
        }
    }
}
