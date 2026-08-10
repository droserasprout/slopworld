using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Fullscreen view of one agent's pane: renders what slopd captured, forwards
    // keystrokes back as tmux keys.
    public class TerminalWindow : Window
    {
        const float Pad = 6f;

        // Not readonly: the strip switches sessions by pointing the window at a new one,
        // which keeps the terminal's scroll and selection instead of rebuilding it. Null is
        // a window with no pane behind it at all - the options menu opened from the map, and
        // nothing to go back to when it is left.
        string _name;

        // What is in the body instead of the pane, or null for the pane itself. See
        // IContentView: the window is the chrome, and this is what the chrome is showing.
        IContentView _content;
        readonly StringBuilder _literal = new StringBuilder();

        int _cols, _rows;
        float _resizeAt;
        bool _sizeDirty;

        // Mouse-wheel scrollback: lines scrolled up from the live bottom.
        int _scrollOff;
        // Leading-edge throttle: the first wheel event sends immediately, then the rest ride
        // a 50ms beat. `_wantedScrollOff` is the user's desired offset (updated by every wheel
        // event), `_sentScrollOff` is what was last sent to the daemon. Responses are accepted
        // only when they answer the latest request, so a stale reply cannot clamp the offset.
        int _wantedScrollOff;
        int _sentScrollOff;
        float _nextScrollSend;
        bool _scrollPending;
        ulong _scrollRequestId;
        const float ScrollBeat = 0.05f;

        // Drag selection, in cell coordinates of the drawn buffer.
        bool _dragging;
        bool _hasSel;
        Vector2Int _selA, _selB;

        // The cell its press landed on, where the click is closed if the gesture turns
        // out to be a drag the app never asked for.
        bool _mouseFwd;
        Vector2Int _fwdCell;

        // The link the pointer is over, and the stretch of one row it covers. Held for
        // the draw rather than looked up there: the same answer decides the highlight,
        // the tooltip and what a Ctrl+click opens.
        string _hoverUrl;
        int _hoverRow, _hoverC0, _hoverC1;

        // There is nowhere to send them, and swallowing them in silence is how a redeploy
        // reads as a frozen terminal.
        int _droppedKeys;

        public static TerminalWindow Open(string name)
        {
            // The current session follows the pane.
            SessionSelectable.Current = name;

            // Re-opening the same session should focus it, not stack a second copy. Asking
            // for a pane always puts the pane back, though, even the one already behind the
            // content: a portrait clicked while the options menu is up is a request to see
            // that agent.
            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null)
            {
                if (existing._name == name) existing.Leave();
                else existing.SwitchTo(name);
                return existing;
            }
            var w = new TerminalWindow(name);
            Find.WindowStack.Add(w);
            TerminalRecall.Remember(name);
            return w;
        }

        // Opens whatever the chrome is being asked to show. With a pane already up the pane
        // stays behind it - Leave puts it back - and with nothing up the window opens on the
        // content alone, which is the options menu reached from the map.
        public static void OpenContent(IContentView view)
        {
            if (view == null || Find.WindowStack == null) return;

            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null) { existing.SetContent(view); return; }

            var w = new TerminalWindow(null);
            w._content = view;
            Find.WindowStack.Add(w);
            view.Opened();
        }

        // The pane's session, and *only* while the pane is what is on show: with content up
        // there is no current agent, which is what keeps a row from reading as selected under
        // the options menu and what makes clicking that row open it again.
        public static string CurrentName
        {
            get
            {
                var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
                return w == null || w._content != null ? null : w._name;
            }
        }

        // What the chrome is showing, for anything that has to know which it is. Null is the
        // pane, and null window is neither.
        public static IContentView Showing =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._content;

        // The one of a kind already up, so a door that opens a view can hand the same one
        // back rather than build a second: pressing `config` twice is a toggle, not a reset.
        public static T ShowingAs<T>() where T : class, IContentView => Showing as T;

        // Up means leave it; down means show it, and the view is built only in the second
        // case - the factory rather than an instance, so a press that turns out to be a
        // close asks the daemon for nothing. Every door onto a view takes this road, which
        // is what makes each of them a switch.
        public static void ToggleContent<T>(System.Func<T> make) where T : class, IContentView
        {
            if (ShowingAs<T>() != null)
            {
                Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
                return;
            }
            OpenContent(make());
        }

        void SetContent(IContentView view)
        {
            if (_content == view) return;
            _content?.Closed();
            _content = view;
            _content?.Opened();
        }

        // Out of the content and back to what is behind it: the pane it was opened over, or
        // the map when there was none. The chrome exists to show something.
        public void Leave()
        {
            if (_content == null) return;
            SetContent(null);
            if (_name == null) Close();
        }

        // The pane is on the Super layer, so an ordinary dialog opened from inside it would
        // be added underneath and never seen.
        public static void OpenOverPane(Window w)
        {
            if (Find.WindowStack == null) return;
            if (Find.WindowStack.WindowOfType<TerminalWindow>() != null)
                w.layer = WindowLayer.Super;
            Find.WindowStack.Add(w);
        }

        // PaneOverDraw reads this several times a frame, so the closed case costs one static
        // read. Open, it is checked against the stack: a flag left standing wrongly is a map
        // never drawn again.
        static bool _covering;

        public static bool Covering =>
            _covering && Find.WindowStack?.WindowOfType<TerminalWindow>() != null;

        // Resets per-pane view state but keeps the window's place in the stack. Whatever was
        // in the body goes: being pointed at an agent is a request to see it.
        void SwitchTo(string name)
        {
            SetContent(null);
            if (name == _name) return;
            // A window opened on content alone has no pane to let go of, and a subscription
            // named null is one the daemon would have to answer.
            if (_name != null) SessionHub.Instance.Unsubscribe(_name);
            _name = name;
            if (_name != null)
            {
                SessionHub.Instance.Subscribe(_name);
                TerminalRecall.Remember(_name);
                SelectAgent(_name);
            }
            _scrollOff = 0;
            _wantedScrollOff = 0;
            _scrollPending = false;
            ClearSelection();
            // The negotiated size belonged to the session we just left. Kept, it would read
            // as "already the right shape" for a pane still at the daemon's boot size.
            _cols = _rows = 0;
            _sizeDirty = false;
        }

        // Clearing first: the brackets' jump-out is an animation off SelectionDrawer's select
        // time, so a pawn already selected would never replay it.
        static void SelectAgent(string session)
        {
            var pawn = AgentColony.Current?.PawnOf(session);
            if (pawn == null) return;
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn);
        }

        TerminalWindow(string name)
        {
            _name = name;
            doWindowBackground = false;
            doCloseButton = false;
            doCloseX = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
            preventCameraMotion = true;
            draggable = false;
            resizeable = false;
            forcePause = false;
            closeOnAccept = false;
            closeOnCancel = false; // Escape belongs to the agent, not to us
            layer = WindowLayer.Super;
        }

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        // Window.InnerWindowOnGUI opens a GUI group on the contracted rect, translating
        // everything drawn here by the margin without moving GUI.matrix or mousePosition with
        // it, so anything working in screen coordinates lands 18px off.
        protected override float Margin => 0f;

        protected override void SetInitialSizeAndPosition() =>
            windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);

        public override void PreOpen()
        {
            base.PreOpen();
            _covering = true;
            if (_name == null) return;
            SessionHub.Instance.Subscribe(_name);
            SelectAgent(_name);
        }

        public override void PostClose()
        {
            base.PostClose();
            _covering = false;
            Drop(); // a screen's worth of VRAM, held for a window that is gone
            // The window is what the view was being shown in, so it is closed with it.
            SetContent(null);
            if (_name == null) return;
            SessionHub.Instance.Unsubscribe(_name);
            // The terminal is a pager's only home: closing it while a file was being read, or
            // a diff, means the focus has moved away. Both are asked - the window does not
            // know which view opened what, and only one of them can be showing this session.
            FilesView.CloseViewerIf(_name);
            GitView.CloseViewerIf(_name);
        }

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;
            var info = hub.Get(_name);

            Widgets.DrawBoxSolid(rect, Sgr.DefaultBg);

            // An agent that has gone takes its pane with it - but not the window, while the
            // window is showing something else. The chrome closes when there is nothing left
            // in it, which is what Leave says too.
            if (_name != null && (info == null || info.Gone))
            {
                if (_content == null) { Close(); return; }
                hub.Unsubscribe(_name);
                _name = null;
            }
            else if (_name == null && _content == null)
            {
                Close();
                return;
            }

            // Anything stacked over the pane takes the keys and the clicks, or a dialog the
            // strip opened would be typed straight through into the agent.
            bool input = Find.WindowStack == null || Find.WindowStack.GetsInput(this);

            // All of this after the background fill: anywhere earlier in the frame it is
            // painted over. See ColonistBarStrip.cs.
            TopBar.Draw(this, input);
            ColonistBarStrip.Draw(input);
            float top = TopBar.H;
            float left = AgentSidebar.Width;

            var body = new Rect(
                rect.x + left + Pad,
                top + Pad,
                rect.width - left - Pad * 2,
                rect.height - top - Pad * 2);

            // A view in the body is the whole of what the window is for while it is up: the
            // chrome's own keys are still read - F1, F12, Alt+Num, Escape back out of it -
            // but nothing is forwarded to an agent nobody is looking at.
            if (_content != null)
            {
                if (input) ChromeKeys(Event.current);
                _content.Draw(body);
                DrawHint(); // the pane is opaque; a hint drawn from the map is behind it
                return;
            }

            if (input) HandleInput(body);

            var live = hub.Screen(_name);
            // While scrolled, show the history frame; fall back to live until it lands.
            ScreenBuf buf;
            if (_scrollOff > 0)
            {
                var sb = hub.ScrollScreen(_name);
                // Follow the daemon's clamp so we can't run off the top of the history. Only
                // the answer to the latest request is adopted: an older reply still in flight,
                // or left over from before a reconnect, would drag the view backward.
                if (sb != null && sb.ScrollRequestId == _scrollRequestId)
                    _scrollOff = Mathf.Min(_wantedScrollOff, sb.Off);
                buf = sb ?? live;
            }
            else buf = live;

            if (buf == null || buf.Lines.Length == 0)
            {
                DrawCentered(body, hub.Online ? "Waiting for output..." : $"Daemon {hub.Status}");
                DrawHint();
                return;
            }

            NegotiateSize(body, buf);
            DrawScreen(body, buf);
            DrawSelection(body, buf);

            if (_scrollOff > 0)
                DrawScrollHint(body);

            if (!hub.Online) DrawOfflineBanner(body);
            else _droppedKeys = 0;

            DrawHint(); // over the pane: a hint drawn from the map layer is behind it
        }

        // The pane keeps showing its last frame across a daemon restart, which without this
        // is indistinguishable from an agent that has stopped answering.
        void DrawOfflineBanner(Rect body)
        {
            var r = new Rect(body.x, body.y, body.width, SlopWidgets.LineH + 3f);
            Widgets.DrawBoxSolid(r, new Color(0.42f, 0.12f, 0.10f, 0.92f));

            string tail = _droppedKeys > 0
                ? $" - {_droppedKeys} keystroke{(_droppedKeys == 1 ? "" : "s")} not delivered"
                : "";

            Text.Font = GameFont.Small;
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, $"daemon {SessionHub.Instance.Status} - reconnecting{tail}");
            Text.Anchor = anchor;
        }

        // The negotiated shape, for the top bar to say in the layout where this window draws
        // no header of its own. Blank until the first frame has been measured.
        public string Shape => _cols > 0 ? $"{_cols}x{_rows}" : "";

        public static Color StateColor(AgentState s)
        {
            switch (s)
            {
                case AgentState.Working: return new Color(0.45f, 0.75f, 0.95f);
                case AgentState.Waiting: return new Color(0.98f, 0.80f, 0.30f);
                case AgentState.Idle: return new Color(0.60f, 0.62f, 0.64f);
                default: return new Color(0.85f, 0.35f, 0.35f);
            }
        }

        void DrawCentered(Rect r, string msg)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(r, msg);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // The persona core's hint bubble, drawn here rather than on the map layer so it sits
        // over this pane: the window fills the screen opaque and a bubble behind it cannot be
        // seen. Only the current map's core holds a hint; elsewhere there is nothing to draw
        // and this returns at once.
        void DrawHint() => Find.CurrentMap?.GetComponent<CoreTip>()?.DrawHint();

        // The daemon's own limits, so what we ask for is always something it can answer with.
        const int MinCols = 20, MaxCols = 500, MinRows = 5, MaxRows = 200;

        // A loop rather than a statement: a resize is one fire-and-forget message over a
        // socket that may be down, and the daemon answers a size it already holds with a
        // no-op. The frame carries the emulator's dimensions, so that closes the loop.
        void NegotiateSize(Rect body, ScreenBuf buf)
        {
            var style = TerminalFont.Style;
            if (TerminalFont.CellW <= 0.01f) return;

            int cols = Mathf.Clamp(
                Mathf.FloorToInt(body.width / TerminalFont.CellW), MinCols, MaxCols);
            int rows = Mathf.Clamp(
                Mathf.FloorToInt(body.height / TerminalFont.CellH), MinRows, MaxRows);

            if (cols != _cols || rows != _rows)
            {
                _cols = cols;
                _rows = rows;
                // Debounce: dragging the game window otherwise spams SIGWINCH, and Claude Code
                // redraws its whole TUI on every one.
                _resizeAt = Time.realtimeSinceStartup + 0.2f;
                _sizeDirty = true;
                return;
            }

            // A pane not that shape means the last ask did not land. A scrolled frame is
            // history and says nothing about the live pane.
            if (_sizeDirty || buf.Off > 0) return;
            if (buf.Cols == cols && buf.Rows == rows) return;

            _resizeAt = Time.realtimeSinceStartup + 1f;
            _sizeDirty = true;
        }

        void SendPendingScroll()
        {
            if (!_scrollPending) return;
            _scrollPending = false;

            if (_scrollOff <= 0) return;

            _sentScrollOff = _wantedScrollOff;
            _nextScrollSend = Time.realtimeSinceStartup + ScrollBeat;

            ulong id = ++_scrollRequestId;
            SessionHub.Instance.RequestScroll(_name, _sentScrollOff, id);
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            // Both of these are the pane's business with its own session, and a view in the
            // body means there is no pane being measured or scrolled.
            if (_name == null || _content != null) return;

            float now = Time.realtimeSinceStartup;
            if (_scrollPending && now >= _nextScrollSend)
                SendPendingScroll();

            if (!_sizeDirty || Time.realtimeSinceStartup < _resizeAt) return;

            // A socket that is down drops the message, so hold the ask rather than spend it.
            if (!SessionHub.Instance.Online)
            {
                _resizeAt = Time.realtimeSinceStartup + 1f;
                return;
            }

            SessionHub.Instance.Resize(_name, _cols, _rows);
            _sizeDirty = false;
        }

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

                    // Half strength in the text's own colour; the pointer is what makes a
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

        // The pane is drawn only on the frames it moves: a frame arrives about ten times a
        // second and the window draws at the monitor's rate, so five frames in six laid ~130
        // GUI.Labels down again over an identical picture.
        //
        // Screen-sized rather than pane-sized: GUI drawing goes to the active render target
        // in the coordinates it already holds, so a target shaped like the backbuffer needs
        // no projection and no argument with GUIClip. Cleared opaque, because glyph edges are
        // part-transparent and over a clear target would blend twice into a fringe.
        //
        // Throwing takes the cache out for the life of the process rather than per frame.
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

        // The thin black line that ran through coloured diff: a cell is 19 units tall and the
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

        // A row and a stretch of columns rather than a run, a URL drawn in two colours being
        // still one link.
        void TrackHover(Rect body, ScreenBuf buf)
        {
            _hoverUrl = null;
            var e = Event.current;
            if (e == null) return;
            if (Find.WindowStack != null && !Find.WindowStack.GetsInput(this)) return;

            _hoverUrl = LinkAt(body, buf, e.mousePosition,
                out _hoverRow, out _hoverC0, out _hoverC1);
        }

        // Looked up rather than remembered from the last draw: a click is handled ahead of the
        // frame it lands in, so the drawn answer is one pointer position out of date.
        string LinkAt(Rect body, ScreenBuf buf, Vector2 m, out int row, out int c0, out int c1)
        {
            row = c0 = c1 = 0;
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
            c0 = line[hit].Col;
            c1 = line[hit].Col + line[hit].Text.Length;
            for (int i = hit - 1; i >= 0 && line[i].Url == url; i--) c0 = line[i].Col;
            for (int i = hit + 1; i < line.Count && line[i].Url == url; i++)
                c1 = line[i].Col + line[i].Text.Length;

            row = cell.y;
            return url;
        }

        // Runs made sure of first: a pane that arrived but was never drawn has none.
        string LinkUnder(Rect body, Vector2 m)
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0) return null;
            EnsureRuns(buf);
            return LinkAt(body, buf, m, out _, out _, out _);
        }

        void DrawHover(Rect body)
        {
            if (_hoverUrl == null) return;

            float cw = TerminalFont.CellW, ch = TerminalFont.CellH;
            float l = SnapX(body.x + _hoverC0 * cw);
            float r = SnapX(body.x + _hoverC1 * cw);
            float t = SnapY(body.y + _hoverRow * ch);
            float b = SnapY(body.y + (_hoverRow + 1) * ch);
            if (b > body.yMax) return;

            // Over the text: the only place anything can go once the pane has been blitted.
            var col = TerminalTheme.Current.Link;
            var wash = col;
            wash.a = 0.14f;
            Widgets.DrawBoxSolid(new Rect(l, t, r - l, b - t), wash);
            Widgets.DrawBoxSolid(new Rect(l, b - 2f, r - l, 2f), col);

            TooltipHandler.TipRegion(new Rect(l, t, r - l, b - t),
                $"{_hoverUrl}\n\nCtrl+click to open it on the host");
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


        void HandleInput(Rect body)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    HandleWheel(body, e);
                    return;
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.MouseUp:
                    HandleMouse(body, e);
                    return;
                case EventType.KeyDown:
                    HandleKey(e);
                    return;
            }
        }

        // Single gate for the chrome's own keys. Returns true if consumed. Shift+key passes
        // through to the agent, and so does anything none of the bindings claim - F6-F11
        // by default, being on nothing.
        //
        // Read off the KeyBindingDefs rather than off KeyCode.F1..F5 directly: the options
        // menu's Shortcuts page rebinds these, and matching the raw key would let the page
        // report a change it then went on to ignore. The defaults in KeyBindings.xml are
        // the same F-keys, so out of the box this is the switch it replaced.
        public static bool HandleFunctionKey(Event e)
        {
            // Shift+key = pass through to the agent/tui.
            if (e.shift || e.keyCode == KeyCode.None) return false;

            if (Bound(SlopDefOf.SlopCommandPalette, e))
            {
                CommandPalette.Toggle();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarAgents, e))
            {
                AgentSidebar.FocusTerminal();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarFiles, e))
            {
                AgentSidebar.ShowFiles();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarGit, e))
            {
                AgentSidebar.ShowGit();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarShortcuts, e))
            {
                AgentSidebar.ShowShortcuts();
                return true;
            }
            if (Bound(SlopDefOf.SlopQuickTerminal, e))
            {
                // Close if the window is open, open one if not (handles both map and
                // pane contexts via the same check).
                var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
                if (w != null) w.Close();
                else AgentSidebar.FocusTerminal();
                return true;
            }
            return false;
        }

        // Whether this event's key is either of the def's two slots. Asked of the event
        // rather than through KeyBindingDef.KeyDownEvent, because the caller has already
        // taken the event and needs to know whether to Use it.
        static bool Bound(KeyBindingDef def, Event e)
        {
            if (def == null) return false;
            var data = KeyPrefs.KeyPrefsData;
            if (data == null) return false;
            return data.GetBoundKeyCode(def, KeyPrefs.BindingSlot.A) == e.keyCode
                || data.GetBoundKeyCode(def, KeyPrefs.BindingSlot.B) == e.keyCode;
        }

        // The chrome's own keys, read while a view has the body. Everything an agent would
        // have been sent stays unsent - there is no agent on screen to send it to - so this
        // is the short list: the palette, the way out of the view, the way out of the window,
        // and the numbers that point it back at a portrait.
        //
        // A bare Escape is the way out of a view where in a pane it belongs to the agent. It
        // is what closed the options dialog when the options menu was a window, and what
        // every other view here would be closed with.
        void ChromeKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;

            // All F-keys go through one gate: bare = ours, Shift+F = agent.
            if (HandleFunctionKey(e)) { e.Use(); return; }

            if (e.keyCode == KeyCode.Escape)
            {
                Leave();
                e.Use();
                return;
            }

            int slot = TerminalHotkeys.SlotKey(e);
            if (slot >= 0 && e.alt)
            {
                SwitchToSlot(slot);
                e.Use();
                return;
            }

            // Alt+comma/Alt+period: walk the session list while a content view is up.
            // With a content view, bare comma/dot would be eaten by the view; the alt
            // prefix is what keeps them for the chrome.
            if (e.alt && (e.keyCode == KeyCode.Comma || e.keyCode == KeyCode.Period))
            {
                WalkSession(e.keyCode == KeyCode.Period ? 1 : -1);
                e.Use();
            }
        }

        void HandleKey(Event e)
        {
            // Shift+Escape is the way out; a bare Escape must reach the agent.
            if (e.keyCode == KeyCode.Escape && e.shift)
            {
                Close();
                e.Use();
                return;
            }

            // Shift+Enter: send the kitty keyboard protocol sequence for Shift+Enter
            // (\e[13;2u) so apps like Claude Code can distinguish it from plain Enter
            // and insert a newline rather than submitting.
            if (e.keyCode == KeyCode.Return && e.shift)
            {
                JumpToLive();
                Flush();
                SessionHub.Instance.SendKeys(
                    _name, new[] { "\u001b[13;2u" }, true);
                e.Use();
                return;
            }

            // Not in TerminalHotkeys: a window absorbing input makes
            // WindowStack.HandleEventsHighPriority Use every KeyDown, and that runs earlier in
            // UIRoot.UIRootOnGUI than any game component.
            // All F-keys go through one gate: bare = ours, Shift+F = agent.
            if (HandleFunctionKey(e)) { e.Use(); return; }

            // Ahead of the offline check: switching is local and the subscription survives a
            // dead socket, so a pane that will not change during a redeploy reads as hung.
            int slot = TerminalHotkeys.SlotKey(e);
            if (slot >= 0 && e.alt)
            {
                SwitchToSlot(slot);
                e.Use();
                return;
            }

            // Alt+comma/Alt+period: walk the session list while a pane is open. Bare
            // comma/dot belong to the agent; the alt prefix is the chrome's own walk.
            if (e.alt && (e.keyCode == KeyCode.Comma || e.keyCode == KeyCode.Period))
            {
                WalkSession(e.keyCode == KeyCode.Period ? 1 : -1);
                e.Use();
                return;
            }

            // Offline the hub drops sends, so count them for the banner rather than letting
            // the terminal silently eat what was typed.
            if (!SessionHub.Instance.Online)
            {
                if (e.keyCode != KeyCode.None || e.character != '\0')
                {
                    _droppedKeys++;
                    e.Use();
                }
                return;
            }

            // Terminal convention: Ctrl+Shift+C is always copy, and Ctrl+C copies
            // when text is selected (otherwise it passes through as SIGINT).
            if (e.control && e.keyCode == KeyCode.C)
            {
                if (_hasSel)
                {
                    CopySelection();
                    e.Use();
                    return;
                }
                // No selection: Ctrl+Shift+C is a no-op; bare Ctrl+C falls through to SIGINT.
                if (!e.shift)
                {
                    // Fall through to MapKey below.
                }
                else
                {
                    e.Use();
                    return;
                }
            }

            if (e.keyCode != KeyCode.None)
            {
                var keyScreen = SessionHub.Instance.Screen(_name);
                string key = MapKey(e, keyScreen != null && keyScreen.AltScreen);
                if (key != null)
                {
                    JumpToLive();
                    Flush();
                    // Tips ride the Enter that is about to have breadcrumbs pasted in front
                    // of it, and nothing else: `BreadcrumbsPending` is the daemon's answer to
                    // whether this is that Enter.
                    var info = SessionHub.Instance.Get(_name);
                    bool crumbs = key == "Enter" && info != null && info.BreadcrumbsPending;
                    SessionHub.Instance.SendKeys(_name, new[] { key }, false,
                        crumbs ? Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch) : null);
                    e.Use();
                    return;
                }

                if (e.control && e.keyCode == KeyCode.V)
                {
                    JumpToLive();
                    PasteClipboard();
                    e.Use();
                    return;
                }
            }

            // Unity delivers printable input as a second event carrying only the character.
            if (e.character != '\0' && e.character != '\n' &&
                e.character != '\r' && e.character != '\t' && !e.control && !e.alt)
            {
                JumpToLive();
                _literal.Append(e.character);
                e.Use();
                return;
            }

            if (e.keyCode != KeyCode.None)
                e.Use(); // swallow it so RimWorld hotkeys don't fire behind us
        }

        // A slot past the end is a no-op rather than a wrap: the keys are muscle memory for a
        // fixed portrait. A down agent is started, as clicking the portrait does.
        void SwitchToSlot(int slot)
        {
            var order = AgentColony.InBarOrder();
            if (slot >= order.Count) return;

            string name = order[slot];
            // The same agent while a view has the body is still a request to see it: the
            // number points the window at a portrait, and the pane is what a portrait is.
            if (name == _name && _content == null) return;

            var info = SessionHub.Instance.Get(name);
            if (info == null) return;

            // Alt+Num while the pane is open is about an agent: switch the sidebar to the
            // agents view, which releases whatever the view being left was showing.
            AgentSidebar.FocusTerminal();

            if (info.Gone) { SetContent(null); SessionHub.Instance.Start(name); }
            else SwitchTo(name);
        }

        // Walk the session list by dir (-1 or 1). Used from Alt+comma/Alt+period in both
        // ChromeKeys (content view up) and HandleKey (pane open). Sets the current session
        // and switches the pane, or if the target has no process starts it.
        static void WalkSession(int dir)
        {
            var order = AgentSidebar.WalkOrder();
            if (order.Count == 0)
            {
                // Fallback: the hub's alive sessions.
                var fallback = new List<string>();
                foreach (var s in SessionHub.Instance.Sessions)
                    if (s.Alive) fallback.Add(s.Name);
                if (fallback.Count == 0) return;
                order = fallback;
            }

            string current = SessionSelectable.Current;
            int idx = -1;
            if (current != null)
                idx = order.IndexOf(current);

            int next = idx < 0
                ? (dir > 0 ? 0 : order.Count - 1)
                : (idx + dir + order.Count) % order.Count;

            string target = order[next];
            if (target == null) return;

            SessionSelectable.Current = target;
            Find.Selector?.ClearSelection();
            AgentSidebar.FocusTerminal();

            var info = SessionHub.Instance.Get(target);
            if (info == null) return;

            // The same agent while a pane is open is already on screen. A different agent
            // switches the pane.
            var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (w != null && target == w._name) return;

            if (info.Gone) { SessionHub.Instance.Start(target); }
            else Open(target);
        }


        void HandleWheel(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition)) return;

            // Clear the selection on any wheel event, wherever it goes: an app-backed
            // scroll (arrow keys, mouse wheel) would otherwise leave the highlight at the
            // old cell coordinates while the content moves under it.
            ClearSelection();

            var live = SessionHub.Instance.Screen(_name);
            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(e.delta.y)), 1, 5);
            bool up = e.delta.y < 0;

            // Already in scrollback: stay there, whatever the live app is doing.
            // The app mode check below would otherwise hijack the wheel and send it
            // into the live app while the user is reading historical output.
            if (_scrollOff > 0)
            {
                if (up) _scrollOff += step;
                else _scrollOff = Mathf.Max(0, _scrollOff - step);
                QueueScroll(up);
                e.Use();
                return;
            }

            // App wants the mouse: forward wheel reports at the pointer cell. Batched - `step`
            // is one tmux write, not one tmux process per scrolled line.
            if (live != null && live.AppMouse)
            {
                var cell = CellAt(body, e.mousePosition);
                string act = up ? "wheelup" : "wheeldown";
                SessionHub.Instance.SendMouse(_name, act, 0, cell.x, cell.y, step);
                e.Use();
                return;
            }

            // Alt-screen app with no mouse (less, man, git log): the terminal convention is
            // to translate the wheel to arrow keys.
            if (live != null && live.AltScreen)
            {
                var keys = new string[step];
                for (int k = 0; k < step; k++) keys[k] = up ? "Up" : "Down";
                SessionHub.Instance.SendKeys(_name, keys, false);
                e.Use();
                return;
            }

            // Walk our own scrollback view.
            if (up) _scrollOff += step;
            else _scrollOff = Mathf.Max(0, _scrollOff - step);
            QueueScroll(up);
            e.Use();
        }

        // Leading-edge throttle: the first event of a new gesture sends immediately, then
        // subsequent events ride the beat so a swipe does not take the emulator lock for
        // every tick. A new gesture is a direction change or an expired beat - either way,
        // sending at once means a reversal (up then down, say) answers in one round trip
        // instead of waiting out the previous gesture's beat.
        bool _lastWheelUp;

        void QueueScroll(bool up)
        {
            float now = Time.realtimeSinceStartup;
            _wantedScrollOff = _scrollOff;
            _scrollPending = true;
            bool fresh = up != _lastWheelUp || now >= _nextScrollSend;
            _lastWheelUp = up;
            if (fresh)
                SendPendingScroll();
        }

        void HandleMouse(Rect body, Event e)
        {
            // The pane's own in every mode, whatever the app asked for: the menu has to be
            // reachable from inside a full-screen TUI.
            if (e.button == 1)
            {
                if (e.type == EventType.MouseDown && body.Contains(e.mousePosition))
                    OpenMenu(LinkUnder(body, e.mousePosition));
                e.Use();
                return;
            }

            // Ahead of everything else a press does: a URL printed inside a TUI is over
            // something that wants the mouse as often as not.
            if (e.type == EventType.MouseDown && e.button == 0 && e.control)
            {
                string url = LinkUnder(body, e.mousePosition);
                if (url != null)
                {
                    OpenUrl(url);
                    e.Use();
                    return;
                }
            }

            // Shift forces our own selection, like a real terminal.
            var live = SessionHub.Instance.Screen(_name);
            if (live != null && live.AppMouse && !e.shift && HandleMouseForward(body, e))
                return;

            if (e.button != 0) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                {
                    if (!body.Contains(e.mousePosition)) return;
                    var cell = CellAt(body, e.mousePosition);

                    if (e.clickCount >= 3)
                    {
                        TripleClickSelect(cell.y);
                        e.Use();
                        return;
                    }

                    if (e.clickCount == 2)
                    {
                        DoubleClickSelect(cell);
                        e.Use();
                        return;
                    }

                    _selA = _selB = cell;
                    _dragging = true;
                    _hasSel = false;
                    e.Use();
                    return;
                }

                case EventType.MouseDrag:
                    if (!_dragging) return;
                    _selB = CellAt(body, e.mousePosition);
                    _hasSel = _selA != _selB;
                    e.Use();
                    return;

                case EventType.MouseUp:
                    if (!_dragging) return;
                    _dragging = false;
                    _selB = CellAt(body, e.mousePosition);
                    if (_selA != _selB) { _hasSel = true; CopySelection(); }
                    else _hasSel = false;
                    e.Use();
                    return;
            }
        }

        // An app in click-reporting mode (Claude Code is one) said nothing about motion, so a
        // drag across its output was never its to receive - forwarded anyway, it left no way
        // to select text short of holding Shift. The press goes over as a press, and the
        // moment it turns into a drag that click is closed and the rest taken as a selection.
        bool HandleMouseForward(Rect body, Event e)
        {
            int btn = Mathf.Clamp(e.button, 0, 2);
            var cell = CellAt(body, e.mousePosition);
            var live = SessionHub.Instance.Screen(_name);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!body.Contains(e.mousePosition)) return true;
                    JumpToLive();
                    ClearSelection();
                    SessionHub.Instance.SendMouse(_name, "press", btn, cell.x, cell.y);
                    _mouseFwd = true;
                    _fwdCell = cell;
                    e.Use();
                    return true;

                case EventType.MouseDrag:
                    if (!_mouseFwd) return false;
                    if (live != null && live.AppDrag)
                    {
                        SessionHub.Instance.SendMouse(_name, "drag", btn, cell.x, cell.y);
                        e.Use();
                        return true;
                    }
                    // Only the left button selects; anything else is swallowed.
                    SessionHub.Instance.SendMouse(_name, "release", btn, _fwdCell.x, _fwdCell.y);
                    _mouseFwd = false;
                    if (btn != 0) { e.Use(); return true; }
                    _selA = _fwdCell;
                    _dragging = true;
                    return false;

                case EventType.MouseUp:
                    if (!_mouseFwd) return false;
                    SessionHub.Instance.SendMouse(_name, "release", btn, cell.x, cell.y);
                    _mouseFwd = false;
                    e.Use();
                    return true;
            }
            return true;
        }

        void JumpToLive() { _scrollOff = 0; _wantedScrollOff = 0; _scrollPending = false; _nextScrollSend = 0f; }

        void ClearSelection()
        {
            _hasSel = false;
            _dragging = false;
        }

        // Both ends inclusive, the way a dragged selection states them.
        void SelectSpan(int row, int c0, int c1)
        {
            _selA = new Vector2Int(c0, row);
            _selB = new Vector2Int(c1, row);
            _hasSel = true;
            _dragging = false;
            CopySelection();
        }

        // A word, or the run of identical characters a non-word cell sits in.
        void DoubleClickSelect(Vector2Int cell)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return;

            string line = RowText(buf, cell.y);
            int len = ContentLen(line);
            if (cell.x < 0 || cell.x >= len) { ClearSelection(); return; }

            char anchor = line[cell.x];
            bool word = IsWordChar(anchor);
            int c0 = cell.x, c1 = cell.x;
            while (c0 > 0 && SameClass(line[c0 - 1], anchor, word)) c0--;
            while (c1 + 1 < len && SameClass(line[c1 + 1], anchor, word)) c1++;
            SelectSpan(cell.y, c0, c1);
        }

        // The row, not the logical line: the daemon does not mark where one wrapped.
        void TripleClickSelect(int row)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (row < 0 || row >= buf.Runs.Length) return;

            int len = ContentLen(RowText(buf, row));
            if (len == 0) { ClearSelection(); return; }
            SelectSpan(row, 0, len - 1);
        }

        static bool SameClass(char c, char anchor, bool word) =>
            word ? IsWordChar(c) : c == anchor;

        static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_';

        Vector2Int CellAt(Rect body, Vector2 m)
        {
            float cw = TerminalFont.CellW, ch = TerminalFont.CellH;
            if (cw <= 0.01f || ch <= 0.01f) return Vector2Int.zero;
            int col = Mathf.FloorToInt((m.x - body.x) / cw);
            int row = Mathf.FloorToInt((m.y - body.y) / ch);
            return new Vector2Int(col, row);
        }

        ScreenBuf DisplayedBuf()
        {
            var hub = SessionHub.Instance;
            var live = hub.Screen(_name);
            return _scrollOff > 0 ? (hub.ScrollScreen(_name) ?? live) : live;
        }

        void CopySelection()
        {
            var buf = DisplayedBuf();
            if (buf != null) CopyText(SelectionText(buf));
        }

        // Trailing newlines go: a screen is padded to its row count, so an app half a screen
        // tall would copy the blank half with it.
        void SelectAll()
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0) return;

            EnsureRuns(buf);
            _selA = Vector2Int.zero;
            _selB = new Vector2Int(buf.Cols, buf.Runs.Length - 1);
            _hasSel = true;
            _dragging = false;
            CopyText(SelectionText(buf).TrimEnd('\n'));
        }

        // The *host's* clipboard, through the daemon: on this Unity player
        // GUIUtility.systemCopyBuffer is as often the process's own buffer as the desktop's.
        // An agent copying on its own behalf goes via OSC 52 instead, never through here.
        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            GUIUtility.systemCopyBuffer = text;
            SlopClient.Post("/api/clipboard", "{\"text\":" + JVal.Q(text) + "}", null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        // The clipboard errands, which never had a button anywhere. Nothing that ends an agent
        // is here - a menu opened to copy a line is the wrong place to find it.
        void OpenMenu(string url)
        {
            var options = new List<FloatMenuOption>();

            // First when there is one: the pointer is already on it, and the right button is
            // the road for anyone who never learned Ctrl+click.
            if (url != null)
            {
                options.Add(new FloatMenuOption("Open " + url.Truncate(360f), () => OpenUrl(url)));
                options.Add(new FloatMenuOption("Copy link", () => CopyText(url)));
            }

            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !_hasSel;
            options.Add(copy);
            options.Add(new FloatMenuOption("Paste", () => { JumpToLive(); PasteClipboard(); }));
            options.Add(new FloatMenuOption("Select all", SelectAll));

            if (_scrollOff > 0)
                options.Add(new FloatMenuOption("Back to the live view", () =>
                {
                    JumpToLive();
                    ClearSelection();
                }));

            OpenOverPane(new FloatMenu(options));
        }

        // Falls back to the game's own buffer. A round trip, so the paste lands a frame or
        // two later.
        void PasteClipboard()
        {
            string name = _name;
            SlopClient.Get("/api/clipboard",
                j => Deliver(name, j["text"].AsString()),
                _ => Deliver(name, null));
        }

        static void Deliver(string name, string text)
        {
            if (string.IsNullOrEmpty(text)) text = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(text)) SessionHub.Instance.Paste(name, text);
        }

        void OrderedSel(out Vector2Int a, out Vector2Int b)
        {
            a = _selA;
            b = _selB;
            if (b.y < a.y || (b.y == a.y && b.x < a.x)) { var t = a; a = b; b = t; }
        }

        string SelectionText(ScreenBuf buf)
        {
            EnsureRuns(buf);
            OrderedSel(out var a, out var b);
            int rows = buf.Runs.Length;
            if (rows == 0) return "";

            var sb = new StringBuilder();
            int r0 = Mathf.Clamp(a.y, 0, rows - 1);
            int r1 = Mathf.Clamp(b.y, 0, rows - 1);
            for (int row = r0; row <= r1; row++)
            {
                string line = RowText(buf, row);
                int len = ContentLen(line);
                int startCol = row == a.y ? Mathf.Max(0, a.x) : 0;
                // The head cell is inclusive, matching the highlight.
                int endCol = row == b.y ? b.x + 1 : len;
                startCol = Mathf.Clamp(startCol, 0, len);
                endCol = Mathf.Clamp(endCol, 0, len);
                if (endCol > startCol) sb.Append(line.Substring(startCol, endCol - startCol));
                if (row < r1) sb.Append('\n');
            }
            return sb.ToString();
        }

        void DrawSelection(Rect body, ScreenBuf buf)
        {
            // Not `_selA == _selB`: a one-character word is a selection, and drawn.
            if (!_hasSel) return;
            EnsureRuns(buf);
            SyncSnap();

            float cw = TerminalFont.CellW, ch = TerminalFont.CellH;
            OrderedSel(out var a, out var b);
            int rows = buf.Runs.Length;

            for (int row = Mathf.Max(0, a.y); row <= Mathf.Min(rows - 1, b.y); row++)
            {
                int lineLen = ContentLen(RowText(buf, row));
                int startCol = Mathf.Max(0, row == a.y ? a.x : 0);
                int endCol = row == b.y ? b.x + 1 : lineLen;
                endCol = Mathf.Clamp(endCol, startCol, lineLen);

                float y = body.y + row * ch;
                if (y > body.yMax) break;
                if (endCol <= startCol) continue;

                float l = SnapX(body.x + startCol * cw);
                float r = SnapX(body.x + endCol * cw);
                float t = SnapY(y);
                float bot = SnapY(body.y + (row + 1) * ch);
                Widgets.DrawBoxSolid(new Rect(l, t, r - l, bot - t),
                    TerminalTheme.Current.Selection);
            }
        }

        void DrawScrollHint(Rect body)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.98f, 0.80f, 0.30f);
            Widgets.Label(new Rect(body.x, body.y, body.width - 6f, SlopWidgets.TinyH),
                $"scrollback -{_scrollOff}   type or scroll down to resume");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        // Colours are resolved into the runs at parse time, so a scheme change is a re-parse:
        // without it an idle pane keeps the old palette until the agent next writes, which on
        // an idle agent is never.
        static void EnsureRuns(ScreenBuf buf)
        {
            if (buf.Runs != null && buf.RunsRev == TerminalTheme.Rev) return;
            buf.Runs = new List<SgrRun>[buf.Lines.Length];
            for (int i = 0; i < buf.Lines.Length; i++)
                buf.Runs[i] = Sgr.ParseLine(buf.Lines[i]);
            buf.RunsRev = TerminalTheme.Rev;
        }

        // The daemon trims trailing blanks only when they carry nothing, so anything coloured
        // to the right margin arrives padded with spaces - which copied as spaces.
        static int ContentLen(string line)
        {
            int n = line.Length;
            while (n > 0 && line[n - 1] == ' ') n--;
            return n;
        }

        static string RowText(ScreenBuf buf, int row)
        {
            var sb = new StringBuilder();
            foreach (var run in buf.Runs[row]) sb.Append(run.Text);
            return sb.ToString();
        }

        void Flush()
        {
            if (_literal.Length == 0) return;
            SessionHub.Instance.SendKeys(_name, new[] { _literal.ToString() }, true);
            _literal.Length = 0;
        }

        // UI.screenWidth truncates, so the window is a fraction of a pixel short of the right
        // edge and nothing covers or repaints the last column, PaneOverDraw having stood the
        // map down. Here rather than by widening the window, which would be a wider pane.
        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current.type != EventType.Repaint) return;

            Widgets.DrawBoxSolid(new Rect(0f, 0f,
                Mathf.Ceil(Screen.width / Prefs.UIScale),
                Mathf.Ceil(Screen.height / Prefs.UIScale)), Sgr.DefaultBg);

            Flush();
        }

        static string MapKey(Event e, bool altScreen)
        {
            // tmux turns modifier-prefixed names (C-Left, M-Up, S-Right) into the xterm
            // sequences apps read for word-wise motion and selection. Shift goes over only on
            // the alt screen: an editor there asked for the whole screen and groks \e[1;2C,
            // while zsh and bash leave it undefined - zsh rings the bell and inserts the C
            // (see zsh-terminal.md), where a bare arrow at least still moved the cursor.
            string mod = "";
            if (e.control) mod += "C-";
            if (e.alt) mod += "M-";
            if (e.shift && altScreen) mod += "S-";

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.Escape: return "Escape";
                case KeyCode.Backspace: return "BSpace";
                case KeyCode.Tab: return e.shift ? "BTab" : "Tab";
                case KeyCode.UpArrow: return mod + "Up";
                case KeyCode.DownArrow: return mod + "Down";
                case KeyCode.LeftArrow: return mod + "Left";
                case KeyCode.RightArrow: return mod + "Right";
                case KeyCode.Home: return mod + "Home";
                case KeyCode.End: return mod + "End";
                case KeyCode.PageUp: return mod + "PPage";
                case KeyCode.PageDown: return mod + "NPage";
                case KeyCode.Delete: return mod + "DC";
                case KeyCode.Insert: return mod + "IC";
                case KeyCode.F1: return "F1";
                case KeyCode.F2: return "F2";
                case KeyCode.F3: return "F3";
                case KeyCode.F4: return "F4";
                case KeyCode.F5: return "F5";
                case KeyCode.F6: return "F6";
                case KeyCode.F7: return "F7";
                case KeyCode.F8: return "F8";
                case KeyCode.F9: return "F9";
                case KeyCode.F10: return "F10";
                case KeyCode.F11: return "F11";
                case KeyCode.F12: return "F12";
            }

            // Ctrl+V is a paste, handled by the caller, not a key to forward.
            if (e.control && e.keyCode == KeyCode.V) return null;

            if (e.keyCode >= KeyCode.A && e.keyCode <= KeyCode.Z)
            {
                char c = (char)('a' + (e.keyCode - KeyCode.A));
                if (e.control) return "C-" + c;
                if (e.alt) return "M-" + c;
            }

            return null;
        }
    }
}
