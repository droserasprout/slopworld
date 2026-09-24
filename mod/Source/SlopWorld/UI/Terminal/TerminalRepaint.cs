using System;

namespace SlopWorld
{
    internal enum TerminalRepaint { None, Rows, Full }

    internal static class TerminalCacheSampling
    {
        // UI.screenWidth/Height can round a scaled viewport up by a fraction of a
        // physical pixel. Clamp that edge to the texture; larger excursions mean
        // the cache cannot safely provide the requested pixels.
        public static bool TryClamp(int width, int height,
            ref float x0, ref float y0, ref float x1, ref float y1)
        {
            if (width <= 0 || height <= 0 || !(x1 > x0 && y1 > y0) ||
                x0 < -1f || y0 < -1f || x1 > width + 1f || y1 > height + 1f)
                return false;
            x0 = Math.Max(0f, x0);
            y0 = Math.Max(0f, y0);
            x1 = Math.Min(width, x1);
            y1 = Math.Min(height, y1);
            return x1 > x0 && y1 > y0;
        }
    }

    internal struct TerminalCacheKey
    {
        public ScreenBuf Buffer;
        public string Session;
        public int Offset, Theme, Font;
        public bool AltScreen;
        public float X, Y, Width, Height, CellW, CellH, Lead;

        public bool Matches(TerminalCacheKey other) =>
            ReferenceEquals(Buffer, other.Buffer) && Offset == other.Offset &&
            SameSurface(other);

        public bool SameSurface(TerminalCacheKey other) =>
            Session == other.Session && AltScreen == other.AltScreen &&
            Theme == other.Theme && Font == other.Font &&
            X == other.X && Y == other.Y && Width == other.Width && Height == other.Height &&
            CellW == other.CellW && CellH == other.CellH && Lead == other.Lead;
    }

    internal static class TerminalScrollReuse
    {
        // A history anchor moves old row i to new row i + (new offset - old offset).
        // This is only an opportunity measurement: the cache still repaints the full pane.
        public static int MatchingRows(TerminalCacheKey old, TerminalCacheKey next)
        {
            var before = old.Buffer;
            var after = next.Buffer;
            if (!old.SameSurface(next) || old.AltScreen || old.Offset <= 0 ||
                next.Offset <= 0 || old.Offset == next.Offset ||
                before == null || after == null || ReferenceEquals(before, after) ||
                before.Off != old.Offset || after.Off != next.Offset ||
                !before.LinksKnown || !after.LinksKnown || before.HasLinks || after.HasLinks ||
                before.Lines == null || after.Lines == null)
                return 0;

            int delta = next.Offset - old.Offset;
            int oldStart = Math.Max(0, -delta);
            int newStart = Math.Max(0, delta);
            int count = Math.Min(before.Lines.Length - oldStart,
                                 after.Lines.Length - newStart);
            if (count <= 0) return 0;
            for (int row = 0; row < count; row++)
                if (!string.Equals(before.Lines[oldStart + row],
                                   after.Lines[newStart + row], StringComparison.Ordinal))
                    return 0;
            return count;
        }

        public static bool PixelAligned(int oldOffset, int newOffset,
                                        float cellHeight, float screenScaleY)
        {
            double shift = (double)(newOffset - oldOffset) * cellHeight * screenScaleY;
            return !double.IsNaN(shift) && !double.IsInfinity(shift) &&
                   Math.Abs(shift - Math.Round(shift)) <= 0.01;
        }

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
