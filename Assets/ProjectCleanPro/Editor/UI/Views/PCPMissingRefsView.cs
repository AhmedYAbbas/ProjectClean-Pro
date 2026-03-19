using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for displaying missing reference scan results. Extends <see cref="PCPModuleView"/>
    /// and overrides <see cref="PopulateResults"/> to convert <see cref="PCPMissingReference"/>
    /// entries into rows with severity badges (ERROR, WARNING, INFO).
    /// Columns: severity, source asset, component type, property path, missing GUID.
    /// </summary>
    public sealed class PCPMissingRefsView : PCPModuleView
    {
        // Badge colors
        private static readonly Color k_ErrorColor = new Color(0.957f, 0.278f, 0.278f, 1f);
        private static readonly Color k_WarningColor = new Color(0.800f, 0.655f, 0.000f, 1f);
        private static readonly Color k_InfoColor = new Color(0.337f, 0.612f, 0.839f, 1f);

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPMissingRefsView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
            : base(
                scanResult,
                createContext,
                "Missing References",
                "\u26A0",
                new Color(0.306f, 0.804f, 0.769f, 1f))
        {
            // Missing refs: wider Type column for component.property paths
            m_ResultList.SetColumnWidths(name: 160, path: 220, type: 180, size: 60, status: 90);
        }

        // --------------------------------------------------------------------
        // PopulateResults override
        // --------------------------------------------------------------------

        protected override void PopulateResults()
        {
            if (m_ScanResult == null || m_ScanResult.missingReferences == null)
            {
                m_ResultList.SetData(new ArrayList(), _ => default);
                UpdateHeader(0);
                return;
            }

            var items = m_ScanResult.missingReferences;
            m_ResultList.SetData(items as IList, ConvertRow);
            UpdateHeader(items.Count);
        }

        // --------------------------------------------------------------------
        // Row conversion
        // --------------------------------------------------------------------

        private PCPRowData ConvertRow(object item)
        {
            var missingRef = item as PCPMissingReference;
            if (missingRef == null)
                return default;

            // Determine status badge text and color based on severity
            string status;
            Color statusColor;
            switch (missingRef.severity)
            {
                case PCPSeverity.Error:
                    status = "ERROR";
                    statusColor = k_ErrorColor;
                    break;
                case PCPSeverity.Warning:
                    status = "WARNING";
                    statusColor = k_WarningColor;
                    break;
                default:
                    status = "INFO";
                    statusColor = k_InfoColor;
                    break;
            }

            // Load icon for the source asset
            Texture2D icon = null;
            if (!string.IsNullOrEmpty(missingRef.sourceAssetPath))
            {
                icon = AssetDatabase.GetCachedIcon(missingRef.sourceAssetPath) as Texture2D;
            }

            // Build display name from component type and property path
            string displayName = missingRef.sourceAssetName ?? string.Empty;
            if (string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(missingRef.sourceAssetPath))
            {
                displayName = System.IO.Path.GetFileNameWithoutExtension(missingRef.sourceAssetPath);
            }

            // Show component and property in the type column
            string typeInfo = string.Empty;
            if (!string.IsNullOrEmpty(missingRef.componentType))
            {
                typeInfo = missingRef.componentType;
                if (!string.IsNullOrEmpty(missingRef.propertyPath))
                {
                    // Truncate long property paths for display
                    string propPath = missingRef.propertyPath;
                    if (propPath.Length > 40)
                        propPath = "..." + propPath.Substring(propPath.Length - 37);
                    typeInfo += "." + propPath;
                }
            }

            // Build path column: show game object hierarchy if available
            string pathDisplay = missingRef.sourceAssetPath ?? string.Empty;
            if (!string.IsNullOrEmpty(missingRef.gameObjectPath))
            {
                pathDisplay += " > " + missingRef.gameObjectPath;
            }

            return new PCPRowData
            {
                selected = false,
                icon = icon,
                name = displayName,
                path = pathDisplay,
                type = typeInfo,
                sizeBytes = 0,
                status = status,
                statusColor = statusColor,
                guid = missingRef.missingGuid ?? string.Empty
            };
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void UpdateHeader(int count)
        {
            m_Header.FindingCount = count;
            m_Header.TotalSize = 0;
        }
    }
}
