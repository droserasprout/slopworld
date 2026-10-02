using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Link hit testing owns the row-spanning link state used by hover painting and clicks.
    // TerminalPanel owns pane geometry and input policy. This class only understands
    // parsed terminal runs.
    internal sealed class TerminalLinkService
    {
        internal struct HoverSpan
        {
            public int Row, StartColumn, EndColumnExclusive;

            public HoverSpan(int row, int startColumn, int endColumnExclusive)
            {
                Row = row;
                StartColumn = startColumn;
                EndColumnExclusive = endColumnExclusive;
            }
        }

        readonly List<HoverSpan> _hoverSpans = new List<HoverSpan>();

        internal string HoverUrl { get; private set; }
        internal List<HoverSpan> HoverSpans => _hoverSpans;

        internal void ClearHover()
        {
            HoverUrl = null;
            _hoverSpans.Clear();
        }

        internal void Track(ScreenBuf buf, Vector2Int cell)
        {
            HoverUrl = Find(buf, cell);
        }

        // A row and a stretch of columns rather than a run, a URL drawn in two colors being
        // still one link.
        internal string Find(ScreenBuf buf, Vector2Int cell)
        {
            if (buf == null || buf.Runs == null) return null;
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return null;

            var line = buf.Runs[cell.y];
            int hit = -1;
            for (int i = 0; i < line.Count; i++)
            {
                var run = line[i];
                if (run.Url == null) continue;
                if (cell.x >= run.Col && cell.x < run.Col + run.Columns) { hit = i; break; }
            }
            if (hit < 0) return null;

            // Outwards over everything carrying the same URL.
            string url = line[hit].Url;
            LinkSegment(line, hit, url, out int startColumn, out int endColumnExclusive);
            var spans = new List<HoverSpan> { new HoverSpan(cell.y, startColumn, endColumnExclusive) };
            int width = Mathf.Max(1, buf.Cols);

            int scanRow = cell.y;
            int edge = startColumn;
            while (scanRow > 0 && edge <= 0)
            {
                var previous = buf.Runs[scanRow - 1];
                if (!LinkAtEdge(previous, url, width, false,
                        out int previousStartColumn, out int previousEndColumnExclusive)) break;
                spans.Insert(0, new HoverSpan(scanRow - 1, previousStartColumn, previousEndColumnExclusive));
                scanRow--;
                edge = previousStartColumn;
            }

            scanRow = cell.y;
            edge = endColumnExclusive;
            while (scanRow + 1 < buf.Runs.Length && edge >= width)
            {
                var next = buf.Runs[scanRow + 1];
                if (!LinkAtEdge(next, url, width, true, out int nextStartColumn, out int nextEndColumnExclusive)) break;
                spans.Add(new HoverSpan(scanRow + 1, nextStartColumn, nextEndColumnExclusive));
                scanRow++;
                edge = nextEndColumnExclusive;
            }

            _hoverSpans.AddRange(spans);
            return url;
        }

        static bool LinkAtEdge(List<SgrRun> line, string url, int width, bool start,
            out int startColumn, out int endColumnExclusive)
        {
            startColumn = endColumnExclusive = 0;
            for (int i = 0; i < line.Count; i++)
            {
                if (line[i].Url != url) continue;
                LinkSegment(line, i, url, out startColumn, out endColumnExclusive);
                if (start ? startColumn == 0 : endColumnExclusive >= width) return true;
            }
            return false;
        }

        static void LinkSegment(List<SgrRun> line, int index, string url,
            out int startColumn, out int endColumnExclusive)
        {
            startColumn = line[index].Col;
            endColumnExclusive = startColumn + line[index].Columns;
            int i = index - 1;
            while (i >= 0 && line[i].Url == url && line[i].Col + line[i].Columns == startColumn)
            {
                startColumn = line[i].Col;
                i--;
            }

            i = index + 1;
            while (i < line.Count && line[i].Url == url && endColumnExclusive == line[i].Col)
            {
                endColumnExclusive += line[i].Columns;
                i++;
            }
        }
    }
}
