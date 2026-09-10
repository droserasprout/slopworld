using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class TerminalWindow
    {
        Color Background => TerminalVisible ? TerminalPanel.SolidTerminalBackground : UiTheme.WindowBg;
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            _terminals.Update();
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current.type != EventType.Repaint) return;

            // UI.screenWidth truncates, so the window is a fraction of a pixel short of the
            // right edge and nothing covers or repaints the last column, PaneOverDraw having
            // stood the map down. Here rather than by widening the window, which would be a
            // wider pane.
            Slab.Fill(TerminalPanel.OverdrawBackground(new Rect(0f, 0f,
                Mathf.Ceil(Screen.width / Prefs.UIScale),
                Mathf.Ceil(Screen.height / Prefs.UIScale))), Background);

            _terminals.Flush();
        }
    }
}
