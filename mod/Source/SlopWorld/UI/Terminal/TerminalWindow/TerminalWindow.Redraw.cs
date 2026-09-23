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

            // UI.screenWidth truncates the width and can leave the last pixel column uncovered.
            // PaneOverDraw prevents the map from repainting that column.
            // Fill it here instead of widening the pane.
            Slab.Fill(TerminalPanel.OverdrawBackground(new Rect(0f, 0f,
                Mathf.Ceil(Screen.width / Prefs.UIScale),
                Mathf.Ceil(Screen.height / Prefs.UIScale))), Background);

            _terminals.Flush();
        }
    }
}
