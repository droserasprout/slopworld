using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Fullscreen workspace host. TerminalPanel owns terminal rendering and input;
    // this window places the active panel and draws shared chrome.
    public partial class TerminalWindow : Window, ITerminalPanelHost
    {
        static float ContentPad => UiTheme.GapS;
        FieldLifetime _fieldLifetime = new FieldLifetime();
        TerminalWindow(string name)
        {
            _terminals = new TerminalSplit(this, name, nameSelected =>
            {
                SessionSelectable.Current = nameSelected;
                if (nameSelected != null) SelectAgent(nameSelected);
            });
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

        internal static List<string> OpenBesideOrder() =>
            TerminalInputController.OpenBesideOrder();

        internal static EventType MouseType(Event e) => TerminalInputController.MouseType(e);

        // Window.InnerWindowOnGUI opens a GUI group on the contracted rect, translating
        // everything drawn here by the margin without moving GUI.matrix or mousePosition with
        // it, so anything working in screen coordinates lands 18px off.
        protected override float Margin => 0f;

        protected override void SetInitialSizeAndPosition() =>
            windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);

        public override void DoWindowContents(Rect rect)
        {
            long started = PerfTrace.Start();
            try
            {
                using (FieldLifetimeScope.Push(_fieldLifetime))
                {
                    var hub = SessionHub.Instance;
                    Slab.Fill(TerminalPanel.OverdrawBackground(rect), Background);
                    if (!EnsureSession(hub)) return;

                    bool input = Find.WindowStack == null || Find.WindowStack.GetsInput(this);
                    _panels.SetFocus(input);
                    Rect body = DrawTopBar(rect, input);
                    DrawBody(body, input, hub);
                    Find.CurrentMap?.GetComponent<CoreTip>()?.DrawHint();
                    if (TerminalVisible && _showStopped && _name != null && hub.Get(_name)?.Gone == true)
                        MapGizmoUtility.MapUIOnGUI();
                }
            }
            finally
            {
                PerfTrace.End("terminal-window", started, 1);
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
            if (!TerminalVisible && input) _terminal.HandleChrome(Event.current);
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

    }
}
