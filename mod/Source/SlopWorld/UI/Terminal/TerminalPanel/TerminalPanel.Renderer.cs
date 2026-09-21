using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    sealed partial class TerminalPanel
    {
        // The panel coordinates lifecycle and input. This owner receives a frame, pane
        // geometry, and the current selection when it paints terminal pixels.
        sealed class TerminalRenderer
        {
            readonly TerminalPanel _panel;
            readonly TerminalRunCache _runCache = new TerminalRunCache();

            public TerminalRenderer(TerminalPanel window)
            {
                _panel = window;
            }

            public int RunCount => _runCache.Count;

            public void EnsureRuns(ScreenBuf buf)
            {
                if (buf.Runs != null && buf.RunsRev == TerminalTheme.Rev && buf.RunsComplete)
                    return;
                float debugStarted = _panel.ScrollDebugTimer();
                buf.Runs = _runCache.Parse(buf, TerminalTheme.Rev, TerminalFont.Rev,
                                           out int hits, out int misses);
                buf.RunsRev = TerminalTheme.Rev;
                buf.RunsComplete = true;
                _panel.ScrollDebugParse(debugStarted, hits, misses);
            }

            public void DrawSelection(Rect body, ScreenBuf buf, float shift)
            {
                if (Event.current.type != EventType.Repaint ||
                    !_panel._state.Selection.HasSelection) return;
                EnsureRuns(buf);
                _panel.SyncSnap();

                float cw = _panel.DisplayCellW(), ch = TerminalFont.CellH;
                _panel.OrderedSel(out var a, out var b);
                int rows = buf.Runs.Length;

                for (int row = Mathf.Max(0, a.y); row <= Mathf.Min(rows - 1, b.y); row++)
                {
                    var cells = TerminalColumns.Cells(buf.Runs[row]);
                    int lineLen = TerminalColumns.ContentColumns(cells);
                    int startCol = Mathf.Max(0, row == a.y ? a.x : 0);
                    int endCol = row == b.y ? b.x + 1 : lineLen;
                    endCol = Mathf.Clamp(endCol, startCol, lineLen);
                    TerminalColumns.ExpandWideRange(cells, ref startCol, ref endCol);
                    endCol = Mathf.Clamp(endCol, startCol, lineLen);

                    float y = body.y + shift + row * ch;
                    if (y + ch < body.y) continue;
                    if (y > body.yMax) break;
                    if (endCol <= startCol) continue;

                    float l = _panel.SnapX(body.x + startCol * cw);
                    float r = _panel.SnapX(body.x + endCol * cw);
                    l = Mathf.Max(l, body.x);
                    r = Mathf.Min(r, body.xMax);
                    if (r <= l) continue;
                    float t = _panel.SnapY(y);
                    float bot = _panel.SnapY(body.y + shift + (row + 1) * ch);
                    if (bot <= body.y || t >= body.yMax) continue;
                    t = Mathf.Max(t, body.y);
                    bot = Mathf.Min(bot, body.yMax);
                    Widgets.DrawBoxSolid(new Rect(l, t, r - l, bot - t),
                        TerminalTheme.Current.Selection);
                }
            }

            public void Paint(Rect body, ScreenBuf buf, float cw, float ch, float yShift = 0f) =>
                PaintRows(body, buf, cw, ch, null, yShift);

            public void PaintRows(Rect body, ScreenBuf buf, float cw, float ch, int[] rows,
                                  float yShift = 0f)
            {
                float debugStarted = _panel.ScrollDebugTimer();
                var style = TerminalFont.Style;

                if (rows == null)
                {
                    for (int row = 0; row < buf.Runs.Length; row++) PaintRow(
                        body, buf.Runs[row], row, cw, ch, yShift, style, body.y);
                }
                else
                {
                    foreach (int row in rows)
                    {
                        if (row < 0 || row >= buf.Runs.Length) continue;
                        float y = body.y + yShift + row * ch;
                        if (y + ch < body.y || y > body.yMax) continue;
                        Widgets.DrawBoxSolid(new Rect(body.x, _panel.SnapY(y), body.width,
                                                       _panel.SnapY(y + ch) - _panel.SnapY(y)),
                                             SolidTerminalBackground);
                        PaintRow(body, buf.Runs[row], row, cw, ch, yShift, style, body.y);
                    }
                }

                _panel.ScrollDebugPaint(debugStarted);
            }

            public void DrawScrollLock(Rect body)
            {
                if (Event.current.type != EventType.Repaint) return;
                const float w = 11f;
                const float h = 12f;
                float x = body.xMax - 30f;
                float y = body.y + 5f;
                var ink = new Color(0.55f, 0.55f, 0.55f, 1f);

                Widgets.DrawBoxSolid(new Rect(x + 2f, y, 7f, 1.5f), ink);
                Widgets.DrawBoxSolid(new Rect(x + 1f, y + 1f, 1.5f, 5f), ink);
                Widgets.DrawBoxSolid(new Rect(x + 8.5f, y + 1f, 1.5f, 5f), ink);
                Widgets.DrawBoxSolid(new Rect(x, y + 5f, w, h - 5f), ink);

                var hole = SolidTerminalBackground;
                hole.a = 1f;
                Widgets.DrawBoxSolid(new Rect(x + 4.5f, y + 7f, 2f, 3f), hole);
            }

            public void DrawHistoryBar(Rect body, bool historyInput)
            {
                if (!historyInput || !_panel.HistoryBarAvailable() ||
                    Event.current.type != EventType.Repaint)
                    return;

                _panel.HistoryBarGeometry(body, out var hit, out var track, out var thumb);
                UiScrollbar.Draw(hit, track, thumb, _panel._historyBarDragging);
            }

            void PaintRow(Rect body, List<SgrRun> runs, int row, float cw, float ch,
                          float yShift, GUIStyle style, float clipTop)
            {
                float y = body.y + yShift + row * ch;
                if (y + ch < clipTop || y > body.yMax) return;

                float bgTop = _panel.SnapY(y);
                float bgBot = _panel.SnapY(y + ch);
                foreach (var run in runs)
                {
                    float x = body.x + run.Col * cw;
                    int endCol = run.Col + run.Columns;
                    if (run.HasBg)
                    {
                        float bgL = _panel.SnapX(x);
                        float bgR = _panel.SnapX(body.x + endCol * cw);
                        Widgets.DrawBoxSolid(new Rect(bgL, bgTop, bgR - bgL, bgBot - bgTop), run.Bg);
                    }

                    style.normal.textColor = run.Fg;
                    TerminalPanel.DrawRun(run.Text, x, y, cw, ch, style, run.Columns);
                    if (run.Url != null)
                    {
                        var underline = run.Fg;
                        underline.a *= 0.5f;
                        Widgets.DrawBoxSolid(new Rect(
                            _panel.SnapX(x), bgBot - 1f,
                            _panel.SnapX(body.x + endCol * cw) - _panel.SnapX(x),
                            1f), underline);
                    }
                }
            }
        }
    }
}
