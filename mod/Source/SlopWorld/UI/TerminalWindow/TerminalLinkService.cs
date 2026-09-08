using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Link hit testing owns the row-spanning link state used by hover painting and clicks.
    // TerminalWindow still owns pane geometry and input policy; this class only understands
    // parsed terminal runs.
    internal sealed class TerminalLinkService
    {
        internal struct HoverSpan
        {
            public int Row, C0, C1;

            public HoverSpan(int row, int c0, int c1)
            {
                Row = row;
                C0 = c0;
                C1 = c1;
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
                if (cell.x >= run.Col && cell.x < run.Col + run.Text.Length) { hit = i; break; }
            }
            if (hit < 0) return null;

            // Outwards over everything carrying the same URL.
            string url = line[hit].Url;
            LinkSegment(line, hit, url, out int c0, out int c1);
            var spans = new List<HoverSpan> { new HoverSpan(cell.y, c0, c1) };
            int width = Mathf.Max(1, buf.Cols);

            int scanRow = cell.y;
            int edge = c0;
            while (scanRow > 0 && edge <= 0)
            {
                var previous = buf.Runs[scanRow - 1];
                if (!LinkAtEdge(previous, url, width, false,
                        out int previousC0, out int previousC1)) break;
                spans.Insert(0, new HoverSpan(scanRow - 1, previousC0, previousC1));
                scanRow--;
                edge = previousC0;
            }

            scanRow = cell.y;
            edge = c1;
            while (scanRow + 1 < buf.Runs.Length && edge >= width)
            {
                var next = buf.Runs[scanRow + 1];
                if (!LinkAtEdge(next, url, width, true, out int nextC0, out int nextC1)) break;
                spans.Add(new HoverSpan(scanRow + 1, nextC0, nextC1));
                scanRow++;
                edge = nextC1;
            }

            _hoverSpans.AddRange(spans);
            return url;
        }

        static bool LinkAtEdge(List<SgrRun> line, string url, int width, bool start,
            out int c0, out int c1)
        {
            c0 = c1 = 0;
            for (int i = 0; i < line.Count; i++)
            {
                if (line[i].Url != url) continue;
                LinkSegment(line, i, url, out c0, out c1);
                if (start ? c0 == 0 : c1 >= width) return true;
            }
            return false;
        }

        static void LinkSegment(List<SgrRun> line, int index, string url,
            out int start, out int end)
        {
            start = line[index].Col;
            end = start + line[index].Text.Length;
            int i = index - 1;
            while (i >= 0 && line[i].Url == url && line[i].Col + line[i].Text.Length == start)
            {
                start = line[i].Col;
                i--;
            }

            i = index + 1;
            while (i < line.Count && line[i].Url == url && end == line[i].Col)
            {
                end += line[i].Text.Length;
                i++;
            }
        }
    }
}
