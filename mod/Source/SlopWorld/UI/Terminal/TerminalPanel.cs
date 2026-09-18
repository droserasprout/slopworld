using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Workspace actions stay on the host; terminal controllers only know this boundary.
    interface ITerminalPanelHost
    {
        IContentView Content { get; }
        bool InputAvailable { get; }
        void Leave();
        void ClosePanel(TerminalPanel panel);
        void SwitchTo(string name);
    }

    // Owns terminal state, rendering, history and input. The host only places this panel
    // and handles workspace navigation; resizing never replaces these service instances.
    sealed partial class TerminalPanel : ContentView
    {
        readonly ITerminalPanelHost _host;
        readonly TerminalPanelState _state = new TerminalPanelState();
        readonly TerminalInputController _input;
        readonly TerminalSelectionInput _selectionInput;
        readonly TerminalSelectionCoordinator _selectionCoordinator;
        readonly TerminalRenderer _renderer;
        readonly TerminalHistoryCoordinator _historyCoordinator;
        public bool DrewScreen { get; private set; }
        internal TerminalSelectionInput SelectionInput => _selectionInput;

        public TerminalPanel(ITerminalPanelHost host, string name)
        {
            _host = host;
            _state.Name = name;
            ResetCursorBlink();
            _selectionCoordinator = new TerminalSelectionCoordinator(this);
            _renderer = new TerminalRenderer(this);
            _historyCoordinator = new TerminalHistoryCoordinator(this);
            _input = new TerminalInputController(this);
            _selectionInput = new TerminalSelectionInput(this);
        }

        public override string Title => _state.Name ?? "Terminal";
        public override PanelSize MinimumSize => new PanelSize(160f, 80f);
        public override void Draw(Rect body)
        {
            DrewScreen = DrawTerminalBody(body, Focused);
            DrawStatus(body, SessionHub.Instance, DrewScreen);
        }
        public override void FocusChanged(bool focused)
        {
            if (Focused && !focused) ReleasePanelInput();
            base.FocusChanged(focused);
        }
        internal void HandleChrome(Event e) => _input.HandleChrome(e);
        internal void Leave() => _host.Leave();
        internal void Close() => _host.ClosePanel(this);
        internal void SwitchTo(string name) => _host.SwitchTo(name);
        internal static EventType MouseType(Event e) => TerminalInputController.MouseType(e);

        internal bool DrawTerminalBody(Rect body, bool input)
        {
            var hub = SessionHub.Instance;
            string drawingSession = _state.Name;

            if (_state.ShowStopped && hub.Get(_state.Name)?.Gone == true)
            {
                if (input) _input.HandleChrome(Event.current);
                if (!_opened || !Visible || _state.Name != drawingSession) return false;
                if (input && MouseType(Event.current) == EventType.MouseDown &&
                    Event.current.button == 1 && body.Contains(Event.current.mousePosition))
                {
                    OpenMenu(null);
                    Event.current.Use();
                }
                Slab.Fill(OverdrawBackground(body), SolidTerminalBackground);
                DrawCentered(body, "Agent is stopped");
                return false;
            }

            SyncHistoryConnection();
            var live = hub.Screen(_state.Name);
            // Invalidate history before planning this frame's request. A resize or redraw
            // can publish a new live sequence while the local scroll offset remains active.
            int restoredShift = RestoredHistoryShift(live);
            NoteLiveFrame(live, restoredShift);
            var style = TerminalFont.Style;
            float cellH = TerminalFont.CellH;
            bool historyInput = HistoryInputEnabled(live);
            PrepareHistoryScroll(cellH, live);
            bool historyGesture = historyInput || _historyBarDragging;
            if (input && historyGesture)
            {
                _historyScroll.BeginInput(body, new Vector2(0f, _historyMax));
            }

            if (input)
            {
                _input.CaptureSemicolonInput();
                _input.Handle(body);
            }

            if (input && historyGesture)
            {
                // This pane has no nested scroll owner. Spend the claimed packet after the
                // input controller has had a chance to claim the scrollbar drag, so the
                // request and repaint both see this event's position.
                _historyScroll.EndInput();
            }

            // Navigation can close this panel or change its session during input. Do not
            // repaint a released cache or use the old session's frame after that transition.
            if (!_opened || !Visible || _state.Name != drawingSession) return false;

            if (input) UpdateSelectionEdgeScroll(body, historyInput);

            UpdateHistoryTarget(cellH, live);
            var buf = DisplayedScreen();
            if (buf == null || buf.Lines == null || buf.Lines.Length == 0)
            {
                // A tab may have been visited before but have no current live frame while
                // its subscription is being restored. Use that tab's own last frame rather
                // than the shared render texture, which still belongs to the old tab.
                var cached = CachedDisplayedFrame(_state.Name);
                if (cached != null && cached.Lines != null && cached.Lines.Length > 0)
                    buf = cached;
            }
            if (buf == null)
            {
                // Switching a pager keeps this window alive, but the new session needs a
                // round trip before it has a screen. Keep the last pane frame over that gap.
                if (BlitCached(body)) return true;
                DrawCentered(body, hub.Online ? "Waiting for output..." : $"Daemon {hub.Status}");
                return false;
            }
            if (buf.Lines == null || buf.Lines.Length == 0)
            {
                DrawCentered(body, hub.Online ? "Waiting for output..." : $"Daemon {hub.Status}");
                return false;
            }

            RememberDisplayedFrame(_state.Name, buf);

            NegotiateSize(body, buf);
            float shift = HistoryShift(buf, cellH);
            _renderHistoryShift = shift;
            DrawScreen(body, buf, shift);
            ExtendSelectionToEdge(body, buf);
            DrawSelection(body, buf, shift);
            DrawHistoryBar(body, historyInput);
            return true;
        }

        void DrawStatus(Rect body, SessionHub hub, bool pane)
        {
            if (pane)
            {
                if (!hub.Online) DrawOfflineBanner(body);
                else _state.DroppedKeys = 0;
                if (HistoryBarAvailable()) DrawScrollLock(body);
            }

        }
    }
}
