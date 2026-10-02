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

            // Logical viewport dimensions can truncate the last row or column.
            // Cover those trailing pixels without widening the pane.
            Slab.Fill(TerminalPanel.OverdrawBackground(new Rect(0f, 0f,
                Mathf.Ceil(Screen.width / Prefs.UIScale),
                Mathf.Ceil(Screen.height / Prefs.UIScale))), Background);

            _terminals.Flush();
        }
    }
}
