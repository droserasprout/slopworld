using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Terminal link hit testing, hover overlays, and host URL actions.
    public partial class TerminalWindow
    {
        // The link under the pointer and its row spans are retained for the draw. The same
        // answer drives the highlight, tooltip, and Ctrl+click target.
        string _hoverUrl;
        struct HoverSpan
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

        // ------------------------------------------------------------------- links

        // A row and a stretch of columns rather than a run, a URL drawn in two colors being
        // still one link.
        void TrackHover(Rect body, ScreenBuf buf)
        {
            _hoverUrl = null;
            _hoverSpans.Clear();
            var e = Event.current;
            if (e == null) return;
            if (Find.WindowStack != null && !Find.WindowStack.GetsInput(this)) return;

            _hoverUrl = LinkAt(body, buf, e.mousePosition);
        }

        // Looked up rather than remembered from the last draw: a click is handled ahead of the
        // frame it lands in, so the drawn answer is one pointer position out of date.
        string LinkAt(Rect body, ScreenBuf buf, Vector2 m)
        {
            if (buf == null || buf.Runs == null || !body.Contains(m)) return null;

            var cell = CellAt(body, m);
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

        // Runs made sure of first: a pane that arrived but was never drawn has none.
        internal string LinkUnder(Rect body, Vector2 m)
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0) return null;
            EnsureRuns(buf);
            return LinkAt(body, buf, m);
        }

        internal string PathUnder(Rect body, Vector2 m, out int line)
        {
            line = 0;
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines == null || !body.Contains(m)) return null;
            EnsureRuns(buf);
            var cell = CellAt(body, m);
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return null;
            // A column-indexed line so cell.x (a screen column) points at the right char.
            return PathScan.At(
                TerminalColumns.Line(TerminalColumns.Cells(buf.Runs[cell.y])), cell.x, out line);
        }

        void DrawHover(Rect body, float shift)
        {
            if (_hoverUrl == null) return;

            float cw = DisplayCellW(), ch = TerminalFont.CellH;
            var col = TerminalTheme.Current.Link;
            var wash = col;
            wash.a = 0.14f;
            foreach (var span in _hoverSpans)
            {
                float l = SnapX(body.x + span.C0 * cw);
                float r = SnapX(body.x + span.C1 * cw);
                float t = SnapY(body.y + shift + span.Row * ch);
                float b = SnapY(body.y + shift + (span.Row + 1) * ch);
                if (b <= body.y || t >= body.yMax) continue;
                t = Mathf.Max(t, body.y);
                b = Mathf.Min(b, body.yMax);

                // Over the text: the only place anything can go once the pane has been blitted.
                Widgets.DrawBoxSolid(new Rect(l, t, r - l, b - t), wash);
                Widgets.DrawBoxSolid(new Rect(l, b - 2f, r - l, 2f), col);
                TooltipHandler.TipRegion(new Rect(l, t, r - l, b - t),
                    $"{_hoverUrl}\n\nCtrl+click to open it on the host");
            }
        }

        internal static void OpenUrl(string url)
        {
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }
    }
}
