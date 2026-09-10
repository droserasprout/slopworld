using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Pane geometry measurement and debounced daemon resize negotiation.
    public partial class TerminalWindow
    {
        int _cols
        {
            get => _state.Cols;
            set => _state.Cols = value;
        }
        int _rows
        {
            get => _state.Rows;
            set => _state.Rows = value;
        }
        float _resizeAt
        {
            get => _state.ResizeAt;
            set => _state.ResizeAt = value;
        }
        bool _sizeDirty
        {
            get => _state.SizeDirty;
            set => _state.SizeDirty = value;
        }

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

        void PrimePanelSize()
        {
            // New sessions use the intended slot immediately, even before their first frame.
            // Do not borrow another panel's most recently rendered dimensions.
            ArrangeTerminal(WorkspaceLayout.Current.Content);
            var style = TerminalFont.Style;
            if (style == null || !TerminalPanelGeometry.TryMeasure(_terminal.Bounds,
                    TerminalFont.CellWAtScreenScale(Prefs.UIScale), TerminalFont.CellH,
                    out int cols, out int rows)) return;
            if (_sizeDirty) return;
            _cols = cols;
            _rows = rows;
            if (_name != null && SessionHub.Instance.Online)
                SessionHub.Instance.Terminal.Resize(_name, _cols, _rows);
        }

        // A loop rather than a statement: a resize is one fire-and-forget message over a
        // socket that may be down, and the daemon answers a size it already holds with a
        // no-op. The frame carries the emulator's dimensions, so that closes the loop.
        void NegotiateSize(Rect body, ScreenBuf buf)
        {
            // The getter builds or refreshes the font and, as part of that, measures the
            // cells. Reading CellW/CellH first sees zero on the first pane and stale values
            // after a font setting changes.
            var style = TerminalFont.Style;
            SyncSnap();
            float cw = DisplayCellW();
            if (!TerminalPanelGeometry.TryMeasure(_terminal.Bounds, cw, TerminalFont.CellH,
                    out int cols, out int rows)) return;

            if (cols != _cols || rows != _rows)
            {
                _cols = cols;
                _rows = rows;
                // Debounce: dragging the game window otherwise spams SIGWINCH, and Claude Code
                // redraws its whole TUI on every one.
                _resizeAt = Time.realtimeSinceStartup + 0.2f;
                _sizeDirty = true;
                return;
            }

            // A pane not that shape means the last ask did not land. A scrolled frame is
            // history and says nothing about the live pane.
            if (_sizeDirty || buf.Off > 0) return;
            if (buf.Cols == cols && buf.Rows == rows) return;

            _resizeAt = Time.realtimeSinceStartup + 1f;
            _sizeDirty = true;
        }
    }
}
