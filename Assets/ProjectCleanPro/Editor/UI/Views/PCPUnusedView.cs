using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCleanPro.Editor
{
    /// <summary>
    /// View for displaying unused asset scan results. Extends <see cref="PCPModuleView"/>
    /// and overrides <see cref="PopulateResults"/> to convert <see cref="PCPUnusedAsset"/>
    /// entries into rows with appropriate status badges.
    /// </summary>
    public sealed class PCPUnusedView : PCPModuleView
    {
        // Colors for status badges
        private static readonly Color k_UnusedColor      = new Color(0.753f, 0.224f, 0.169f, 1f);
        private static readonly Color k_InResourcesColor = new Color(0.800f, 0.655f, 0.000f, 1f);

        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------

        public PCPUnusedView(PCPScanResult scanResult, Func<PCPScanContext> createContext)
            : base(
                scanResult,
                createContext,
                "Unused Assets",
                "\u2718",
                0)
        {
            // Unused view: emphasize Name and Path, standard Size/Status
            m_ResultList.SetColumnWidths(name: 200, path: 280, type: 90, size: 80, status: 90);
            m_FilterBar.SetStatusChoices("All Statuses", "UNUSED", "IN RESOURCES");
        }

        // --------------------------------------------------------------------
        // PopulateResults override
        // --------------------------------------------------------------------

        protected override void PopulateResults()
        {
            if (m_ScanResult == null || m_ScanResult.unusedAssets == null)
            {
                m_ResultList.SetData(new ArrayList(), _ => default);
                UpdateHeader(0, 0);
                return;
            }

            var items = m_ScanResult.unusedAssets;
            m_ResultList.SetData(items, ConvertRow);
            UpdateHeader(items.Count, CalculateTotalSize(items));
        }

        protected override void DoModuleScan(PCPScanContext context)
        {
            var scanner = new PCPUnusedScanner();
            scanner.Scan(context);

            m_ScanResult.unusedAssets.Clear();
            foreach (var result in scanner.Results)
                m_ScanResult.unusedAssets.Add(result);
        }

        // --------------------------------------------------------------------
        // Row conversion
        // --------------------------------------------------------------------

        private PCPRowData ConvertRow(object item)
        {
            var unused = item as PCPUnusedAsset;
            if (unused == null || unused.assetInfo == null)
                return default;

            var info = unused.assetInfo;

            string status;
            Color  statusColor;
            if (unused.isInResources)
            {
                status      = "IN RESOURCES";
                statusColor = k_InResourcesColor;
            }
            else
            {
                status      = "UNUSED";
                statusColor = k_UnusedColor;
            }

            Texture2D icon = null;
            if (!string.IsNullOrEmpty(info.path))
                icon = AssetDatabase.GetCachedIcon(info.path) as Texture2D;

            return new PCPRowData
            {
                selected    = false,
                icon        = icon,
                name        = info.name        ?? string.Empty,
                path        = info.path        ?? string.Empty,
                type        = info.assetTypeName ?? "Unknown",
                sizeBytes   = info.sizeBytes,
                status      = status,
                statusColor = statusColor,
                guid        = info.guid ?? string.Empty
            };
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static long CalculateTotalSize(IList items)
        {
            long total = 0;
            foreach (var obj in items)
            {
                if (obj is PCPUnusedAsset asset)
                    total += asset.SizeBytes;
            }
            return total;
        }

        private void UpdateHeader(int count, long totalSize)
        {
            m_Header.FindingCount = count;
            m_Header.TotalSize    = totalSize;
        }
    }
}
