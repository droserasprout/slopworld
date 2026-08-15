using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Mechanical split: TerminalWindow.Rendering methods.
    public partial class TerminalWindow
    {
        void DrawScreen(Rect body, ScreenBuf buf)
        {
            float cw = TerminalFont.CellW;
            float ch = TerminalFont.CellH;

            EnsureRuns(buf);
            SyncSnap();

            // The runs go down only on the frames they change; see Blit.
            if (!Blit(body, buf, cw, ch)) Paint(body, buf, cw, ch);

            // Not cached: the pointer moves over a still pane, and the cursor blinks under one.
            TrackHover(body, buf);
            DrawHover(body);
            DrawCursor(body, buf, cw, ch);

            GUI.color = Color.white;
        }

        // A pure function of the buffer, the rect and the font, which is what makes Blit's
        // cache possible.
        void Paint(Rect body, ScreenBuf buf, float cw, float ch)
        {
            var style = TerminalFont.Style;

            for (int row = 0; row < buf.Runs.Length; row++)
            {
                float y = body.y + row * ch;
                if (y > body.yMax) break;

                // Snapped so it meets its neighbours' on a pixel rather than near one; see SnapY.
                float bgTop = SnapY(y);
                float bgBot = SnapY(body.y + (row + 1) * ch);

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
        }

        // ------------------------------------------------------------- pane cache

        // What the pane looked like last time it changed.
        RenderTexture _cache;
        string _cacheName;
        int _cacheSeq = -1, _cacheOff = -1, _cacheRev = -1;
        Rect _cacheBody;
        float _cacheCw, _cacheCh;
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
                    || _cacheRev != TerminalTheme.Rev)
                {
                    var was = RenderTexture.active;
                    RenderTexture.active = _cache;
                    GL.Clear(false, true, Sgr.DefaultBg);
                    Paint(body, buf, cw, ch);
                    RenderTexture.active = was;

                    _cacheName = _name;
                    _cacheSeq = buf.Seq;
                    _cacheOff = buf.Off;
                    _cacheRev = TerminalTheme.Rev;
                    _cacheBody = body;
                    _cacheCw = cw;
                    _cacheCh = ch;
                }

                // Where the pane sits in the texture is where it sits on the screen - SnapX's
                // sum, run forwards.
                float x0 = body.x * _snapSx + _snapOx;
                float y0 = body.y * _snapSy + _snapOy;
                float w = body.width * _snapSx;
                float h = body.height * _snapSy;

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
                    body, _cache, new Rect(u0, v0, u1 - u0, v1 - v0));
                GUI.color = tint;
                return true;
            }
            catch (System.Exception e)
            {
                _noCache = true;
                Drop();
                Log.Warning($"[SlopWorld] pane cache off, drawing straight to the screen: {e}");
                return false;
            }
        }

        void Drop()
        {
            if (_cache == null) return;
            _cache.Release();
            Object.Destroy(_cache);
            _cache = null;
            _cacheSeq = _cacheOff = _cacheRev = -1;
            _cacheName = null;
        }

        // The GUI-to-screen transform, sampled once a draw; see SnapX.
        static float _snapSx = 1f, _snapSy = 1f, _snapOx, _snapOy;

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
            int start = 0;
            int i = 0;
            while (i < text.Length)
            {
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
        string LinkUnder(Rect body, Vector2 m)
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0) return null;
            EnsureRuns(buf);
            return LinkAt(body, buf, m);
        }

        void DrawHover(Rect body)
        {
            if (_hoverUrl == null) return;

            float cw = TerminalFont.CellW, ch = TerminalFont.CellH;
            var col = TerminalTheme.Current.Link;
            var wash = col;
            wash.a = 0.14f;
            foreach (var span in _hoverSpans)
            {
                float l = SnapX(body.x + span.C0 * cw);
                float r = SnapX(body.x + span.C1 * cw);
                float t = SnapY(body.y + span.Row * ch);
                float b = SnapY(body.y + (span.Row + 1) * ch);
                if (b > body.yMax) continue;

                // Over the text: the only place anything can go once the pane has been blitted.
                Widgets.DrawBoxSolid(new Rect(l, t, r - l, b - t), wash);
                Widgets.DrawBoxSolid(new Rect(l, b - 2f, r - l, 2f), col);
                TooltipHandler.TipRegion(new Rect(l, t, r - l, b - t),
                    $"{_hoverUrl}\n\nCtrl+click to open it on the host");
            }
        }

        // Through the daemon: the game is a Unity player under Wine as often as not, and
        // slopd is the half on the host with a desktop to hand the URL to.
        static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            SlopClient.Post("/api/open", "{\"url\":" + JVal.Q(url) + "}", null,
                msg =>
                {
                    Log.Warning($"[SlopWorld] open {url}: {msg}");
                    Application.OpenURL(url);
                });
        }

        // Blinks on a half-second beat unless the app asked for a steady cursor.
        void DrawCursor(Rect body, ScreenBuf buf, float cw, float ch)
        {
            // No cursor on a historical frame: the daemon hides it, but the frame still
            // carries cursor coordinates from the render snapshot.
            if (buf.Off > 0) return;
            if (buf.Cy >= buf.Rows) return;
            if (buf.CursorBlink && (int)(Time.realtimeSinceStartup * 2f) % 2 != 0)
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

