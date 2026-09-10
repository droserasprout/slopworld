using UnityEngine;

namespace SlopWorld
{
    // The panel owns session/input state and placement; the window still supplies the
    // rendering and history services while it also hosts the workspace chrome.
    sealed class TerminalPanel : ContentView
    {
        readonly TerminalWindow _window;
        public readonly TerminalWindowState State = new TerminalWindowState();
        public bool DrewScreen { get; private set; }

        public TerminalPanel(TerminalWindow window) { _window = window; }
        public override string Title => State.Name ?? "Terminal";
        public override PanelSize MinimumSize => new PanelSize(160f, 80f);

        public override void Draw(Rect body)
        {
            DrewScreen = _window.DrawTerminalBody(body, Focused);
        }

        public override void FocusChanged(bool focused)
        {
            if (Focused && !focused) _window.ReleasePanelInput();
            base.FocusChanged(focused);
        }
    }
}
