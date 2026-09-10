using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Fullscreen view of one agent's pane: renders what slopd captured, forwards
    // keystrokes back as tmux keys.
    public partial class TerminalWindow : Window
    {
        static float ContentPad => UiWidgets.GapS;
        FieldLifetime _fieldLifetime = new FieldLifetime();
        readonly TerminalInputController _input;
        readonly TerminalSelectionInput _selectionInput;
        readonly TerminalSelectionCoordinator _selectionCoordinator;
        readonly TerminalRenderer _renderer;
        readonly TerminalHistoryCoordinator _historyCoordinator;

        internal TerminalSelectionInput SelectionInput => _selectionInput;

        TerminalWindow(string name)
        {
            _terminal = new TerminalPanel(this);
            _name = name;
            ResetCursorBlink();
            _selectionCoordinator = new TerminalSelectionCoordinator(this);
            _renderer = new TerminalRenderer(this);
            _historyCoordinator = new TerminalHistoryCoordinator(this);
            _input = new TerminalInputController(this);
            _selectionInput = new TerminalSelectionInput(this);
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

        // These static entry points are used by map-layer patches as well as the window's
        // input path. Keep the public facade on TerminalWindow while the event policy lives in
        // TerminalInputController.
        public static bool HandleFunctionKey(Event e) =>
            TerminalInputController.HandleFunctionKey(e);

        internal static bool TryTabWalkDirection(Event e, out int dir) =>
            TerminalInputController.TryTabWalkDirection(e, out dir);

        internal static bool IsSemicolonKey(KeyCode key) =>
            TerminalInputController.IsSemicolonKey(key);

        internal static void WalkSession(int dir) => TerminalInputController.WalkSession(dir);

        internal static List<string> TabOrder() => TerminalInputController.TabOrder();

        internal static EventType MouseType(Event e) => TerminalInputController.MouseType(e);

        // Window.InnerWindowOnGUI opens a GUI group on the contracted rect, translating
        // everything drawn here by the margin without moving GUI.matrix or mousePosition with
        // it, so anything working in screen coordinates lands 18px off.
        protected override float Margin => 0f;

        protected override void SetInitialSizeAndPosition() =>
            windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);

        public override void DoWindowContents(Rect rect)
        {
            using (FieldLifetimeScope.Push(_fieldLifetime))
            {
                var hub = SessionHub.Instance;
                Slab.Fill(OverdrawBackground(rect), Background);
                if (!EnsureSession(hub)) return;

                bool input = Find.WindowStack == null || Find.WindowStack.GetsInput(this);
                _panels.SetFocus(input);
                Rect body = DrawTopBar(rect, input);
                bool pane = DrawBody(body, input, hub);
                DrawStatus(body, hub, pane);
                if (TerminalVisible && _showStopped && _name != null && hub.Get(_name)?.Gone == true)
                    MapGizmoUtility.MapUIOnGUI();
            }
        }

        Rect DrawTopBar(Rect rect, bool input)
        {
            // All of this after the background fill: anywhere earlier in the frame it is
            // painted over. See ColonistBarStrip.cs.
            TopBar.Draw(input);
            ColonistBarStrip.Draw(input);

            var workspace = WorkspaceLayout.Current;
            ArrangeTerminal(workspace.Content);
            float pad = TerminalVisible ? 0f : ContentPad;
            var body = workspace.Content;
            return new Rect(body.x + pad, body.y + pad,
                Mathf.Max(0f, body.width - pad * 2f),
                Mathf.Max(0f, body.height - pad * 2f));
        }

        bool DrawBody(Rect body, bool input, SessionHub hub)
        {
            var active = _panels.Active;
            if (active == null) return false;
            if (!TerminalVisible && input) _input.HandleChrome(Event.current);
            // Chrome input may close or replace the panel. Do not deliver the same event
            // to its replacement using bounds computed for the old panel.
            if (!ReferenceEquals(active, _panels.Active)) return false;
            _panels.Arrange(new UiLayoutRect(body.x, body.y, body.width, body.height));
            if (TerminalVisible || active is OptionsView) active.Draw(body);
            else
                using (new FieldFocusScope(_fieldLifetime, input && !ModOptions.KeyboardCaptureActive))
                    active.Draw(body);
            return TerminalVisible && _terminal.DrewScreen;
        }

        internal bool DrawTerminalBody(Rect body, bool input)
        {
            var hub = SessionHub.Instance;

            if (_showStopped && hub.Get(_name)?.Gone == true)
            {
                if (input) _input.HandleChrome(Event.current);
                Slab.Fill(OverdrawBackground(body), SolidTerminalBackground);
                DrawCentered(body, "Agent is stopped");
                return false;
            }

            SyncHistoryConnection();
            var live = hub.Screen(_name);
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

            if (input) UpdateSelectionEdgeScroll(body, historyInput);

            UpdateHistoryTarget(cellH, live);
            var buf = DisplayedScreen();
            if (buf == null || buf.Lines == null || buf.Lines.Length == 0)
            {
                // A tab may have been visited before but have no current live frame while
                // its subscription is being restored. Use that tab's own last frame rather
                // than the shared render texture, which still belongs to the old tab.
                var cached = CachedDisplayedFrame(_name);
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

            RememberDisplayedFrame(_name, buf);

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
                else _droppedKeys = 0;
                if (HistoryBarAvailable()) DrawScrollLock(body);
            }

            // The pane is opaque; a hint drawn from the map layer is behind it.
            DrawHint();
        }
    }
}
