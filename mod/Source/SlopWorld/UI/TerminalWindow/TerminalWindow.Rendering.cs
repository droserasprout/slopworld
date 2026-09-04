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

        void DrawScreen(Rect body, ScreenBuf buf, float shift)
        {
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
            DrawCursor(body, buf, cw, ch);
            ScrollDebugFrame(buf, shift);

            GUI.color = Color.white;
        }

        // The pane keeps showing its last frame across a daemon restart, which without this
        // is indistinguishable from an agent that has stopped answering.
        void DrawOfflineBanner(Rect body)
        {
            var r = new Rect(body.x, body.y, body.width, SlopWidgets.LineH + 3f);
            Slab.Box(r, SlopWidgets.OfflineBg, SlopWidgets.Edge);

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
            GUI.color = SlopWidgets.Dim;
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

        void DrawScrollHint(Rect body)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = SlopWidgets.Warn;
            SlopWidgets.RowLabel(new Rect(body.x, body.y, body.width - 6f, SlopWidgets.TinyH),
                $"scrollback -{_scrollOff}   type or scroll down to resume",
                TextAnchor.MiddleRight);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        // A quiet position cue rather than another terminal control. The wheel/touchpad owns
        // history movement; this stays narrow enough to sit over the last cell without taking
        // a column from the negotiated terminal shape.
        void DrawHistoryBar(Rect body)
        {
            if (_scrollOff <= 0 || Event.current.type != EventType.Repaint) return;

            float ch = TerminalFont.CellH;
            int rows = Mathf.Max(1, _rows);
            float off = ch > 0.01f ? HistoryOffsetPixels() / ch : _scrollOff;
            float history = _historyTopOff >= 0
                ? Mathf.Max(1f, _historyTopOff)
                : Mathf.Max(off + rows, Mathf.Max(_sentScrollOff, rows));

            const float pad = 5f;
            var track = new Rect(body.xMax - 5f, body.y + pad, 3f,
                Mathf.Max(1f, body.height - pad * 2f));
            float thumbH = Mathf.Clamp(
                track.height * rows / (history + rows), 10f, track.height);
            float travel = track.height - thumbH;
            float fromTop = 1f - Mathf.Clamp01(off / history);
            var rail = new Rect(track.x + 1f, track.y, 1f, track.height);
            var thumb = new Rect(track.x, track.y + travel * fromTop, track.width, thumbH);

            Widgets.DrawBoxSolid(rail, SlopWidgets.ScrollTrough);
            Widgets.DrawBoxSolid(thumb, SlopWidgets.ScrollThumb);
        }

        // A pure function of the buffer, the rect and the font, which is what makes Blit's
        // cache possible.
        void Paint(Rect body, ScreenBuf buf, float cw, float ch, float yShift = 0f)
        {
            float debugStarted = ScrollDebugTimer();
            var style = TerminalFont.Style;

            for (int row = 0; row < buf.Runs.Length; row++)
            {
                float y = body.y + yShift + row * ch;
                if (y + ch < body.y) continue;
                if (y > body.yMax) break;

                // Snapped so it meets its neighbours' on a pixel rather than near one; see SnapY.
                float bgTop = SnapY(y);
                float bgBot = SnapY(y + ch);

                foreach (var run in buf.Runs[row])
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
            ScrollDebugPaint(debugStarted);
        }

        // ------------------------------------------------------------- pane cache

        // What the pane looked like last time it changed.
        RenderTexture _cache;
        string _cacheName;
        int _cacheSeq = -1, _cacheOff = -1, _cacheRev = -1, _cacheFontRev = -1;
        Rect _cacheBody;
        float _cacheCw, _cacheCh, _cacheLead;
        bool _noCache;

        // Cache the pane between screen frames: the daemon updates more slowly than the monitor.
        // Use an opaque screen-sized target so GUI coordinates and glyph edges remain stable;
        // disable the cache for the process after a render-target failure.
        bool Blit(Rect body, ScreenBuf buf, float cw, float ch)
        {
            if (_noCache) return false;
            // Labels and boxes draw on repaint and nowhere else.
            if (Event.current.type != EventType.Repaint) return true;

            try
            {
                if (!EnsureCache(body, buf, cw, ch)) return false;
                return BlitCached(body);
            }
            catch (System.Exception e)
            {
                DisableCache(e);
                return false;
            }
        }

        bool EnsureCache(Rect body, ScreenBuf buf, float cw, float ch)
        {
            int pw = Screen.width, ph = Screen.height;
            if (pw <= 0 || ph <= 0) return false;

            if (_cache != null && (_cache.width != pw || _cache.height != ph)) Drop();

            bool fresh = _cache == null;
            if (fresh)
                _cache = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32)
                {
                    name = "SlopWorldPane",
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.DontUnloadUnusedAsset, // see TerminalFont
                };

            if (!_cache.IsCreated()) { _cache.Create(); fresh = true; }

            if (fresh
                || _cacheName != _name
                || _cacheSeq != buf.Seq || _cacheOff != buf.Off
                || _cacheBody != body
                || _cacheCw != cw || _cacheCh != ch
                || _cacheLead != CacheLead(body, ch)
                || _cacheRev != TerminalTheme.Rev
                || _cacheFontRev != TerminalFont.Rev)
            {
                // Keep the pre-paint revision. RequestCharactersInTexture can rebuild the
                // atlas while Paint is running; retaining the old revision forces one clean
                // repaint after that rebuild instead of caching a half-drawn first frame.
                int fontRev = TerminalFont.Rev;
                var was = RenderTexture.active;
                try
                {
                    RenderTexture.active = _cache;
                    GL.Clear(false, true, SolidTerminalBackground);
                    _cacheLead = CacheLead(body, ch);
                    Paint(new Rect(body.x, body.y - _cacheLead, body.width,
                                   body.height + _cacheLead), buf, cw, ch);
                }
                finally
                {
                    RenderTexture.active = was;
                }

                _cacheName = _name;
                _cacheSeq = buf.Seq;
                _cacheOff = buf.Off;
                _cacheRev = TerminalTheme.Rev;
                _cacheFontRev = fontRev;
                _cacheBody = body;
                _cacheCw = cw;
                _cacheCh = ch;
            }

            return true;
        }

        void DisableCache(System.Exception e)
        {
            _noCache = true;
            Drop();
            Log.Warning($"[SlopWorld] pane cache off, drawing straight to the screen: {e}");
        }

        // Draw the last complete pane frame. During a session handoff the new reader can exist
        // before its first screen arrives; keeping this frame avoids exposing that transport gap
        // as a close-and-reopen of the terminal.
        bool BlitCached(Rect body)
        {
            SyncSnap();
            return DrawCached(body, CacheSource(body), 0f);
        }

        bool DrawCached(Rect destination, Rect source, float sourceExtraBottom)
        {
            if (_cache == null || !_cache.IsCreated()) return false;
            // The shared texture is only a valid fallback while this session remains active.
            // A switched tab must use its own displayed-frame snapshot or wait for its first
            // screen; showing the previous tab for one frame reads as terminal flicker.
            if (_cacheName != _name) return false;
            if (Event.current.type != EventType.Repaint) return true;

            float debugStarted = ScrollDebugTimer();
            bool drawn = false;

            int pw = Screen.width, ph = Screen.height;
            if (pw <= 0 || ph <= 0 || _cache.width != pw || _cache.height != ph)
            {
                Drop();
                return false;
            }

            float x0 = source.x * _snapSx + _snapOx;
            float y0 = source.y * _snapSy + _snapOy;
            float w = source.width * _snapSx;
            float h = (source.height + sourceExtraBottom) * _snapSy;
            // A screen-sized cache has no texels outside the screen. Sampling beyond its
            // edge makes the GPU repeat or clamp its last scanline, which turns the bottom
            // terminal row into barcode-like vertical streaks during fractional scrolling.
            if (x0 < 0f || y0 < 0f || x0 + w > pw || y0 + h > ph)
                return false;
            float u0 = x0 / pw, u1 = (x0 + w) / pw;

            // texCoords y counts from the destination's bottom either way; which end of
            // the texture that is, is where the API put the target's first row.
            float v0, v1;
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                v0 = (y0 + h) / ph;
                v1 = y0 / ph;
            }
            else
            {
                v0 = 1f - (y0 + h) / ph;
                v1 = 1f - y0 / ph;
            }

            var tint = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(
                destination, _cache, new Rect(u0, v0, u1 - u0, v1 - v0));
            GUI.color = tint;
            drawn = true;
            ScrollDebugBlit(debugStarted, drawn);
            return true;
        }

        void Drop()
        {
            if (_cache == null) return;
            _cache.Release();
            Object.Destroy(_cache);
            _cache = null;
            _cacheSeq = _cacheOff = _cacheRev = _cacheFontRev = -1;
            _cacheName = null;
            _cacheLead = 0f;
        }

        // Fractional history needs one row below the normal viewport. Put the baked rows one
        // cell above the pane so that this overscan row remains inside the screen-sized cache.
        // The source rect is translated back when it is drawn into the pane.
        static float CacheLead(Rect body, float ch)
        {
            if (ch <= 0.01f) return 0f;
            float top = body.y + _snapOy / _snapSy;
            return Mathf.Clamp(Mathf.Min(ch, top), 0f, ch);
        }

        Rect CacheSource(Rect body) =>
            new Rect(body.x, body.y - _cacheLead, body.width, body.height);

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

        // Blinks on a half-second beat unless the app asked for a steady cursor.
        void DrawCursor(Rect body, ScreenBuf buf, float cw, float ch)
        {
            // No cursor on a historical frame: the daemon hides it, but the frame still
            // carries cursor coordinates from the render snapshot.
            if (buf.Off > 0) return;
            if (buf.Cy >= buf.Rows) return;
            // Keyboard activity restarts the visible half of the blink cycle, so typing or
            // moving the cursor never leaves it hidden until the next global beat.
            if (buf.CursorBlink &&
                (int)((Time.realtimeSinceStartup - _cursorBlinkAt) * 2f) % 2 != 0)
                return;

            float x = body.x + buf.Cx * cw;
            float y = body.y + buf.Cy * ch;
            if (!body.Contains(new Vector2(x, y))) return;

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
