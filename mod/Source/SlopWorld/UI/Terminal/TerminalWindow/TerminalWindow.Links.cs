using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Terminal link hit testing, hover overlays, and host URL actions.
    public partial class TerminalWindow
    {
        readonly TerminalLinkService _links = new TerminalLinkService();

        // ------------------------------------------------------------------- links

        // A row and a stretch of columns rather than a run, a URL drawn in two colors being
        // still one link.
        void TrackHover(Rect body, ScreenBuf buf)
        {
            _links.ClearHover();
            var e = Event.current;
            if (e == null) return;
            if (Find.WindowStack != null && !Find.WindowStack.GetsInput(this)) return;

            if (buf == null || !body.Contains(e.mousePosition)) return;
            _links.Track(buf, CellAt(body, e.mousePosition));
        }

        // Runs made sure of first: a pane that arrived but was never drawn has none.
        internal string LinkUnder(Rect body, Vector2 m)
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0) return null;
            EnsureRuns(buf);
            if (!body.Contains(m)) return null;
            return _links.Find(buf, CellAt(body, m));
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
            if (_links.HoverUrl == null) return;

            float cw = DisplayCellW(), ch = TerminalFont.CellH;
            var col = TerminalTheme.Current.Link;
            var wash = col;
            wash.a = 0.14f;
            foreach (var span in _links.HoverSpans)
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
                    $"{_links.HoverUrl}\n\nCtrl+click to open it on the host");
            }
        }

        internal static void OpenUrl(string url)
        {
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }
    }
}
