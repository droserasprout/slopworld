using System;

namespace SlopWorld
{
    internal enum TerminalRepaint { None, Rows, Full }

    internal struct TerminalCacheKey
    {
        public ScreenBuf Buffer;
        public string Session;
        public int Offset, Theme, Font;
        public float X, Y, Width, Height, CellW, CellH, Lead;

        public bool Matches(TerminalCacheKey other) =>
            ReferenceEquals(Buffer, other.Buffer) && Session == other.Session &&
            Offset == other.Offset && Theme == other.Theme && Font == other.Font &&
            X == other.X && Y == other.Y && Width == other.Width && Height == other.Height &&
            CellW == other.CellW && CellH == other.CellH && Lead == other.Lead;
    }

    internal static class TerminalRepaintPolicy
    {
        public static TerminalRepaint Choose(bool invalidated, int previousRevision, ScreenBuf screen)
        {
            if (invalidated) return TerminalRepaint.Full;
            if (previousRevision == screen.ContentRevision) return TerminalRepaint.None;
            // Missing damage metadata cannot establish which pixels are still valid.
            int changed = screen.ChangedRows?.Length ?? 0;
            if (changed == 0 || changed * 2 >= Math.Max(1, screen.Lines?.Length ?? 0))
                return TerminalRepaint.Full;
            return TerminalRepaint.Rows;
        }
    }
}
