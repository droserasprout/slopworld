using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TerminalWindow state, session binding, and Window lifecycle.
    public partial class TerminalWindow
    {
        // Not readonly: the strip switches sessions by pointing the window at a new one,
        // which keeps the terminal's scroll and selection instead of rebuilding it. Null is
        // a window with no pane behind it at all - the options menu opened from the map, and
        // nothing to go back to when it is left.
        string _name;

        // A pane opened by tab navigation may intentionally point at a stopped agent. Keep
        // that pane visible until its Start gizmo is pressed; an agent that exits during
        // normal terminal use still follows the live-session handoff below.
        bool _showStopped;

        // What is in the body instead of the pane, or null for the pane itself. See
        // IContentView: the window is the chrome, and this is what the chrome is showing.
        IContentView _content;
        readonly StringBuilder _literal = new StringBuilder();
        int _semicolonFrame = -1;
        readonly TerminalInputHandler _input;

        int _cols, _rows;
        float _resizeAt;
        bool _sizeDirty;
        float _cursorBlinkAt;

        // Every terminal window is fullscreen and shares the same pane geometry. Keep the
        // last measured shape outside the window instance so a fresh pager can start there
        // instead of drawing once at slopd's boot size and making less redraw on the first
        // resize.
        static int _cachedCols, _cachedRows;

        // Mouse-wheel scrollback: lines scrolled up from the live bottom.
        int _scrollOff;
        // Leading-edge throttle: the first request sends immediately, then the rest ride the
        // display beat. `_wantedScrollOff` is the newest prefetch target and `_sentScrollOff`
        // is what was last sent. Request ids retain their own offsets until answered, so a
        // delayed prefetch can populate the row cache without clamping a newer gesture.
        int _wantedScrollOff;
        int _sentScrollOff;
        float _nextScrollSend;
        bool _scrollPending;
        ulong _scrollRequestId;
        const float ScrollBeat = 1f / 60f;

        // The daemon owns terminal history, but the visible position is local so a touchpad
        // can move between snapshots without waiting for a websocket round trip.
        const int MaxScrollLines = 10_000;
        readonly SmoothScroll _historyScroll = new SmoothScroll();
        struct ScrollbackState
        {
            public int Offset;
            public float Pixels;

            public ScrollbackState(int offset, float pixels)
            {
                Offset = offset;
                Pixels = pixels;
            }
        }

        // The window is reused as terminal tabs change, so the active history cache cannot
        // carry the position for every tab. Keep the position separately; the rows themselves
        // are still fetched again when a tab comes back into view.
        readonly Dictionary<string, ScrollbackState> _scrollbackStates =
            new Dictionary<string, ScrollbackState>();
        // A subscription can take a frame to answer after a tab switch. Retain the last
        // complete content for each session so that gap is filled by the right tab, not by
        // the render texture belonging to the tab we just left.
        readonly Dictionary<string, ScreenBuf> _displayedFrames =
            new Dictionary<string, ScreenBuf>();
        bool _historyScrollReady;
        bool _historyJumpPending;
        int _historyJumpOff;
        float _historyJumpPixels = -1f;
        float _historyMax;
        float _historyLastPixels;
        float _renderHistoryShift;
        int _historyTopOff = -1;
        bool _historyViewReady;
        readonly TerminalHistory _history = new TerminalHistory();
        readonly Dictionary<ulong, int> _historyRequests = new Dictionary<ulong, int>();
        // Stable fallback while the first prefetched window for a new position is in flight.
        ScreenBuf _historyDisplayedFrame;

        // Drag selection, in cell coordinates of the drawn buffer.
        bool _dragging;
        bool _selectionMoved;
        bool _wordDragging;
        bool _lineDragging;
        Vector2Int _wordStart, _wordEnd;
        int _lineStart;
        int _selectionControl;
        bool _hasSel;
        Vector2Int _selA, _selB;
        readonly MouseClickSequence _clicks = new MouseClickSequence();
        // The viewport offset the selection endpoints belong to. A history frame is an older
        // slice of the same terminal, so its rows move down as this offset increases.
        int _selectionOff;
        int _lastLiveSeq = -1;

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

        // A successful rename must not go through Open: that would reset the pane and can
        // briefly bind it to the old name while the sessions snapshot catches up. Keep the
        // existing window, scrollback and selection, changing only the session handle.
        internal static void RenameActive(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) ||
                oldName == newName) return;

            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            bool active = window != null && window._name == oldName;
            if (active)
            {
                window._name = newName;
                TerminalRecall.Remember(newName);
            }

            if (!active && SessionSelectable.Current != oldName) return;
            SessionSelectable.Current = newName;
            if (active && window._content == null) SelectAgent(newName);
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

        // The pane is on the Super layer, so an ordinary dialog opened from inside it would be
        // added underneath and never seen.
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

        internal bool AutoResumePending =>
            _name != null && SessionHub.Instance.Get(_name)?.AutoResumePending == true;

        // Rebinds the pane but keeps the window's place in the stack. Whatever was in the body
        // goes: being pointed at an agent is a request to see it. History rows belong to the
        // old session, while its scroll position is saved for the next visit.
        void SwitchTo(string name)
        {
            SetContent(null);
            if (name == _name) return;
            SaveScrollbackState(_name);
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
            _showStopped = _name != null && SessionHub.Instance.Get(_name)?.Gone == true;
            PrimeCachedSize();
            _scrollOff = 0;
            _wantedScrollOff = 0;
            _scrollPending = false;
            _nextScrollSend = 0f;
            _hasWheelDirection = false;
            _historyScrollReady = false;
            _historyJumpPending = false;
            _historyJumpPixels = -1f;
            _renderHistoryShift = 0f;
            _historyLastPixels = 0f;
            _historyTopOff = -1;
            _historyViewReady = false;
            _history.Reset();
            _historyRequests.Clear();
            _historyDisplayedFrame = null;
            RestoreScrollbackState(_name);
            if (_scrollOff > 0)
                _historyDisplayedFrame = CachedDisplayedFrame(_name);
            _selectionOff = 0;
            _lastLiveSeq = -1;
            ClearSelection();
            ResetCursorBlink();
        }

        void SaveScrollbackState(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            // `_historyScroll` may still hold the old position for one IMGUI pass after an
            // input jumps to live output. The integer mode is authoritative in that case.
            float pixels = _scrollOff <= 0 ? 0f
                : _historyScrollReady ? HistoryOffsetPixels() : -1f;
            _scrollbackStates[name] = new ScrollbackState(Mathf.Max(0, _scrollOff), pixels);
        }

        void RestoreScrollbackState(string name)
        {
            if (string.IsNullOrEmpty(name) ||
                !_scrollbackStates.TryGetValue(name, out var state))
            {
                _scrollOff = 0;
                return;
            }

            _scrollOff = Mathf.Clamp(state.Offset, 0, MaxScrollLines);
            _historyJumpPending = true;
            _historyJumpOff = _scrollOff;
            _historyJumpPixels = state.Pixels;
        }

        ScreenBuf CachedDisplayedFrame(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _displayedFrames.TryGetValue(name, out var frame) ? frame : null;
        }

        void RememberDisplayedFrame(string name, ScreenBuf frame)
        {
            if (string.IsNullOrEmpty(name) || frame == null || frame.Lines == null ||
                frame.Lines.Length == 0)
                return;

            if (_displayedFrames.TryGetValue(name, out var previous) &&
                previous.Seq == frame.Seq && previous.Off == frame.Off &&
                previous.Cols == frame.Cols && previous.Rows == frame.Rows)
                return;

            _displayedFrames[name] = frame.Snapshot();
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

        // The window is also the host for settings and other content views. Those views are
        // SlopWorld chrome, so their fullscreen backing surface belongs to UIScheme; only the
        // pane itself is allowed to expose the terminal palette here.
        static Color SolidTerminalBackground
        {
            get
            {
                var c = Sgr.DefaultBg;
                c.a = 1f;
                return c;
            }
        }

        Color Background => _content == null ? SolidTerminalBackground : SlopWidgets.WindowBg;

        public override void PreOpen()
        {
            base.PreOpen();
            _covering = true;
            if (_name == null) return;
            _showStopped = SessionHub.Instance.Get(_name)?.Gone == true;
            SessionHub.Instance.Subscribe(_name);
            SelectAgent(_name);
            PrimeCachedSize();
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

        bool EnsureSession(SessionHub hub)
        {
            var info = hub.Get(_name);
            if (info != null && info.Alive) _showStopped = false;
            // An agent that exits during normal terminal use takes its pane with it. A pane
            // advances to the next live session; an intentionally selected stopped agent is
            // held for its action gizmos; content views keep the window for their chrome.
            if (_name != null && (info == null || info.Gone))
            {
                // A rename event removes the old name before the save response retargets this
                // window. Hold the pane through that expected gap; otherwise the normal exit
                // handoff steals focus from the agent being renamed.
                if (hub.TryPendingRename(_name, out _)) return true;

                if (_content == null)
                {
                    if (_showStopped && info != null) return true;
                    // Ctrl+C/D can be the last input an agent receives. Keep the terminal
                    // focused on the next live session instead of dropping back to the map;
                    // ephemeral host shells take this path too, after they disappear from the
                    // daemon's session list.
                    string departed = _name;
                    if (FocusNextSession(departed)) return true;
                    Close();
                    return false;
                }
                hub.Unsubscribe(_name);
                _name = null;
            }
            else if (_name == null && _content == null)
            {
                Close();
                return false;
            }
            return true;
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

        ScreenBuf DisplayedScreen()
        {
            var hub = SessionHub.Instance;
            var live = hub.Screen(_name);
            NoteLiveFrame(live);
            if (_scrollOff <= 0)
            {
                _historyViewReady = false;
                SyncSelectionOffset(0);
                return live;
            }

            var sb = hub.ScrollScreen(_name);
            if (sb != null)
            {
                _history.Add(sb, live, _scrollOff);
                if (sb.History >= 0)
                {
                    _historyTopOff = sb.History;
                    ClampHistoryTarget();
                }
            }
            if (sb != null && _historyRequests.TryGetValue(sb.ScrollRequestId,
                    out int requestedOff))
            {
                _historyRequests.Remove(sb.ScrollRequestId);
                // Compare with this response's own request, not the gesture's newer target.
                // Several prefetched windows may be in flight at once.
                if (sb.History < 0 && sb.Off < requestedOff)
                {
                    _historyTopOff = sb.Off;
                    ClampHistoryTarget();
                }
            }

            float cellH = TerminalFont.CellH;
            float lines = cellH > 0.01f ? HistoryOffsetPixels() / cellH : _scrollOff;
            int anchor = Mathf.Max(0, Mathf.CeilToInt(lines - 0.0001f));
            bool extra = Mathf.Abs(lines - Mathf.Round(lines)) > 0.0001f;
            _historyViewReady = _history.TryView(anchor, extra, out var displayed);
            displayed = displayed ?? _historyDisplayedFrame ?? live;
            if (displayed != null) _historyDisplayedFrame = displayed;
            if (displayed != null) SyncSelectionOffset(displayed.Off);
            return displayed;
        }

        void ClampHistoryTarget()
        {
            if (_historyTopOff < 0 || _scrollOff <= _historyTopOff) return;
            _scrollOff = _historyTopOff;
            _historyJumpPending = true;
            _historyJumpOff = _historyTopOff;
            _historyJumpPixels = -1f;
        }

        bool HistoryInputEnabled(ScreenBuf live) =>
            _scrollOff > 0 || live == null ||
            (!live.AppMouse && !live.AltScreen && !IsEditorSession());

        void PrepareHistoryScroll(float cellH)
        {
            if (cellH <= 0.01f) return;

            float max = cellH * MaxScrollLines;
            if (!_historyScrollReady || Mathf.Abs(_historyMax - max) > 0.01f)
            {
                _historyMax = max;
                _historyScroll.JumpTo(new Vector2(0f, max));
                _historyScrollReady = true;
            }

            if (_historyJumpPending)
            {
                float pixels = _historyJumpPixels >= 0f
                    ? _historyJumpPixels : _historyJumpOff * cellH;
                _historyScroll.JumpTo(new Vector2(0f,
                    Mathf.Clamp(_historyMax - pixels, 0f, _historyMax)));
                _historyJumpPending = false;
                _historyJumpPixels = -1f;
            }
        }

        float HistoryOffsetPixels() =>
            Mathf.Clamp(_historyMax - _historyScroll.Position.y, 0f, _historyMax);

        // Retain the fractional local position while fetching overlapping whole-line windows.
        void UpdateHistoryTarget(float cellH, ScreenBuf live)
        {
            if (!_historyScrollReady || cellH <= 0.01f) return;

            float pixels = HistoryOffsetPixels();
            int target = pixels <= 0.01f
                ? 0
                : Mathf.Clamp(Mathf.CeilToInt(pixels / cellH - 0.0001f), 1, MaxScrollLines);
            if (_historyTopOff >= 0 && target > _historyTopOff)
            {
                target = _historyTopOff;
                JumpHistoryTo(target);
            }

            int previous = _scrollOff;
            bool up = pixels > _historyLastPixels + 0.01f ? true
                : pixels < _historyLastPixels - 0.01f ? false
                : target >= previous;
            _historyLastPixels = pixels;
            bool fromLive = previous <= 0 && target > 0;
            if (fromLive)
            {
                _history.Reset(live);
                _historyRequests.Clear();
                _historyDisplayedFrame = live?.Snapshot();
                _historyTopOff = -1;
            }

            if (target == 0)
            {
                if (previous > 0)
                {
                    _history.Reset();
                    _historyRequests.Clear();
                    _historyDisplayedFrame = null;
                    _historyTopOff = -1;
                }
                _scrollOff = 0;
                return;
            }

            _scrollOff = target;

            // Prefetch half a viewport in the gesture direction. Every daemon reply overlaps
            // the preceding window, so TerminalHistory can serve all intervening line offsets
            // locally instead of requiring one websocket round trip per row.
            int rows = Mathf.Max(2, live?.Rows ?? (_rows > 0 ? _rows : 24));
            int lookahead = Mathf.Max(2, rows / 2);
            int probe = up ? Mathf.Min(MaxScrollLines, target + lookahead) : target;
            if (_historyTopOff >= 0) probe = Mathf.Min(probe, _historyTopOff);
            bool fractional = Mathf.Abs(pixels / cellH - Mathf.Round(pixels / cellH)) > 0.0001f;
            int request = -1;
            if (!_history.Covers(target, fractional))
            {
                // Near the live edge, one lookahead frame overlaps live and covers both jobs.
                // A larger leap needs its exact viewport first; if that viewport is present but
                // lacks the fractional edge row, fetch a newer bridge into the cached range.
                if (fromLive && target <= lookahead) request = probe;
                else if (!_history.Covers(target, false)) request = target;
                else request = Mathf.Max(1, target - lookahead);
            }
            else if (!_history.Covers(probe, false))
                request = probe;

            if (request > 0 && !HistoryRequestPending(request))
                QueueScroll(up, fromLive, request);
        }

        bool HistoryRequestPending(int off)
        {
            foreach (var requested in _historyRequests.Values)
                if (requested == off) return true;
            return _scrollPending && _wantedScrollOff == off;
        }

        float HistoryShift(ScreenBuf buf, float cellH)
        {
            if (buf == null || !_historyViewReady || !_historyScrollReady ||
                cellH <= 0.01f) return 0f;
            float shift = HistoryOffsetPixels() - buf.Off * cellH;
            return Mathf.Abs(shift) < cellH ? shift : 0f;
        }

        float DisplayedHistoryShift(float cellH) =>
            !_historyScrollReady || cellH <= 0.01f ? 0f : _renderHistoryShift;

        void JumpHistoryTo(int off)
        {
            _historyJumpPending = true;
            _historyJumpOff = Mathf.Max(0, off);
            _historyJumpPixels = -1f;
        }

        // The daemon's own limits, so what we ask for is always something it can answer with.
        const int MinCols = 20, MaxCols = 500, MinRows = 5, MaxRows = 200;

        void PrimeCachedSize()
        {
            if (_cols <= 0 && _cachedCols > 0 && _cachedRows > 0)
            {
                _cols = _cachedCols;
                _rows = _cachedRows;
            }

            // A pending geometry change still needs its debounce; otherwise a new session
            // would get both the old request and this one.
            if (_name == null || _sizeDirty || _cols <= 0 || !SessionHub.Instance.Online) return;
            SessionHub.Instance.Resize(_name, _cols, _rows);
        }

        // A loop rather than a statement: a resize is one fire-and-forget message over a
        // socket that may be down, and the daemon answers a size it already holds with a
        // no-op. The frame carries the emulator's dimensions, so that closes the loop.
        void NegotiateSize(Rect body, ScreenBuf buf)
        {
            // The getter builds or refreshes the font and, as part of that, measures the
            // cells. Reading CellW/CellH first sees zero on the first pane and stale values
            // after a font setting changes.
            var style = TerminalFont.Style;
            SyncSnap();
            float cw = DisplayCellW();
            if (cw <= 0.01f) return;

            int cols = Mathf.Clamp(
                Mathf.FloorToInt(body.width / cw), MinCols, MaxCols);
            int rows = Mathf.Clamp(
                Mathf.FloorToInt(body.height / TerminalFont.CellH), MinRows, MaxRows);

            if (cols != _cols || rows != _rows)
            {
                _cols = cols;
                _rows = rows;
                _cachedCols = cols;
                _cachedRows = rows;
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

            if (_wantedScrollOff <= 0) return;

            _sentScrollOff = _wantedScrollOff;
            _nextScrollSend = Time.realtimeSinceStartup + ScrollBeat;

            ulong id = ++_scrollRequestId;
            _historyRequests[id] = _sentScrollOff;
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

            if (!_sizeDirty || now < _resizeAt) return;

            // A socket that is down drops the message, so hold the ask rather than spend it.
            if (!SessionHub.Instance.Online)
            {
                _resizeAt = now + 1f;
                return;
            }

            SessionHub.Instance.Resize(_name, _cols, _rows);
            _sizeDirty = false;
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current.type != EventType.Repaint) return;

            // UI.screenWidth truncates, so the window is a fraction of a pixel short of the
            // right edge and nothing covers or repaints the last column, PaneOverDraw having
            // stood the map down. Here rather than by widening the window, which would be a
            // wider pane.
            Widgets.DrawBoxSolid(new Rect(0f, 0f,
                Mathf.Ceil(Screen.width / Prefs.UIScale),
                Mathf.Ceil(Screen.height / Prefs.UIScale)), Background);

            Flush();
        }
    }
}
