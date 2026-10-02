using System;

namespace SlopWorld
{
    // Terminal dimensions come from the assigned panel, never a global last-used size.
    public static class TerminalPanelGeometry
    {
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool TryMeasure(UiLayoutRect bounds, float cellWidth, float cellHeight,
                                      out int cols, out int rows)
        {
            cols = rows = 0;
            if (bounds.Width <= 0f || bounds.Height <= 0f ||
                cellWidth <= 0.01f || cellHeight <= 0.01f ||
                !Finite(bounds.Width) || !Finite(bounds.Height) ||
                !Finite(cellWidth) || !Finite(cellHeight)) return false;
            var limits = DaemonCapabilities.Current.Terminal;
            limits.EffectiveRange(out int minCols, out int maxCols, out int minRows, out int maxRows);
            cols = (int)Math.Max(minCols, Math.Min(maxCols,
                Math.Floor(bounds.Width / cellWidth)));
            rows = (int)Math.Max(minRows, Math.Min(maxRows,
                Math.Floor(bounds.Height / cellHeight)));
            return true;
        }
    }
}
