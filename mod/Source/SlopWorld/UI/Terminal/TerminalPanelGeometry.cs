using System;

namespace SlopWorld
{
    // Terminal dimensions come from the assigned panel, never a global last-used size.
    public static class TerminalPanelGeometry
    {
        public static bool TryMeasure(UiLayoutRect bounds, float cellWidth, float cellHeight,
                                      out int cols, out int rows)
        {
            cols = rows = 0;
            if (bounds.Width <= 0f || bounds.Height <= 0f ||
                cellWidth <= 0.01f || cellHeight <= 0.01f ||
                float.IsNaN(cellWidth) || float.IsNaN(cellHeight)) return false;
            var limits = DaemonCapabilities.Current.Terminal;
            int minCols = limits.MinCols < 1 || limits.MaxCols < limits.MinCols
                ? 20 : Math.Max(TerminalLimits.ClientMinCols,
                    Math.Min(TerminalLimits.ClientMaxCols, limits.MinCols));
            int maxCols = limits.MinCols < 1 || limits.MaxCols < limits.MinCols
                ? TerminalLimits.ClientMaxCols : Math.Max(minCols,
                    Math.Min(TerminalLimits.ClientMaxCols, limits.MaxCols));
            int minRows = limits.MinRows < 1 || limits.MaxRows < limits.MinRows
                ? 5 : Math.Max(TerminalLimits.ClientMinRows,
                    Math.Min(TerminalLimits.ClientMaxRows, limits.MinRows));
            int maxRows = limits.MinRows < 1 || limits.MaxRows < limits.MinRows
                ? TerminalLimits.ClientMaxRows : Math.Max(minRows,
                    Math.Min(TerminalLimits.ClientMaxRows, limits.MaxRows));
            cols = (int)Math.Max(minCols, Math.Min(maxCols,
                Math.Floor(bounds.Width / cellWidth)));
            rows = (int)Math.Max(minRows, Math.Min(maxRows,
                Math.Floor(bounds.Height / cellHeight)));
            return true;
        }
    }
}
