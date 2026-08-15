using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Fullscreen view of one agent's pane: renders what slopd captured, forwards
    // keystrokes back as tmux keys.
    public partial class TerminalWindow : Window
    {
        const float Pad = SlopWidgets.GapS;

        // Not readonly: the strip switches sessions by pointing the window at a new one,
        // which keeps the terminal's scroll and selection instead of rebuilding it. Null is
        // a window with no pane behind it at all - the options menu opened from the map, and
        // nothing to go back to when it is left.
        string _name;

        // What is in the body instead of the pane, or null for the pane itself. See
        // IContentView: the window is the chrome, and this is what the chrome is showing.
        IContentView _content;
        readonly StringBuilder _literal = new StringBuilder();
        int _semicolonFrame = -1;

        // Named terminal keys are a dispatch table rather than a second list of conditionals
        // in the input method. The local table runs before the online check because closing and
        // Shift+Enter are window actions; the full table runs once ordinary terminal input is
        // known to have somewhere to go.
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _localKeyHandlers;
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _keyHandlers;

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
        bool _wordDragging;
        Vector2Int _wordStart, _wordEnd;
        bool _hasSel;
        Vector2Int _selA, _selB;

        // The cell its press landed on, where the click is closed if the gesture turns
        // out to be a drag the app never asked for.
        bool _mouseFwd;
        Vector2Int _fwdCell;

        // The link the pointer is over and its row spans. Held for the draw rather than looked
        // up there: the same answer decides the highlight, tooltip and Ctrl+click target.
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

        // Content views remain read-only, but a view opened over a pane can still offer the
        // terminal's paste action to the agent behind it. A view opened from the map has no
        // destination, so its Paste menu item is disabled.
        public static bool CanPasteClipboardToAgent =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._name != null;

        public static void PasteClipboardToAgent()
        {
            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (window == null || window._name == null) return;
            window.JumpToLive();
            window.PasteClipboard();
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
            _localKeyHandlers = new Dictionary<KeyCode, System.Func<Event, bool>>
            {
                { KeyCode.Escape, HandleEscapeKey },
                { KeyCode.Return, HandleReturnKey },
            };
            _keyHandlers = new Dictionary<KeyCode, System.Func<Event, bool>>
            {
                { KeyCode.Escape, ForwardMappedKey },
                { KeyCode.Return, ForwardMappedKey },
                { KeyCode.KeypadEnter, ForwardMappedKey },
                { KeyCode.Backspace, ForwardMappedKey },
                { KeyCode.Tab, ForwardMappedKey },
                { KeyCode.UpArrow, ForwardMappedKey },
                { KeyCode.DownArrow, ForwardMappedKey },
                { KeyCode.LeftArrow, ForwardMappedKey },
                { KeyCode.RightArrow, ForwardMappedKey },
                { KeyCode.Home, ForwardMappedKey },
                { KeyCode.End, ForwardMappedKey },
                { KeyCode.PageUp, ForwardMappedKey },
                { KeyCode.PageDown, ForwardMappedKey },
                { KeyCode.Delete, ForwardMappedKey },
                { KeyCode.Insert, ForwardMappedKey },
                { KeyCode.F1, ForwardMappedKey },
                { KeyCode.F2, ForwardMappedKey },
                { KeyCode.F3, ForwardMappedKey },
                { KeyCode.F4, ForwardMappedKey },
                { KeyCode.F5, ForwardMappedKey },
                { KeyCode.F6, ForwardMappedKey },
                { KeyCode.F7, ForwardMappedKey },
                { KeyCode.F8, ForwardMappedKey },
                { KeyCode.F9, ForwardMappedKey },
                { KeyCode.F10, ForwardMappedKey },
                { KeyCode.F11, ForwardMappedKey },
                { KeyCode.F12, ForwardMappedKey },
                { KeyCode.C, HandleControlC },
                { KeyCode.V, HandleControlV },
                { KeyCode.Semicolon, HandleSemicolonKey },
                { KeyCode.Colon, HandleSemicolonKey },
                { KeyCode.A, ForwardMappedKey },
                { KeyCode.B, ForwardMappedKey },
                { KeyCode.D, ForwardMappedKey },
                { KeyCode.E, ForwardMappedKey },
                { KeyCode.F, ForwardMappedKey },
                { KeyCode.G, ForwardMappedKey },
                { KeyCode.H, ForwardMappedKey },
                { KeyCode.I, ForwardMappedKey },
                { KeyCode.J, ForwardMappedKey },
                { KeyCode.K, ForwardMappedKey },
                { KeyCode.L, ForwardMappedKey },
                { KeyCode.M, ForwardMappedKey },
                { KeyCode.N, ForwardMappedKey },
                { KeyCode.O, ForwardMappedKey },
                { KeyCode.P, ForwardMappedKey },
                { KeyCode.Q, ForwardMappedKey },
                { KeyCode.R, ForwardMappedKey },
                { KeyCode.S, ForwardMappedKey },
                { KeyCode.T, ForwardMappedKey },
                { KeyCode.U, ForwardMappedKey },
                { KeyCode.W, ForwardMappedKey },
                { KeyCode.X, ForwardMappedKey },
                { KeyCode.Y, ForwardMappedKey },
                { KeyCode.Z, ForwardMappedKey },
            };
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

        // The window is also the host for settings and other content views. Those views are
        // SlopWorld chrome, so their fullscreen backing surface belongs to UIScheme; only the
        // pane itself is allowed to expose the terminal palette here.
        Color Background => _content == null ? Sgr.DefaultBg : SlopWidgets.WindowBg;

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
            SearchView.CloseViewerIf(_name);
            GitView.CloseViewerIf(_name);
        }

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;
            var info = hub.Get(_name);

            Widgets.DrawBoxSolid(rect, Background);

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

            if (input)
            {
                CaptureSemicolonInput();
                HandleInput(body);
            }

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

        // The negotiated shape, for the top bar to say in the layout where this window draws
        // no header of its own. Blank until the first frame has been measured.
        public string Shape => _cols > 0 ? $"{_cols}x{_rows}" : "";

        public static Color StateColor(AgentState s)
        {
            switch (s)
            {
                case AgentState.Working: return SlopWidgets.StateWorking;
                case AgentState.Waiting: return SlopWidgets.StateWaiting;
                case AgentState.Idle: return SlopWidgets.StateIdle;
                default: return SlopWidgets.StateDown;
            }
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

        void ClearSelection()
        {
            _hasSel = false;
            _dragging = false;
            _wordDragging = false;
        }

        // Both ends inclusive, the way a dragged selection states them.
        void SelectSpan(int row, int c0, int c1)
        {
            _selA = new Vector2Int(c0, row);
            _selB = new Vector2Int(c1, row);
            _hasSel = true;
            _dragging = false;
            _wordDragging = false;
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
            _wordStart = new Vector2Int(c0, cell.y);
            _wordEnd = new Vector2Int(c1, cell.y);
            _selA = _wordStart;
            _selB = _wordEnd;
            _hasSel = true;
            _dragging = true;
            _wordDragging = true;
            CopySelection();
        }

        void UpdateWordSelection(Vector2Int cell)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return;

            string line = RowText(buf, cell.y);
            int len = ContentLen(line);
            if (len == 0) return;
            int x = Mathf.Clamp(cell.x, 0, len - 1);
            char anchor = line[x];
            bool word = IsWordChar(anchor);
            int c0 = x, c1 = x;
            while (c0 > 0 && SameClass(line[c0 - 1], anchor, word)) c0--;
            while (c1 + 1 < len && SameClass(line[c1 + 1], anchor, word)) c1++;

            var destinationStart = new Vector2Int(c0, cell.y);
            var destinationEnd = new Vector2Int(c1, cell.y);
            if (Before(cell, _wordStart))
            {
                _selA = destinationStart;
                _selB = _wordEnd;
            }
            else
            {
                _selA = _wordStart;
                _selB = destinationEnd;
            }
            _hasSel = true;
        }

        static bool Before(Vector2Int a, Vector2Int b) =>
            a.y < b.y || (a.y == b.y && a.x < b.x);

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
            _wordDragging = false;
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
            var info = SessionHub.Instance.Get(_name);
            var breadcrumbs = AllBreadcrumbs();
            var breadcrumbMenu = new SlopSubmenu("Breadcrumbs",
                () => BreadcrumbOptions(breadcrumbs));
            breadcrumbMenu.Disabled = info == null || !info.Alive || breadcrumbs.Count == 0;
            options.Add(breadcrumbMenu);
            options.Add(new FloatMenuOption("Select all", SelectAll));

            if (_scrollOff > 0)
                options.Add(new FloatMenuOption("Back to the live view", () =>
                {
                    JumpToLive();
                    ClearSelection();
                }));

            OpenOverPane(new SlopMenu(options));
        }

        static List<string> AllBreadcrumbs() => SessionHub.Instance.Shortcuts
            .Where(s => s.Kind == ShortcutKind.Breadcrumb)
            .Select(s => s.Name)
            .ToList();

        List<FloatMenuOption> BreadcrumbOptions(List<string> names)
        {
            var options = new List<FloatMenuOption>();
            foreach (string name in names)
            {
                string picked = name;
                options.Add(new FloatMenuOption(picked, () =>
                {
                    JumpToLive();
                    SessionHub.Instance.PasteBreadcrumb(_name, picked,
                        Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
                }));
            }
            return options;
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
            GUI.color = SlopWidgets.Warn;
            SlopWidgets.RowLabel(new Rect(body.x, body.y, body.width - 6f, SlopWidgets.TinyH),
                $"scrollback -{_scrollOff}   type or scroll down to resume",
                TextAnchor.MiddleRight);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        // Colors are resolved into the runs at parse time, so a scheme change is a re-parse:
        // without it an idle pane keeps the old palette until the agent next writes, which on
        // an idle agent is never.
        static void EnsureRuns(ScreenBuf buf)
        {
            if (buf.Runs != null && buf.RunsRev == TerminalTheme.Rev) return;
            buf.Runs = Sgr.ParseLines(buf.Lines, buf.Cols);
            buf.RunsRev = TerminalTheme.Rev;
        }

        // The daemon trims trailing blanks only when they carry nothing, so anything colored
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
                Mathf.Ceil(Screen.height / Prefs.UIScale)), Background);

            Flush();
        }

        static string MapKey(Event e, bool altScreen)
        {
            // Use tmux's modifier names; Shift is forwarded only on the alt screen because
            // shells do not define the corresponding xterm sequences.
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
