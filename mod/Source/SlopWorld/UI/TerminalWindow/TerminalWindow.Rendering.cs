using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TerminalWindow pane rendering, cache, and pointer-overlays.
    public partial class TerminalWindow
    {
        readonly TerminalRunCache _runCache = new TerminalRunCache();

        void DrawScreen(Rect body, ScreenBuf buf, float shift)
        {
            Slab.Fill(body, SolidTerminalBackground);
            EnsureRuns(buf);
            SyncSnap();
            float cw = DisplayCellW();
            float ch = TerminalFont.CellH;

            if (Mathf.Abs(shift) <= 0.01f)
            {
                // The runs go down only on the frames they change; see Blit.
                if (!Blit(body, buf, cw, ch)) Paint(body, buf, cw, ch);
            }
            else if (Event.current.type == EventType.Repaint)
            {
                // History views carry one overscan row assembled from overlapping daemon
                // snapshots, so translating the cached texture never exposes the pane
                // background at either edge. The group clips the extra row at the body.
                bool drawn = false;
                if (!_noCache)
                {
                    try
                    {
                        if (EnsureCache(body, buf, cw, ch))
                        {
                            float extra = shift < -0.01f ? ch : 0f;
                            GUI.BeginGroup(body);
                            try
                            {
                                drawn = DrawCached(
                                    new Rect(0f, shift, body.width, body.height + extra),
                                    CacheSource(body), extra);
                            }
                            finally
                            {
                                GUI.EndGroup();
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        DisableCache(e);
                    }
                }

                if (!drawn)
                {
                    GUI.BeginGroup(body);
                    SyncSnap();
                    var localBody = new Rect(0f, 0f, body.width, body.height);
                    Paint(localBody, buf, cw, ch, shift);
                    GUI.EndGroup();
                }
            }

            // Not cached: the pointer moves over a still pane, and the cursor blinks under one.
            SyncSnap();
            TrackHover(body, buf);
            DrawHover(body, shift);
            GUI.BeginGroup(body);
            try
            {
                SyncSnap();
                DrawCursor(new Rect(0f, 0f, body.width, body.height), buf, cw, ch, shift);
            }
            finally
            {
                GUI.EndGroup();
                SyncSnap();
            }
            ScrollDebugFrame(buf, shift);

            GUI.color = Color.white;
        }

        // The pane keeps showing its last frame across a daemon restart, which without this
        // is indistinguishable from an agent that has stopped answering.
        void DrawOfflineBanner(Rect body)
        {
            var r = new Rect(body.x, body.y, body.width, UiWidgets.LineH + 3f);
            Slab.Box(r, UiWidgets.OfflineBg, UiWidgets.Edge);

            string tail = _droppedKeys > 0
                ? $" - {_droppedKeys} keystroke{(_droppedKeys == 1 ? "" : "s")} not delivered"
                : "";

            Text.Font = GameFont.Small;
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, $"daemon {SessionHub.Instance.Status} - reconnecting{tail}");
            Text.Anchor = anchor;
        }

        void DrawCentered(Rect r, string msg)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = UiWidgets.Dim;
            Widgets.Label(r, msg);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // The persona core's hint bubble, drawn here rather than on the map layer so it sits
        // over this pane: the window fills the screen opaque and a bubble behind it cannot be
        // seen. Only the current map's core holds a hint; elsewhere there is nothing to draw
        // and this returns at once.
        void DrawHint() => Find.CurrentMap?.GetComponent<CoreTip>()?.DrawHint();

        void DrawSelection(Rect body, ScreenBuf buf, float shift)
        {
            if (Event.current.type != EventType.Repaint) return;
            // Not `_selA == _selB`: a one-character word is a selection, and drawn.
            if (!_hasSel) return;
            EnsureRuns(buf);
            SyncSnap();

            float cw = DisplayCellW(), ch = TerminalFont.CellH;
            OrderedSel(out var a, out var b);
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

                float l = SnapX(body.x + startCol * cw);
                float r = SnapX(body.x + endCol * cw);
                float t = SnapY(y);
                float bot = SnapY(body.y + shift + (row + 1) * ch);
                if (bot <= body.y || t >= body.yMax) continue;
                t = Mathf.Max(t, body.y);
                bot = Mathf.Min(bot, body.yMax);
                Widgets.DrawBoxSolid(new Rect(l, t, r - l, bot - t),
                    TerminalTheme.Current.Selection);
            }
        }

        void DrawScrollLock(Rect body)
        {
            if (Event.current.type != EventType.Repaint) return;

            // The history view is a frozen terminal frame. A small vector lock keeps that
            // state visible without covering the first row with a warning-colored sentence.
            const float w = 11f;
            const float h = 12f;
            float x = body.xMax - 30f;
            float y = body.y + 5f;
            var ink = new Color(0.55f, 0.55f, 0.55f, 1f);

            Widgets.DrawBoxSolid(new Rect(x + 2f, y, 7f, 1.5f), ink);
            Widgets.DrawBoxSolid(new Rect(x + 1f, y + 1f, 1.5f, 5f), ink);
            Widgets.DrawBoxSolid(new Rect(x + 8.5f, y + 1f, 1.5f, 5f), ink);
            Widgets.DrawBoxSolid(new Rect(x, y + 5f, w, h - 5f), ink);

            // Keep the keyhole monochrome and cut it from the terminal surface instead of
            // using the scheme's warning color or a potentially colorful terminal palette.
            var hole = SolidTerminalBackground;
            hole.a = 1f;
            Widgets.DrawBoxSolid(new Rect(x + 4.5f, y + 7f, 2f, 3f), hole);
        }

        // A request is not evidence that history exists. Keep the bar and lock hidden until
        // the first snapshot either assembles a view or reports a positive top offset; this
        // prevents an empty terminal from flashing a one-line scrollbar while off=0 is being
        // confirmed.
        bool HistoryBarAvailable() => _scrollOff > 0 &&
            (_historyTopOff > 0 || _historyViewReady);

        void HistoryBarGeometry(Rect body, out Rect hit, out Rect track, out Rect thumb)
        {
            float ch = TerminalFont.CellH;
            int rows = Mathf.Max(1, _rows);
            float off = ch > 0.01f && _historyScrollReady
                ? HistoryOffsetPixels() / ch : _scrollOff;
            float history = _historyTopOff >= 0
                ? Mathf.Max(1f, _historyTopOff)
                : Mathf.Max(off + rows, Mathf.Max(_sentScrollOff, rows));

            const float pad = 5f;
            track = new Rect(body.xMax - 5f, body.y + pad, 3f,
                Mathf.Max(1f, body.height - pad * 2f));
            float thumbH = Mathf.Clamp(
                track.height * rows / (history + rows), 10f, track.height);
            float travel = track.height - thumbH;
            float fromTop = 1f - Mathf.Clamp01(off / history);
            thumb = new Rect(track.x, track.y + travel * fromTop, track.width, thumbH);
            // The visible chip stays narrow, but its hit target is large enough to grab at
            // the pane edge without stealing any terminal column from the negotiated shape.
            hit = new Rect(body.xMax - 16f, track.y, 16f, track.height);
        }

        internal bool HandleHistoryBarInput(Rect body, Event e)
        {
            if (!HistoryInputEnabled(SessionHub.Instance.Screen(_name)) ||
                !HistoryBarAvailable() || !_historyScrollReady) return false;

            HistoryBarGeometry(body, out var hit, out var track, out var thumb);
            EventType type = MouseType(e);
            if (_historyBarDragging)
            {
                if (type == EventType.MouseDrag)
                {
                    DragHistoryBar(e.mousePosition.y, track, thumb.height);
                    e.Use();
                    return true;
                }
                if (type == EventType.MouseUp && e.button == 0)
                {
                    _historyBarDragging = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    return true;
                }
                return false;
            }

            if (type != EventType.MouseDown || e.button != 0 || !hit.Contains(e.mousePosition))
                return false;

            _historyBarDragging = true;
            _historyBarGrab = thumb.Contains(e.mousePosition)
                ? e.mousePosition.y - thumb.y : thumb.height / 2f;
            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive, hit);
            DragHistoryBar(e.mousePosition.y, track, thumb.height);
            e.Use();
            return true;
        }

        void DragHistoryBar(float mouseY, Rect track, float thumbH)
        {
            float span = track.height - thumbH;
            float t = span <= 0f
                ? 0f : Mathf.Clamp01((mouseY - _historyBarGrab - track.y) / span);
            float ch = TerminalFont.CellH;
            if (ch <= 0.01f) return;

            float history = _historyTopOff >= 0
                ? Mathf.Max(1f, _historyTopOff)
                : Mathf.Max(1f, Mathf.Max(_scrollOff, _sentScrollOff));
            float off = (1f - t) * history;
            _historyScroll.JumpTo(new Vector2(0f,
                Mathf.Clamp(_historyMax - off * ch, 0f, _historyMax)));
        }

        // A quiet position cue over the pane rather than another terminal column. The chip is
        // shown whenever the daemon has confirmed history or the user is already reading it.
        void DrawHistoryBar(Rect body, bool historyInput)
        {
            if (!historyInput || !HistoryBarAvailable() || Event.current.type != EventType.Repaint)
                return;

            HistoryBarGeometry(body, out var hit, out var track, out var thumb);
            var rail = new Rect(track.x + 1f, track.y, 1f, track.height);

            Widgets.DrawBoxSolid(rail, UiWidgets.ScrollTrough);
            bool over = hit.Contains(Event.current.mousePosition);
            Widgets.DrawBoxSolid(thumb, _historyBarDragging
                ? UiWidgets.ScrollThumbHeld
                : over ? UiWidgets.ScrollThumbHover : UiWidgets.ScrollThumb);
        }

        // A pure function of the buffer, the rect and the font, which is what makes Blit's
        // cache possible.
        void Paint(Rect body, ScreenBuf buf, float cw, float ch, float yShift = 0f)
        {
            PaintRows(body, buf, cw, ch, null, yShift);
        }

        void PaintRows(Rect body, ScreenBuf buf, float cw, float ch, int[] rows,
                       float yShift = 0f)
        {
            float debugStarted = ScrollDebugTimer();
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
                    Widgets.DrawBoxSolid(new Rect(body.x, SnapY(y), body.width,
                                                   SnapY(y + ch) - SnapY(y)), SolidTerminalBackground);
                    PaintRow(body, buf.Runs[row], row, cw, ch, yShift, style, body.y);
                }
            }

            ScrollDebugPaint(debugStarted);
        }

        static void PaintRow(Rect body, List<SgrRun> runs, int row, float cw, float ch,
                             float yShift, GUIStyle style, float clipTop)
        {
            float y = body.y + yShift + row * ch;
            if (y + ch < clipTop || y > body.yMax) return;

            // Snapped so it meets its neighbours' on a pixel rather than near one; see SnapY.
            float bgTop = SnapY(y);
            float bgBot = SnapY(y + ch);

            foreach (var run in runs)
            {
                // At the run's true column, so wide chars (which the daemon re-anchors
                // with CHA) do not shift the rest of the line.
                float x = body.x + run.Col * cw;

                if (run.HasBg)
                {
                    float bgL = SnapX(x);
                    float bgR = SnapX(body.x + (run.Col + run.Text.Length) * cw);
                    Widgets.DrawBoxSolid(
                        new Rect(bgL, bgTop, bgR - bgL, bgBot - bgTop), run.Bg);
                }

                style.normal.textColor = run.Fg;
                DrawRun(run.Text, x, y, cw, ch, style);

                // Half strength in the text's own color; the pointer is what makes a
                // link loud.
                if (run.Url != null)
                {
                    var u = run.Fg;
                    u.a *= 0.5f;
                    Widgets.DrawBoxSolid(
                        new Rect(SnapX(x), bgBot - 1f,
                                 SnapX(body.x + (run.Col + run.Text.Length) * cw) - SnapX(x), 1f),
                        u);
                }
            }
        }

        // The GUI-to-screen transform, sampled once a draw; see SnapX.
        static float _snapSx = 1f, _snapSy = 1f, _snapOx, _snapOy;

        static float DisplayCellW() => TerminalFont.CellWAtScreenScale(_snapSx);

        // Two points are enough, the transform being a scale and an offset. Sampled rather
        // than read off GUI.matrix, which carries the UI scale but not the offset of the
        // group a window draws inside.
        static void SyncSnap()
        {
            var p0 = GUIUtility.GUIToScreenPoint(Vector2.zero);
            var p1 = GUIUtility.GUIToScreenPoint(Vector2.one);
            _snapSx = Mathf.Abs(p1.x - p0.x) > 0.0001f ? p1.x - p0.x : 1f;
            _snapSy = Mathf.Abs(p1.y - p0.y) > 0.0001f ? p1.y - p0.y : 1f;
            _snapOx = p0.x;
            _snapOy = p0.y;
        }

        // The thin black line that ran through colored diff: a cell is 19 units tall and the
        // UI runs at 1.75, so a row is 33.25 pixels, the shared edge sits on a pixel centre
        // every fourth row, and a pixel split by two quads belongs to neither. It has to be
        // the *screen* grid - a whole unit here is 1.75 pixels there.
        static float SnapX(float v) => (Mathf.Round(v * _snapSx + _snapOx) - _snapOx) / _snapSx;

        static float SnapY(float v) => (Mathf.Round(v * _snapSy + _snapOy) - _snapOy) / _snapSy;

        // Every char the face cannot advance by exactly one cell is placed alone on its own
        // column. Claude Code's prompt chevron is in no mono face here, and drawn inline it
        // took no width and slid the whole input line a cell left.
        static void DrawRun(string text, float x, float y, float cw, float ch, GUIStyle style)
        {
            TerminalFont.Prepare(text, style.fontStyle);
            int start = 0;
            int i = 0;
            while (i < text.Length)
            {
                int emojiLength;
                if (TerminalEmoji.TryDraw(text, i, x, y, cw, ch, out emojiLength))
                {
                    DrawSpan(text, start, i, x, y, cw, ch, style);
                    i += emojiLength;
                    start = i;
                    continue;
                }

                // A surrogate pair is one glyph, and never a one-cell one.
                int len = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                if (len == 1 && TerminalFont.FitsCell(text[i])) { i++; continue; }

                DrawSpan(text, start, i, x, y, cw, ch, style);
                GUI.Label(new Rect(x + i * cw, y, cw * 2f, ch), text.Substring(i, len), style);
                i += len;
                start = i;
            }
            DrawSpan(text, start, text.Length, x, y, cw, ch, style);
        }

        static void DrawSpan(string text, int from, int to,
            float x, float y, float cw, float ch, GUIStyle style)
        {
            if (to <= from) return;
            string seg = from == 0 && to == text.Length ? text : text.Substring(from, to - from);
            GUI.Label(new Rect(x + from * cw, y, seg.Length * cw + cw, ch), seg, style);
        }


        // Blinks on a half-second beat unless the app asked for a steady cursor.
        void DrawCursor(Rect body, ScreenBuf buf, float cw, float ch, float shift)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (buf.Cy < 0 || buf.Cy >= buf.Lines.Length) return;
            // Keyboard activity restarts the visible half of the blink cycle, so typing or
            // moving the cursor never leaves it hidden until the next global beat.
            if (buf.CursorBlink &&
                (int)((Time.realtimeSinceStartup - _cursorBlinkAt) * 2f) % 2 != 0)
                return;

            float x = body.x + buf.Cx * cw;
            float y = body.y + buf.Cy * ch + shift;
            if (x < body.x || x >= body.xMax || y >= body.yMax || y + ch <= body.y)
                return;

            float l = SnapX(x), r = SnapX(x + cw);
            float t = SnapY(y), b = SnapY(y + ch);

            var col = TerminalTheme.CursorColor;
            switch (buf.CursorShape)
            {
                case 1: // underline
                    Widgets.DrawBoxSolid(new Rect(l, b - 2f, r - l, 2f), col);
                    break;
                case 2: // beam
                    Widgets.DrawBoxSolid(new Rect(l, t, 2f, b - t), col);
                    break;
                default: // block
                    // Opaque with the glyph put back over it: a block cursor is a reversed
                    // cell, and a translucent box left the character half-legible.
                    Widgets.DrawBoxSolid(new Rect(l, t, r - l, b - t), col);
                    DrawCursorGlyph(buf, x, y, cw, ch);
                    break;
            }
        }

        void DrawCursorGlyph(ScreenBuf buf, float x, float y, float cw, float ch)
        {
            if (buf.Cy >= buf.Runs.Length) return;

            foreach (var run in buf.Runs[buf.Cy])
            {
                if (buf.Cx < run.Col || buf.Cx >= run.Col + run.Text.Length) continue;
                char c = run.Text[buf.Cx - run.Col];
                if (c == ' ' || c == '\0') return;

                var style = TerminalFont.Style;
                style.normal.textColor = TerminalTheme.Current.CursorText;
                DrawRun(c.ToString(), x, y, cw, ch, style);
                return;
            }
        }


    }
}
