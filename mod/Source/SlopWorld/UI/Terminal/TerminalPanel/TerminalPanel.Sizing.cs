using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Pane geometry measurement and debounced daemon resize negotiation.
    sealed partial class TerminalPanel
    {
        internal void RefreshSize()
        {
            _state.SizeDirty = false;
            PrimePanelSize();
        }

        void PrimePanelSize()
        {
            // New sessions use the intended slot immediately, even before their first frame.
            // Do not borrow another panel's most recently rendered dimensions.
            var style = TerminalFont.Style;
            if (style == null || !TerminalPanelGeometry.TryMeasure(Bounds,
                    TerminalFont.CellWAtScreenScale(Prefs.UIScale), TerminalFont.CellH,
                    out int cols, out int rows)) return;
            if (_state.SizeDirty) return;
            _state.Cols = cols;
            _state.Rows = rows;
            if (_state.Name != null && SessionHub.Instance.Online)
                SessionHub.Instance.Terminal.Resize(_state.Name, _state.Cols, _state.Rows);
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
            if (!TerminalPanelGeometry.TryMeasure(Bounds, cw, TerminalFont.CellH,
                    out int cols, out int rows)) return;

            if (cols != _state.Cols || rows != _state.Rows)
            {
                _state.Cols = cols;
                _state.Rows = rows;
                // Debounce: dragging the game window otherwise spams SIGWINCH, and Claude Code
                // redraws its whole TUI on every one.
                _state.ResizeAt = Time.realtimeSinceStartup + 0.2f;
                _state.SizeDirty = true;
                return;
            }

            // A pane not that shape means the last ask did not land. A scrolled frame is
            // history and says nothing about the live pane.
            if (_state.SizeDirty || buf.Off > 0) return;
            if (buf.Cols == cols && buf.Rows == rows) return;

            _state.ResizeAt = Time.realtimeSinceStartup + 1f;
            _state.SizeDirty = true;
        }
    }
}
