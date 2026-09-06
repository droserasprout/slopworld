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
        const float ContentPad = UiWidgets.GapS;
        FieldLifetime _fieldLifetime = new FieldLifetime();
        readonly TerminalInputController _input;
        readonly TerminalSelectionInput _selectionInput;

        internal TerminalSelectionInput SelectionInput => _selectionInput;

        TerminalWindow(string name)
        {
            _name = name;
            ResetCursorBlink();
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
                Rect body = DrawTopBar(rect, input);
                bool pane = DrawBody(body, input, hub);
                DrawStatus(body, hub, pane);
                if (_content == null && _showStopped && _name != null && hub.Get(_name)?.Gone == true)
                    MapGizmoUtility.MapUIOnGUI();
            }
        }

        Rect DrawTopBar(Rect rect, bool input)
        {
            // All of this after the background fill: anywhere earlier in the frame it is
            // painted over. See ColonistBarStrip.cs.
            TopBar.Draw(input);
            ColonistBarStrip.Draw(input);

            float top = TopBar.H;
            float left = UiLayout.LeftInset;
            float pad = _content == null ? 0f : ContentPad;
            return new Rect(
                rect.x + left + pad,
                top + pad,
                rect.width - left - pad * 2,
                rect.height - top - pad * 2);
        }

        bool DrawBody(Rect body, bool input, SessionHub hub)
        {
            // A view in the body is the whole of what the window is for while it is up: the
            // chrome's own keys are still read - F1, F12, Alt+Num, Alt+Z/Alt+X, Escape back
            // out of it -
            // but nothing is forwarded to an agent nobody is looking at.
            if (_content != null)
            {
                if (input) _input.HandleChrome(Event.current);
                _content.Draw(body);
                return false;
            }

            if (_showStopped && hub.Get(_name)?.Gone == true)
            {
                if (input) _input.HandleChrome(Event.current);
                Slab.Fill(OverdrawBackground(body), SolidTerminalBackground);
                DrawCentered(body, "Agent is stopped");
                return false;
            }

            if (input)
            {
                _input.CaptureSemicolonInput();
                _input.Handle(body);
            }

            SyncHistoryConnection();
            var live = hub.Screen(_name);
            // Invalidate history before planning this frame's request. A resize or redraw
            // can publish a new live sequence while the local scroll offset remains active.
            NoteLiveFrame(live);
            var style = TerminalFont.Style;
            float cellH = TerminalFont.CellH;
            bool historyInput = HistoryInputEnabled(live);
            PrepareHistoryScroll(cellH);
            if (input && historyInput)
            {
                _historyScroll.BeginInput(body, new Vector2(0f, _historyMax));
                // This pane has no nested scroll owner. Spend the claimed packet before
                // choosing a history window so the request and repaint both see this event's
                // position instead of trailing the touchpad by one IMGUI pass.
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
            DrawHistoryBar(body);
            return true;
        }

        void DrawStatus(Rect body, SessionHub hub, bool pane)
        {
            if (pane)
            {
                if (_scrollOff > 0) DrawScrollHint(body);
                if (!hub.Online) DrawOfflineBanner(body);
                else _droppedKeys = 0;
            }

            // The pane is opaque; a hint drawn from the map layer is behind it.
            DrawHint();
        }
    }
}
