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

        TerminalWindow(string name)
        {
            _name = name;
            ResetCursorBlink();
            _input = new TerminalInputHandler(this);
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

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;
            Widgets.DrawBoxSolid(rect, Background);
            if (!EnsureSession(hub)) return;

            bool input = Find.WindowStack == null || Find.WindowStack.GetsInput(this);
            Rect body = DrawTopBar(rect, input);
            bool pane = DrawBody(body, input, hub);
            DrawStatus(body, hub, pane);
            if (_content == null && _showStopped && _name != null && hub.Get(_name)?.Gone == true)
                MapGizmoUtility.MapUIOnGUI();
        }

        Rect DrawTopBar(Rect rect, bool input)
        {
            // All of this after the background fill: anywhere earlier in the frame it is
            // painted over. See ColonistBarStrip.cs.
            TopBar.Draw(this, input);
            ColonistBarStrip.Draw(input);

            float top = TopBar.H;
            float left = SlopLayout.LeftInset;
            return new Rect(
                rect.x + left + Pad,
                top + Pad,
                rect.width - left - Pad * 2,
                rect.height - top - Pad * 2);
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
                Widgets.DrawBoxSolid(body, SolidTerminalBackground);
                DrawCentered(body, "Agent is stopped");
                return false;
            }

            if (input)
            {
                _input.CaptureSemicolonInput();
                _input.Handle(body);
            }

            var live = hub.Screen(_name);
            var style = TerminalFont.Style;
            float cellH = TerminalFont.CellH;
            bool historyInput = HistoryInputEnabled(live);
            PrepareHistoryScroll(cellH);
            if (historyInput)
            {
                _historyScroll.BeginInput(body, new Vector2(0f, _historyMax));
                // This pane has no nested scroll owner. Spend the claimed packet before
                // choosing a history window so the request and repaint both see this event's
                // position instead of trailing the touchpad by one IMGUI pass.
                _historyScroll.EndInput();
            }

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
