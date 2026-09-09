using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class TerminalWindow
    {
        // The window coordinates lifecycle and input. This owner receives only a frame, pane
        // geometry, and the current selection when it paints terminal pixels.
        sealed class TerminalRenderer
        {
            readonly TerminalWindow _window;
            readonly TerminalRunCache _runCache = new TerminalRunCache();

            public TerminalRenderer(TerminalWindow window)
            {
                _window = window;
            }

            public int RunCount => _runCache.Count;

            public void EnsureRuns(ScreenBuf buf)
            {
                if (buf.Runs != null && buf.RunsRev == TerminalTheme.Rev && buf.RunsComplete)
                    return;
                float debugStarted = _window.ScrollDebugTimer();
                buf.Runs = _runCache.Parse(buf, TerminalTheme.Rev, TerminalFont.Rev,
                                           out int hits, out int misses);
                buf.RunsRev = TerminalTheme.Rev;
                buf.RunsComplete = true;
                _window.ScrollDebugParse(debugStarted, hits, misses);
            }

            public void DrawSelection(Rect body, ScreenBuf buf, float shift)
            {
                if (Event.current.type != EventType.Repaint || !_window._hasSel) return;
                EnsureRuns(buf);
                TerminalWindow.SyncSnap();

                float cw = TerminalWindow.DisplayCellW(), ch = TerminalFont.CellH;
                _window.OrderedSel(out var a, out var b);
                int rows = buf.Runs.Length;

                for (int row = Mathf.Max(0, a.y); row <= Mathf.Min(rows - 1, b.y); row++)
                {
                    int lineLen = TerminalColumns.ContentColumns(TerminalColumns.Cells(buf.Runs[row]));
                    int startCol = Mathf.Max(0, row == a.y ? a.x : 0);
                    int endCol = row == b.y ? b.x + 1 : lineLen;
                    endCol = Mathf.Clamp(endCol, startCol, lineLen);

                    float y = body.y + shift + row * ch;
                    if (y + ch < body.y) continue;
                    if (y > body.yMax) break;
                    if (endCol <= startCol) continue;

                    float l = TerminalWindow.SnapX(body.x + startCol * cw);
                    float r = TerminalWindow.SnapX(body.x + endCol * cw);
                    float t = TerminalWindow.SnapY(y);
                    float bot = TerminalWindow.SnapY(body.y + shift + (row + 1) * ch);
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
                float debugStarted = _window.ScrollDebugTimer();
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
                        Widgets.DrawBoxSolid(new Rect(body.x, TerminalWindow.SnapY(y), body.width,
                                                       TerminalWindow.SnapY(y + ch) - TerminalWindow.SnapY(y)),
                                             SolidTerminalBackground);
                        PaintRow(body, buf.Runs[row], row, cw, ch, yShift, style, body.y);
                    }
                }

                _window.ScrollDebugPaint(debugStarted);
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
                if (!historyInput || !_window.HistoryBarAvailable() ||
                    Event.current.type != EventType.Repaint)
                    return;

                _window.HistoryBarGeometry(body, out var hit, out var track, out var thumb);
                var rail = new Rect(track.x + 1f, track.y, 1f, track.height);
                Widgets.DrawBoxSolid(rail, UiWidgets.ScrollTrough);
                bool over = hit.Contains(Event.current.mousePosition);
                Widgets.DrawBoxSolid(thumb, _window._historyBarDragging
                    ? UiWidgets.ScrollThumbHeld
                    : over ? UiWidgets.ScrollThumbHover : UiWidgets.ScrollThumb);
            }

            void PaintRow(Rect body, List<SgrRun> runs, int row, float cw, float ch,
                          float yShift, GUIStyle style, float clipTop)
            {
                float y = body.y + yShift + row * ch;
                if (y + ch < clipTop || y > body.yMax) return;

                float bgTop = TerminalWindow.SnapY(y);
                float bgBot = TerminalWindow.SnapY(y + ch);
                foreach (var run in runs)
                {
                    float x = body.x + run.Col * cw;
                    if (run.HasBg)
                    {
                        float bgL = TerminalWindow.SnapX(x);
                        float bgR = TerminalWindow.SnapX(body.x + (run.Col + run.Text.Length) * cw);
                        Widgets.DrawBoxSolid(new Rect(bgL, bgTop, bgR - bgL, bgBot - bgTop), run.Bg);
                    }

                    style.normal.textColor = run.Fg;
                    TerminalWindow.DrawRun(run.Text, x, y, cw, ch, style);
                    if (run.Url != null)
                    {
                        var underline = run.Fg;
                        underline.a *= 0.5f;
                        Widgets.DrawBoxSolid(new Rect(
                            TerminalWindow.SnapX(x), bgBot - 1f,
                            TerminalWindow.SnapX(body.x + (run.Col + run.Text.Length) * cw) - TerminalWindow.SnapX(x),
                            1f), underline);
                    }
                }
            }
        }
    }
}
