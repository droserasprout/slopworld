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
        const float HeaderH = 28f;
        const float Pad = 6f;

        readonly string _name;
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

        static readonly Color SelColor = new Color(0.30f, 0.50f, 0.90f, 0.35f);

        public static TerminalWindow Open(string name)
        {
            // Re-opening the same session should focus it, not stack a second copy.
            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null)
            {
                if (existing._name == name) return existing;
                existing.Close(false);
            }
            var w = new TerminalWindow(name);
            Find.WindowStack.Add(w);
            return w;
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

        protected override void SetInitialSizeAndPosition() =>
            windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);

        public override void PreOpen()
        {
            base.PreOpen();
            SessionHub.Instance.Subscribe(_name);
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

            var header = new Rect(rect.x, rect.y, rect.width, HeaderH);
            DrawHeader(header, info);

            var body = new Rect(
                rect.x + Pad,
                rect.y + HeaderH + Pad,
                rect.width - Pad * 2,
                rect.height - HeaderH - Pad * 2);

            HandleInput(body);

            if (info == null)
            {
                DrawCentered(body, $"No session named '{_name}'.");
                return;
            }
            if (!info.Alive)
            {
                DrawCentered(body, $"'{_name}' is not running.  Start it from the agents tab.");
                return;
            }

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

            NegotiateSize(body);
            DrawScreen(body, buf);
            DrawSelection(body, buf);

            if (_scrollOff > 0)
                DrawScrollHint(body);
        }

        void DrawHeader(Rect r, SessionInfo info)
        {
            Widgets.DrawBoxSolid(r, new Color(0.10f, 0.11f, 0.13f));

            var label = new Rect(r.x + Pad, r.y + 4f, r.width - 340f, 22f);
            Text.Font = GameFont.Small;

            var state = info?.State ?? AgentState.Dead;
            GUI.color = StateColor(state);
            Widgets.Label(label, $"{_name}  [{state.ToString().ToLower()}]  {_cols}x{_rows}");
            GUI.color = Color.white;

            float x = r.xMax - Pad;
            x -= 90f;
            if (Widgets.ButtonText(new Rect(x, r.y + 2f, 86f, 24f), "Close"))
                Close();

            x -= 94f;
            if (Widgets.ButtonText(new Rect(x, r.y + 2f, 90f, 24f), "Restart"))
                SessionHub.Instance.Restart(_name, Fail);

            x -= 94f;
            if (info != null && info.Alive)
            {
                if (Widgets.ButtonText(new Rect(x, r.y + 2f, 90f, 24f), "Stop"))
                    SessionHub.Instance.Stop(_name, Fail);
            }
            else if (Widgets.ButtonText(new Rect(x, r.y + 2f, 90f, 24f), "Start"))
            {
                SessionHub.Instance.Start(_name, Fail);
            }
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

        /// <summary>Tell the daemon to reshape the pane to whatever fits the screen.</summary>
        void NegotiateSize(Rect body)
        {
            var style = TerminalFont.Style;
            if (TerminalFont.CellW <= 0.01f) return;

            int cols = Mathf.Max(20, Mathf.FloorToInt(body.width / TerminalFont.CellW));
            int rows = Mathf.Max(5, Mathf.FloorToInt(body.height / TerminalFont.CellH));

            if (cols == _cols && rows == _rows) return;

            _cols = cols;
            _rows = rows;
            // Debounce: dragging the game window would otherwise spam SIGWINCH at
            // the agent, and Claude Code redraws its whole TUI on every one.
            _resizeAt = Time.realtimeSinceStartup + 0.2f;
            _sizeDirty = true;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (_sizeDirty && Time.realtimeSinceStartup >= _resizeAt)
            {
                SessionHub.Instance.Resize(_name, _cols, _rows);
                _sizeDirty = false;
            }
        }

        void DrawScreen(Rect body, ScreenBuf buf)
        {
            var style = TerminalFont.Style;
            float cw = TerminalFont.CellW;
            float ch = TerminalFont.CellH;

            EnsureRuns(buf);

            for (int row = 0; row < buf.Runs.Length; row++)
            {
                float y = body.y + row * ch;
                if (y > body.yMax) break;

                foreach (var run in buf.Runs[row])
                {
                    // Draw at the run's true column, so wide chars (which the
                    // daemon re-anchors with CHA) don't shift the rest of the line.
                    float x = body.x + run.Col * cw;
                    float w = run.Text.Length * cw;

                    if (run.HasBg)
                        Widgets.DrawBoxSolid(new Rect(x, y, w, ch), run.Bg);

                    style.normal.textColor = run.Fg;
                    GUI.Label(new Rect(x, y, w + cw, ch), run.Text, style);
                }
            }

            DrawCursor(body, buf, cw, ch);

            GUI.color = Color.white;
        }

        /// <summary>Cursor per reported shape, blinking on a half-second beat.</summary>
        void DrawCursor(Rect body, ScreenBuf buf, float cw, float ch)
        {
            if (buf.Cy >= buf.Rows || (int)(Time.realtimeSinceStartup * 2f) % 2 != 0)
                return;

            float x = body.x + buf.Cx * cw;
            float y = body.y + buf.Cy * ch;
            if (!body.Contains(new Vector2(x, y))) return;

            var col = new Color(0.83f, 0.85f, 0.86f, 0.65f);
            switch (buf.CursorShape)
            {
                case 1: // underline
                    Widgets.DrawBoxSolid(new Rect(x, y + ch - 2f, cw, 2f), col);
                    break;
                case 2: // beam
                    Widgets.DrawBoxSolid(new Rect(x, y, 2f, ch), col);
                    break;
                default: // block
                    Widgets.DrawBoxSolid(new Rect(x, y, cw, ch), col);
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
                    string clip = GUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(clip))
                        SessionHub.Instance.SendKeys(_name, new[] { clip }, true);
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

        // --------------------------------------------------------- scroll / select

        void HandleWheel(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition)) return;

            int step = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(e.delta.y)));
            // Wheel up (delta.y < 0) walks back into scrollback.
            if (e.delta.y < 0) _scrollOff += step;
            else _scrollOff = Mathf.Max(0, _scrollOff - step);

            ClearSelection();
            if (_scrollOff > 0) SessionHub.Instance.RequestScroll(_name, _scrollOff);
            e.Use();
        }

        void HandleMouse(Rect body, Event e)
        {
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

        void CopySelection()
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            string text = SelectionText(buf);
            if (!string.IsNullOrEmpty(text))
                GUIUtility.systemCopyBuffer = text;
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
                int startCol = row == a.y ? Mathf.Max(0, a.x) : 0;
                // The head cell is inclusive, matching the highlight.
                int endCol = row == b.y ? b.x + 1 : line.Length;
                startCol = Mathf.Clamp(startCol, 0, line.Length);
                endCol = Mathf.Clamp(endCol, 0, line.Length);
                if (endCol > startCol) sb.Append(line.Substring(startCol, endCol - startCol));
                if (row < r1) sb.Append('\n');
            }
            return sb.ToString();
        }

        void DrawSelection(Rect body, ScreenBuf buf)
        {
            if ((!_hasSel && !_dragging) || _selA == _selB) return;
            EnsureRuns(buf);

            float cw = TerminalFont.CellW, ch = TerminalFont.CellH;
            OrderedSel(out var a, out var b);
            int rows = buf.Runs.Length;

            for (int row = Mathf.Max(0, a.y); row <= Mathf.Min(rows - 1, b.y); row++)
            {
                int lineLen = RowText(buf, row).Length;
                int startCol = Mathf.Max(0, row == a.y ? a.x : 0);
                int endCol = row == b.y ? b.x + 1 : lineLen;
                endCol = Mathf.Max(startCol, endCol);

                float y = body.y + row * ch;
                if (y > body.yMax) break;
                float w = (endCol - startCol) * cw;
                if (w <= 0f) continue;

                Widgets.DrawBoxSolid(new Rect(body.x + startCol * cw, y, w, ch), SelColor);
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
            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.Escape: return "Escape";
                case KeyCode.Backspace: return "BSpace";
                case KeyCode.Tab: return e.shift ? "BTab" : "Tab";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.Home: return "Home";
                case KeyCode.End: return "End";
                case KeyCode.PageUp: return "PPage";
                case KeyCode.PageDown: return "NPage";
                case KeyCode.Delete: return "DC";
                case KeyCode.Insert: return "IC";
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
