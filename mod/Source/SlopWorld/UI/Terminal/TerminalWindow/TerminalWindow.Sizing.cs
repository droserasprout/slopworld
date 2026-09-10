using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Pane geometry measurement and debounced daemon resize negotiation.
    public partial class TerminalWindow
    {
        // Retain pane geometry while a content view covers the workspace.
        void ArrangeTerminal(Rect body) =>
            _terminals.Arrange(new UiLayoutRect(body.x, body.y, body.width, body.height));

        internal static void RefreshPanels()
        {
            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (window != null && window._terminals.Split)
            {
                window.ArrangeTerminal(WorkspaceLayout.Current.Content);
                window._terminals.First.RefreshSize();
                window._terminals.Second.RefreshSize();
                SessionHub.Instance.Terminal.RefreshPanels();
            }
            else if (TryPanelShape(out int cols, out int rows))
                SessionHub.Instance.Terminal.RefreshPanels(cols, rows);
            else SessionHub.Instance.Terminal.RefreshPanels();
        }

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
