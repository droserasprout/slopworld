using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Pane geometry measurement and debounced daemon resize negotiation.
    public partial class TerminalWindow
    {
        // The current host places one terminal in the workspace content slot. Retaining
        // this assignment on the panel also sizes its session while a content view covers it.
        void ArrangeTerminal(Rect body) =>
            _terminal.Arrange(new UiLayoutRect(body.x, body.y, body.width, body.height));

        internal static bool TryPanelShape(out int cols, out int rows)
        {
            cols = rows = 0;
            if (!UiLayout.Shown || UI.screenWidth <= 0 || UI.screenHeight <= 0) return false;
            var style = TerminalFont.Style;
            if (style == null) return false;
            var content = WorkspaceLayout.Current.Content;
            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            UiLayoutRect bounds;
            if (window != null)
            {
                window.ArrangeTerminal(content);
                bounds = window._terminal.Bounds;
            }
            else bounds = new UiLayoutRect(content.x, content.y, content.width, content.height);
            return TerminalPanelGeometry.TryMeasure(bounds,
                TerminalFont.CellWAtScreenScale(Prefs.UIScale), TerminalFont.CellH, out cols, out rows);
        }

    }
}
