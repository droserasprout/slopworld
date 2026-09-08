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

        // Every terminal window is fullscreen and shares the same pane geometry. Keep the
        // last measured shape outside the window instance so a fresh pager can start there
        // instead of drawing once at slopd's boot size and making less redraw on the first
        // resize.
        static int _cachedCols
        {
            get => TerminalWindowState.CachedCols;
            set => TerminalWindowState.CachedCols = value;
        }
        static int _cachedRows
        {
            get => TerminalWindowState.CachedRows;
            set => TerminalWindowState.CachedRows = value;
        }

        // The daemon's own limits, so what we ask for is always something it can answer with.
        const int MinCols = WireContract.TerminalMinCols;
        const int MaxCols = WireContract.TerminalMaxCols;
        const int MinRows = WireContract.TerminalMinRows;
        const int MaxRows = WireContract.TerminalMaxRows;

        // Sidebar changes happen outside the terminal window, so there may be no instance from
        // which to reuse NegotiateSize. Compute the fullscreen pane's shape directly and attach
        // it to the background redraw request; otherwise an inactive tab keeps its old tmux
        // size until its first activation.
        internal static bool TryPanelShape(out int cols, out int rows)
        {
            cols = rows = 0;
            if (!UiLayout.Shown || UI.screenWidth <= 0 || UI.screenHeight <= 0) return false;

            var style = TerminalFont.Style;
            if (style == null || TerminalFont.CellH <= 0.01f) return false;

            float width = UI.screenWidth - UiLayout.LeftInset;
            float height = UI.screenHeight - TopBar.H;
            float cw = TerminalFont.CellWAtScreenScale(Prefs.UIScale);
            if (width <= 0.01f || height <= 0.01f || cw <= 0.01f) return false;

            cols = Mathf.Clamp(Mathf.FloorToInt(width / cw), MinCols, MaxCols);
            rows = Mathf.Clamp(Mathf.FloorToInt(height / TerminalFont.CellH), MinRows, MaxRows);
            return true;
        }

        void PrimeCachedSize()
        {
            if (_cols <= 0 && _cachedCols > 0 && _cachedRows > 0)
            {
                _cols = _cachedCols;
                _rows = _cachedRows;
            }

            // A pending geometry change still needs its debounce; otherwise a new session
            // would get both the old request and this one.
            if (_name == null || _sizeDirty || _cols <= 0 || !SessionHub.Instance.Online) return;
            SessionHub.Instance.Resize(_name, _cols, _rows);
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
            if (cw <= 0.01f) return;

            int cols = Mathf.Clamp(
                Mathf.FloorToInt(body.width / cw), MinCols, MaxCols);
            int rows = Mathf.Clamp(
                Mathf.FloorToInt(body.height / TerminalFont.CellH), MinRows, MaxRows);

            if (cols != _cols || rows != _rows)
            {
                _cols = cols;
                _rows = rows;
                _cachedCols = cols;
                _cachedRows = rows;
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
