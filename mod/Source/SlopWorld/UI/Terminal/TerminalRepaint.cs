using System;

namespace SlopWorld
{
    internal enum TerminalRepaint { None, Rows, Full }

    internal struct TerminalCacheKey
    {
        public ScreenBuf Buffer;
        public string Session;
        public int Offset, Theme, Font;
        public bool AltScreen;
        public float X, Y, Width, Height, CellW, CellH, Lead;

        public bool Matches(TerminalCacheKey other) =>
            ReferenceEquals(Buffer, other.Buffer) && Session == other.Session &&
            Offset == other.Offset && AltScreen == other.AltScreen &&
            Theme == other.Theme && Font == other.Font &&
            X == other.X && Y == other.Y && Width == other.Width && Height == other.Height &&
            CellW == other.CellW && CellH == other.CellH && Lead == other.Lead;
    }

    internal static class TerminalRepaintPolicy
    {
        public static TerminalRepaint Choose(bool invalidated, int previousRevision, ScreenBuf screen)
        {
            if (invalidated) return TerminalRepaint.Full;
            if (previousRevision == screen.ContentRevision) return TerminalRepaint.None;
            // ChangedRows describes only the latest received frame. If painting skipped a
            // content revision, earlier damage is no longer represented by that row list.
            if (screen.ContentRevision != unchecked(previousRevision + 1))
                return TerminalRepaint.Full;
            // Missing damage metadata cannot establish which pixels are still valid.
            int changed = screen.ChangedRows?.Length ?? 0;
            if (changed == 0 || changed * 2 >= Math.Max(1, screen.Lines?.Length ?? 0))
                return TerminalRepaint.Full;
            return TerminalRepaint.Rows;
        }
    }
}
