using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Fullscreen view of one agent's pane. Renders the screen slopd captured and
    /// forwards keystrokes back as tmux keys.
    /// </summary>
    public class TerminalWindow : Window
    {
        /// What the title bar is when it is only a title bar - one row of buttons.
        const float HeaderMinH = 28f;

        /// <summary>The title bar's height. The colonist strip draws inside it, so
        /// it grows to hold a whole row - portrait and name - and never shrinks below
        /// the row of buttons it also has to hold.</summary>
        static float HeaderH => Mathf.Max(HeaderMinH, ColonistBarOverlay.BarH);
        const float Pad = 6f;

        static readonly Color SelColor = new Color(0.30f, 0.50f, 0.90f, 0.35f);

        // Not readonly: the strip switches sessions by pointing the window at a
        // new one, which keeps the terminal's scroll and selection state instead
        // of throwing the whole window away.
        string _name;
        readonly StringBuilder _literal = new StringBuilder();

        int _cols, _rows;
        float _resizeAt;
        bool _sizeDirty;

        // Mouse-wheel scrollback: lines scrolled up from the live bottom.
        int _scrollOff;

        // Drag selection, in cell (col,row) coordinates of the drawn buffer.
        bool _dragging;
        bool _hasSel;
        Vector2Int _selA, _selB;

        // A mouse gesture currently being forwarded to an app that wants the mouse,
        // and the cell its press landed on - where the click is closed if the
        // gesture turns out to be a drag the app never asked for.
        bool _mouseFwd;
        Vector2Int _fwdCell;

        // Keys typed while the socket was down. There is nowhere to send them, and
        // swallowing them in silence is how a redeploy reads as a frozen terminal.
        int _droppedKeys;

        public static TerminalWindow Open(string name)
        {
            // Re-opening the same session should focus it, not stack a second copy.
            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null)
            {
                if (existing._name == name) return existing;
                existing.SwitchTo(name);
                return existing;
            }
            var w = new TerminalWindow(name);
            Find.WindowStack.Add(w);
            TerminalRecall.Remember(name);
            return w;
        }

        /// <summary>The session this window is showing.</summary>
        public static string CurrentName =>
            Find.WindowStack.WindowOfType<TerminalWindow>()?._name;

        /// <summary>Points the window at another session, resetting per-pane view
        /// state but keeping the window (and its place in the stack) where it is.</summary>
        void SwitchTo(string name)
        {
            if (name == _name) return;
            SessionHub.Instance.Unsubscribe(_name);
            _name = name;
            SessionHub.Instance.Subscribe(_name);
            TerminalRecall.Remember(_name);
            SelectAgent(_name);
            _scrollOff = 0;
            ClearSelection();
            // The negotiated size belonged to the session we just left. Kept, it
            // would read as "this pane is already the right shape" for one that has
            // never been looked at - and a session nobody has opened is still at the
            // daemon's boot size, so the switch would land a 120x34 pane in a window
            // several times that and nothing would ever say otherwise.
            _cols = _rows = 0;
            _sizeDirty = false;
        }

        /// <summary>Selects the shown agent's pawn, which is what puts the colonist
        /// bar's white corner brackets on the portrait you are typing at. Clearing
        /// first is not just tidiness: the brackets' jump-out is an animation off
        /// SelectionDrawer's select time, so a pawn that is already selected would
        /// keep the brackets sitting where they settled and never replay it. This
        /// is the same clear-then-select vanilla does for a bar click - which never
        /// reaches the map while a fullscreen window is absorbing input.</summary>
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

        /// <summary>No margin, for two reasons. The pane is meant to fill the
        /// screen, and vanilla's 18 leaves a transparent border around it. And the
        /// margin is not padding: Window.InnerWindowOnGUI opens a GUI group on the
        /// contracted rect, which translates everything drawn here by (18,18)
        /// without moving GUI.matrix or Event.current.mousePosition into the same
        /// frame. Anything that works in screen coordinates then lands 18px off -
        /// the colonist strip drew low, its hit tests missed, and its selection
        /// brackets (drawn rotated, so pivoted through GUI.matrix) came apart. At
        /// zero the group is the screen and the two agree again.</summary>
        protected override float Margin => 0f;

        protected override void SetInitialSizeAndPosition() =>
            windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);

        public override void PreOpen()
        {
            base.PreOpen();
            SessionHub.Instance.Subscribe(_name);
            SelectAgent(_name);
        }

        public override void PostClose()
        {
            base.PostClose();
            SessionHub.Instance.Unsubscribe(_name);
        }

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;
            var info = hub.Get(_name);

            Widgets.DrawBoxSolid(rect, Sgr.DefaultBg);

            // A session that stopped or was deleted has no pane to look at; its
            // colonist is on the floor and so is its terminal. The strip stays
            // up one final frame so the close never races a click.
            if (info == null || info.Gone)
            {
                Close();
                return;
            }

            var header = new Rect(rect.x, rect.y, rect.width, HeaderH);
            DrawHeader(header, info);

            // The colonist bar draws inside that header while this window is up, and
            // a click on it switches the session. It has to be drawn from here, after
            // the background fill and the header - drawn anywhere earlier in the
            // frame it is painted over. See ColonistBarAboveTerminal.cs.
            var bar = ColonistBarOverlay.Rect;
            ColonistBarOverlay.Draw();

            // The strip is the header, or is inside it; the pane starts under both.
            float top = Mathf.Max(header.yMax, bar.yMax);
            var body = new Rect(
                rect.x + Pad,
                top + Pad,
                rect.width - Pad * 2,
                rect.height - top - Pad * 2);

            HandleInput(body);

            var live = hub.Screen(_name);
            // While scrolled, show the history frame; fall back to live until it lands.
            ScreenBuf buf;
            if (_scrollOff > 0)
            {
                var sb = hub.ScrollScreen(_name);
                // The daemon clamps to real scrollback; follow it so we can't run off the top.
                if (sb != null && sb.Off > 0) _scrollOff = Mathf.Min(_scrollOff, sb.Off);
                buf = sb ?? live;
            }
            else buf = live;

            if (buf == null || buf.Lines.Length == 0)
            {
                DrawCentered(body, hub.Online ? "Waiting for output..." : $"Daemon {hub.Status}");
                return;
            }

            NegotiateSize(body, buf);
            DrawScreen(body, buf);
            DrawSelection(body, buf);

            if (_scrollOff > 0)
                DrawScrollHint(body);

            if (!hub.Online) DrawOfflineBanner(body);
            else _droppedKeys = 0;
        }

        /// <summary>Says the pane is a still photograph, not a live terminal. The
        /// daemon restarting under a working agent is routine here - it is what
        /// `make install-daemon` does - and the pane keeps showing the last frame
        /// throughout, which without this is indistinguishable from an agent that
        /// has stopped answering.</summary>
        void DrawOfflineBanner(Rect body)
        {
            var r = new Rect(body.x, body.y, body.width, 24f);
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

        void DrawHeader(Rect r, SessionInfo info)
        {
            Widgets.DrawBoxSolid(r, new Color(0.10f, 0.11f, 0.13f));

            // Centred rather than parked at the top: the bar is as tall as a portrait
            // now, and everything that is not a portrait reads as adrift in it if it
            // hangs off the ceiling.
            var label = new Rect(r.x + Pad, r.y + (r.height - 22f) / 2f, r.width - 340f, 22f);
            Text.Font = GameFont.Small;

            var state = info?.State ?? AgentState.Down;
            GUI.color = StateColor(state);
            Widgets.Label(label, $"{_name}  [{state.ToString().ToLower()}]  {_cols}x{_rows}");
            GUI.color = Color.white;

            float x = r.xMax - Pad;
            float by = r.y + (r.height - 24f) / 2f;

            x -= 90f;
            if (Widgets.ButtonText(new Rect(x, by, 86f, 24f), "Close"))
                Close();

            x -= 94f;
            if (Widgets.ButtonText(new Rect(x, by, 90f, 24f), "Restart"))
                SessionHub.Instance.Restart(_name, Fail);

            x -= 94f;
            if (Widgets.ButtonText(new Rect(x, by, 90f, 24f), "Stop"))
                SessionHub.Instance.Stop(_name, Fail);
        }

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

        static void Fail(string msg) => Messages.Message($"SlopWorld: {msg}",
            MessageTypeDefOf.RejectInput, false);

        void DrawCentered(Rect r, string msg)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(r, msg);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>The daemon's own limits, applied here so what we ask for is
        /// always something it can answer with exactly. Asking past them would leave
        /// the loop below chasing a size the clamp will never report back.</summary>
        const int MinCols = 20, MaxCols = 500, MinRows = 5, MaxRows = 200;

        /// <summary>
        /// Tell the daemon to reshape the pane to whatever fits the screen, and keep
        /// telling it until the frames coming back are that shape.
        ///
        /// The second half is what makes this a loop rather than a statement, and it
        /// is the whole of the "terminal opens at a fixed size" bug: a resize is one
        /// fire-and-forget message over a socket that may be down at that moment, and
        /// the daemon answers a size it already holds with a no-op. So a window that
        /// asks once and believes itself ends up drawing a 120x34 pane full-screen
        /// with no way back. The frame carries the emulator's own dimensions, which
        /// is the only honest answer to "what shape is the pane", so that is what we
        /// close the loop on.
        /// </summary>
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
                // Debounce: dragging the game window would otherwise spam SIGWINCH at
                // the agent, and Claude Code redraws its whole TUI on every one.
                _resizeAt = Time.realtimeSinceStartup + 0.2f;
                _sizeDirty = true;
                return;
            }

            // Same numbers as last frame, and nothing already on its way out. If the
            // pane we are drawing is not that shape, the last ask did not land - so
            // ask again, slowly, until it does. A scrolled frame is a capture of
            // history and says nothing about the live pane, so it is not evidence.
            if (_sizeDirty || buf.Off > 0) return;
            if (buf.Cols == cols && buf.Rows == rows) return;

            _resizeAt = Time.realtimeSinceStartup + 1f;
            _sizeDirty = true;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (!_sizeDirty || Time.realtimeSinceStartup < _resizeAt) return;

            // A socket that is down drops the message on the floor, so hold the ask
            // rather than spending it: this is the redeploy case, where the daemon
            // is away for a couple of seconds and the window is up throughout.
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
            var style = TerminalFont.Style;
            float cw = TerminalFont.CellW;
            float ch = TerminalFont.CellH;

            EnsureRuns(buf);
            SyncSnap();

            for (int row = 0; row < buf.Runs.Length; row++)
            {
                float y = body.y + row * ch;
                if (y > body.yMax) break;

                // The band this row's backgrounds fill, snapped so it meets its
                // neighbours' on a pixel rather than near one; see SnapY.
                float bgTop = SnapY(y);
                float bgBot = SnapY(body.y + (row + 1) * ch);

                foreach (var run in buf.Runs[row])
                {
                    // Draw at the run's true column, so wide chars (which the
                    // daemon re-anchors with CHA) don't shift the rest of the line.
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
                }
            }

            DrawCursor(body, buf, cw, ch);

            GUI.color = Color.white;
        }

        // The GUI-to-screen transform, sampled once a draw; see SnapX.
        static float _snapSx = 1f, _snapSy = 1f, _snapOx, _snapOy;

        /// <summary>
        /// Reads how this frame's GUI coordinates land on the screen. Two points
        /// are enough - the transform is a scale and an offset - and sampling it
        /// beats reading GUI.matrix, which carries the UI scale but not the offset
        /// of the group a window draws inside.
        /// </summary>
        static void SyncSnap()
        {
            var p0 = GUIUtility.GUIToScreenPoint(Vector2.zero);
            var p1 = GUIUtility.GUIToScreenPoint(Vector2.one);
            _snapSx = Mathf.Abs(p1.x - p0.x) > 0.0001f ? p1.x - p0.x : 1f;
            _snapSy = Mathf.Abs(p1.y - p0.y) > 0.0001f ? p1.y - p0.y : 1f;
            _snapOx = p0.x;
            _snapOy = p0.y;
        }

        /// <summary>
        /// Rounds a coordinate so it lands on a whole screen pixel.
        ///
        /// This is the thin black line that ran through a block of coloured diff.
        /// A cell is 19 units tall and the UI runs at 1.75, so a row is 33.25
        /// pixels: row n's background ends exactly where row n+1's begins in
        /// arithmetic, but the shared edge sits on a pixel centre every fourth row
        /// and a pixel split down the middle by two separate quads can come out
        /// belonging to neither. Snapping every box's edges to the pixel grid is
        /// what makes the rows tile rather than nearly tile. It has to be the
        /// *screen* grid: a whole unit here is 1.75 pixels there, so rounding in
        /// GUI coordinates would leave the fraction exactly where it was.
        /// </summary>
        static float SnapX(float v) => (Mathf.Round(v * _snapSx + _snapOx) - _snapOx) / _snapSx;

        static float SnapY(float v) => (Mathf.Round(v * _snapSy + _snapOy) - _snapOy) / _snapSy;

        /// <summary>Draws one run, breaking out every char the face cannot advance
        /// by exactly one cell and placing it alone on its own column. Claude Code
        /// opens its prompt with a chevron no mono face here has; drawn inline it
        /// took no width at all, and the whole input line slid a cell left of the
        /// grid - and of the cursor we paint on it.</summary>
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

        /// <summary>Draws text[from..to) at its column, whole runs without a copy.</summary>
        static void DrawSpan(string text, int from, int to,
            float x, float y, float cw, float ch, GUIStyle style)
        {
            if (to <= from) return;
            string seg = from == 0 && to == text.Length ? text : text.Substring(from, to - from);
            GUI.Label(new Rect(x + from * cw, y, seg.Length * cw + cw, ch), seg, style);
        }

        /// <summary>Cursor per reported shape; blinks on a half-second beat unless
        /// the app asked for a steady cursor.</summary>
        void DrawCursor(Rect body, ScreenBuf buf, float cw, float ch)
        {
            if (buf.Cy >= buf.Rows) return;
            if (buf.CursorBlink && (int)(Time.realtimeSinceStartup * 2f) % 2 != 0)
                return;

            float x = body.x + buf.Cx * cw;
            float y = body.y + buf.Cy * ch;
            if (!body.Contains(new Vector2(x, y))) return;

            float l = SnapX(x), r = SnapX(x + cw);
            float t = SnapY(y), b = SnapY(y + ch);

            var col = new Color(0.83f, 0.85f, 0.86f, 0.65f);
            switch (buf.CursorShape)
            {
                case 1: // underline
                    Widgets.DrawBoxSolid(new Rect(l, b - 2f, r - l, 2f), col);
                    break;
                case 2: // beam
                    Widgets.DrawBoxSolid(new Rect(l, t, 2f, b - t), col);
                    break;
                default: // block
                    Widgets.DrawBoxSolid(new Rect(l, t, r - l, b - t), col);
                    break;
            }
        }

        // -------------------------------------------------------------- input

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

        void HandleKey(Event e)
        {
            // Shift+Escape is the way out; a bare Escape must reach the agent.
            if (e.keyCode == KeyCode.Escape && e.shift)
            {
                Close();
                e.Use();
                return;
            }

            // The other way out is the key that opened this, and it has to be caught
            // here rather than in TerminalHotkeys: a window absorbing input around
            // itself makes WindowStack.HandleEventsHighPriority Use every KeyDown,
            // and that runs earlier in UIRoot.UIRootOnGUI than any game component.
            // So the pane holds the closing half of its own hotkey. While it is
            // bound here, F12 is not a key the agent ever receives.
            if (SlopDefOf.SlopQuickTerminal != null && SlopDefOf.SlopQuickTerminal.KeyDownEvent)
            {
                Close();
                e.Use();
                return;
            }

            // Alt+1..9 (and Alt+0 for the tenth) jump to that portrait in the strip
            // above the pane. Ahead of the offline check on purpose: switching is
            // local, the subscription survives a dead socket, and a terminal that
            // will not even change pane while slopd restarts reads as hung.
            int slot = SlotKey(e);
            if (slot >= 0 && e.alt)
            {
                SwitchToSlot(slot);
                e.Use();
                return;
            }

            // Offline: the hub drops sends on the floor, so count them and say so
            // in the banner rather than letting the terminal eat what was typed.
            if (!SessionHub.Instance.Online)
            {
                if (e.keyCode != KeyCode.None || e.character != '\0')
                {
                    _droppedKeys++;
                    e.Use();
                }
                return;
            }

            if (e.keyCode != KeyCode.None)
            {
                string key = MapKey(e);
                if (key != null)
                {
                    JumpToLive();
                    Flush();
                    SessionHub.Instance.SendKeys(_name, new[] { key }, false);
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

            // Unity delivers printable input as a second event carrying only the
            // character, so this is where ordinary typing lands.
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

        /// <summary>The 0-based slot a number key names, or -1 for anything else.
        /// Zero is the tenth, the way a tabbed terminal counts.</summary>
        static int SlotKey(Event e)
        {
            var k = e.keyCode;
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return k - KeyCode.Alpha1;
            if (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9) return k - KeyCode.Keypad1;
            if (k == KeyCode.Alpha0 || k == KeyCode.Keypad0) return 9;
            return -1;
        }

        /// <summary>Points the pane at the nth agent in the strip. A slot past the
        /// end is a no-op rather than a wrap: the keys are meant to be muscle memory
        /// for a fixed portrait, and wrapping would land somewhere unrelated every
        /// time an agent comes or goes. A down agent gets started instead, which is
        /// what a click on the same portrait does.</summary>
        void SwitchToSlot(int slot)
        {
            var order = AgentColony.InBarOrder();
            if (slot >= order.Count) return;

            string name = order[slot];
            if (name == _name) return;

            var info = SessionHub.Instance.Get(name);
            if (info == null) return;

            if (info.Gone) SessionHub.Instance.Start(name);
            else SwitchTo(name);
        }

        // --------------------------------------------------------- scroll / select

        void HandleWheel(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition)) return;

            var live = SessionHub.Instance.Screen(_name);
            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(e.delta.y)), 1, 5);
            bool up = e.delta.y < 0;

            // App wants the mouse: forward wheel reports at the pointer cell.
            if (live != null && live.AppMouse)
            {
                var cell = CellAt(body, e.mousePosition);
                string act = up ? "wheelup" : "wheeldown";
                for (int k = 0; k < step; k++)
                    SessionHub.Instance.SendMouse(_name, act, 0, cell.x, cell.y);
                e.Use();
                return;
            }

            // Alt-screen app with no mouse (less, man, git log): the terminal
            // convention is to translate the wheel to arrow keys.
            if (live != null && live.AltScreen)
            {
                var keys = new string[step];
                for (int k = 0; k < step; k++) keys[k] = up ? "Up" : "Down";
                SessionHub.Instance.SendKeys(_name, keys, false);
                e.Use();
                return;
            }

            // Otherwise walk our own scrollback view. Wheel up goes back in history.
            if (up) _scrollOff += step;
            else _scrollOff = Mathf.Max(0, _scrollOff - step);

            ClearSelection();
            if (_scrollOff > 0) SessionHub.Instance.RequestScroll(_name, _scrollOff);
            e.Use();
        }

        void HandleMouse(Rect body, Event e)
        {
            // Forward to the app when it wants the mouse, unless Shift is held -
            // Shift forces our own local selection, like a real terminal. The
            // forwarder hands a gesture back (false) when it turns out to be one
            // the app never asked for, which is where a drag-select comes from.
            var live = SessionHub.Instance.Screen(_name);
            if (live != null && live.AppMouse && !e.shift && HandleMouseForward(body, e))
                return;

            if (e.button != 0) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!body.Contains(e.mousePosition)) return;
                    _selA = _selB = CellAt(body, e.mousePosition);
                    _dragging = true;
                    _hasSel = false;
                    e.Use();
                    return;

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

        /// <summary>
        /// Forwards a click/drag to an app that asked for the mouse. Answers false
        /// for a gesture the caller should handle itself instead.
        ///
        /// Which is the whole of "copy doesn't work". An app in click-reporting
        /// mode - Claude Code is one - has asked to hear about buttons and said
        /// nothing about motion, so a drag across its output was never its to
        /// receive; forwarding it anyway spent the gesture on an app that ignores
        /// it and left the pane with no way to select text at all short of
        /// holding Shift, which nothing on screen says. So the press goes over as
        /// a press, and the moment it turns into a drag we close that click where
        /// it started - the app sees an ordinary click, complete - and take the
        /// rest of the gesture back as a selection.
        /// </summary>
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
                    // Only the left button selects; anything else the app did not
                    // ask about is swallowed rather than handed to a path that
                    // would drop it anyway.
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

        void JumpToLive() => _scrollOff = 0;

        void ClearSelection()
        {
            _hasSel = false;
            _dragging = false;
        }

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

        /// <summary>
        /// Puts the selection on the clipboard - the *host's*, through the daemon.
        ///
        /// `GUIUtility.systemCopyBuffer` is set as well and is not the point of
        /// this: on the Unity player the game is, that buffer is as often the
        /// process's own as it is the desktop's, so a selection copied out of a
        /// pane could not be pasted into a browser and the feature read as broken.
        /// slopd is on the host with the session's display already in its
        /// environment, which makes it the one half of this that can reach the
        /// clipboard the player meant. Failure is a log line rather than a dialog:
        /// the copy is a gesture, not a command, and a box over the terminal every
        /// time a host has no `wl-copy` is worse than the miss.
        /// </summary>
        void CopySelection()
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            string text = SelectionText(buf);
            if (string.IsNullOrEmpty(text)) return;

            GUIUtility.systemCopyBuffer = text;
            SlopClient.Post("/api/clipboard", "{\"text\":" + JVal.Q(text) + "}", null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        /// <summary>The other direction: paste what the host has, falling back to
        /// the game's own buffer when the daemon cannot reach a clipboard. Reading
        /// it is a round trip, so the paste lands a frame or two later - which is
        /// invisible next to an agent's own redraw.</summary>
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
            if ((!_hasSel && !_dragging) || _selA == _selB) return;
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
                Widgets.DrawBoxSolid(new Rect(l, t, r - l, bot - t), SelColor);
            }
        }

        void DrawScrollHint(Rect body)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.98f, 0.80f, 0.30f);
            Widgets.Label(new Rect(body.x, body.y, body.width - 6f, 20f),
                $"scrollback -{_scrollOff}   type or scroll down to resume");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        static void EnsureRuns(ScreenBuf buf)
        {
            if (buf.Runs != null) return;
            buf.Runs = new List<SgrRun>[buf.Lines.Length];
            for (int i = 0; i < buf.Lines.Length; i++)
                buf.Runs[i] = Sgr.ParseLine(buf.Lines[i]);
        }

        /// <summary>How far a row's text actually goes. The daemon trims a row's
        /// trailing blanks only when they carry nothing - a cell holding a
        /// background colour is a cell it has to emit - so a diff line, a banner
        /// or anything else coloured to the right margin arrives padded out to the
        /// full width with spaces. Selecting one of those highlighted a block of
        /// empty space and copied the spaces with it, which is the "highlights the
        /// whole line" of the report and, quietly, a paste full of trailing
        /// whitespace.</summary>
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

        /// <summary>Batches a frame's worth of typing into one send-keys call.</summary>
        void Flush()
        {
            if (_literal.Length == 0) return;
            SessionHub.Instance.SendKeys(_name, new[] { _literal.ToString() }, true);
            _literal.Length = 0;
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current.type == EventType.Repaint) Flush();
        }

        static string MapKey(Event e)
        {
            // tmux turns modifier-prefixed names (C-Left, M-Up) into the xterm
            // sequences apps read for word-wise motion; a bare "Left" for Ctrl+Left
            // moved one char. Shift is left off - apps that don't grok S- sequences
            // would drop a shift+arrow that used to at least move the cursor.
            string mod = "";
            if (e.control) mod += "C-";
            if (e.alt) mod += "M-";

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
