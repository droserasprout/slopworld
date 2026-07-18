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

            var buf = hub.Screen(_name);
            if (buf == null || buf.Lines.Length == 0)
            {
                DrawCentered(body, hub.Online ? "Waiting for output..." : $"Daemon {hub.Status}");
                return;
            }

            NegotiateSize(body);
            DrawScreen(body, buf);
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

            if (buf.Runs == null)
            {
                buf.Runs = new List<SgrRun>[buf.Lines.Length];
                for (int i = 0; i < buf.Lines.Length; i++)
                    buf.Runs[i] = Sgr.ParseLine(buf.Lines[i]);
            }

            for (int row = 0; row < buf.Runs.Length; row++)
            {
                float y = body.y + row * ch;
                if (y > body.yMax) break;

                float x = body.x;
                foreach (var run in buf.Runs[row])
                {
                    float w = run.Text.Length * cw;

                    if (run.HasBg)
                        Widgets.DrawBoxSolid(new Rect(x, y, w, ch), run.Bg);

                    style.normal.textColor = run.Fg;
                    GUI.Label(new Rect(x, y, w + cw, ch), run.Text, style);

                    x += w;
                }
            }

            // Block cursor, blinking on a half-second beat.
            if (buf.Cy < buf.Rows && (int)(Time.realtimeSinceStartup * 2f) % 2 == 0)
            {
                var cur = new Rect(body.x + buf.Cx * cw, body.y + buf.Cy * ch, cw, ch);
                if (body.Contains(new Vector2(cur.x, cur.y)))
                    Widgets.DrawBoxSolid(cur, new Color(0.83f, 0.85f, 0.86f, 0.65f));
            }

            GUI.color = Color.white;
        }

        // -------------------------------------------------------------- input

        void HandleInput(Rect body)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

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
                    Flush();
                    SessionHub.Instance.SendKeys(_name, new[] { key }, false);
                    e.Use();
                    return;
                }

                if (e.control && e.keyCode == KeyCode.V)
                {
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
                _literal.Append(e.character);
                e.Use();
                return;
            }

            if (e.keyCode != KeyCode.None)
                e.Use(); // swallow it so RimWorld hotkeys don't fire behind us
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
