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
            cols = (int)Math.Max(limits.MinCols, Math.Min(limits.MaxCols,
                Math.Floor(bounds.Width / cellWidth)));
            rows = (int)Math.Max(limits.MinRows, Math.Min(limits.MaxRows,
                Math.Floor(bounds.Height / cellHeight)));
            return true;
        }
    }
}
