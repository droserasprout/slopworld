using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Mouse-selection gestures for TerminalPanel.
    sealed partial class TerminalPanel
    {
        const float SelectionEdgeBand = 32f;
        const float SelectionEdgeRowsPerSecond = 13f;

        // The history scroll position is local, so a held drag can advance it without waiting
        // for a daemon reply. The next draw pass requests/assembles the corresponding rows.
        // Keep this separate from ContinueSelection: Unity may stop delivering MouseDrag once
        // the pointer leaves the window, while the held-button sample still remains reliable.
        void UpdateSelectionEdgeScroll(Rect body, bool historyInput)
        {
            if (!_state.Selection.Dragging || !_state.Selection.SelectionMoved ||
                !Input.GetMouseButton(0))
            {
                _state.Selection.EdgeDirection = 0;
                return;
            }

            var e = Event.current;
            if (e != null && e.type == EventType.Repaint)
                _state.Selection.Mouse = e.mousePosition;

            float distance;
            int direction;
            if (_state.Selection.Mouse.y < body.y + SelectionEdgeBand)
            {
                direction = 1;
                distance = body.y + SelectionEdgeBand - _state.Selection.Mouse.y;
            }
            else if (_state.Selection.Mouse.y > body.yMax - SelectionEdgeBand)
            {
                direction = -1;
                distance = _state.Selection.Mouse.y - (body.yMax - SelectionEdgeBand);
            }
            else
            {
                _state.Selection.EdgeDirection = 0;
                return;
            }

            _state.Selection.EdgeDirection = direction;
            if (!historyInput || _state.Selection.EdgeFrame == Time.frameCount) return;
            _state.Selection.EdgeFrame = Time.frameCount;

            float cellH = TerminalFont.CellH;
            if (!_historyScrollReady || cellH <= 0.01f) return;

            float maxOffset = (_historyTopOff >= 0 ? _historyTopOff : MaxScrollLines) * cellH;
            float current = HistoryOffsetPixels();
            float strength = Mathf.Clamp01(distance / SelectionEdgeBand);
            float amount = SelectionEdgeRowsPerSecond * cellH * strength *
                Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            float target = Mathf.Clamp(current + direction * amount, 0f, maxOffset);
            _historyScroll.JumpTo(new Vector2(0f, _historyMax - target));
        }

        // Scrolling the viewport normally translates both endpoints to keep the selected text
        // fixed. During an edge drag the active endpoint is the exception: it must stay on the
        // edge, where each newly revealed row extends the selection.
        void ExtendSelectionToEdge(Rect body, ScreenBuf buf)
        {
            if (!_state.Selection.Dragging || _state.Selection.EdgeDirection == 0 ||
                buf == null ||
                buf.Runs == null || buf.Runs.Length == 0)
                return;

            var cell = CellAt(body, _state.Selection.Mouse);
            cell.y = _state.Selection.EdgeDirection > 0 ? 0 : buf.Runs.Length - 1;
            _state.Selection.SelectionMoved = true;
            if (_state.Selection.LineDragging)
                SelectLineRange(_state.Selection.LineStart, cell.y);
            else if (_state.Selection.WordDragging) UpdateWordSelection(cell);
            else
            {
                _state.Selection.B = cell;
                _state.Selection.HasSelection = true;
            }
        }

        internal void CaptureSelection(Rect body)
        {
            if (_state.Selection.Control != 0 &&
                GUIUtility.hotControl == _state.Selection.Control)
                GUIUtility.hotControl = 0;
            _state.Selection.Control = GUIUtility.GetControlID(FocusType.Passive, body);
            GUIUtility.hotControl = _state.Selection.Control;
        }

        internal void ReleaseSelection()
        {
            if (_state.Selection.Control != 0 &&
                GUIUtility.hotControl == _state.Selection.Control)
                GUIUtility.hotControl = 0;
            _state.Selection.Control = 0;
        }

        // Both ends inclusive, the way a dragged selection states them.
        void SelectSpan(int row, int c0, int c1)
        {
            _state.Selection.A = new Vector2Int(c0, row);
            _state.Selection.B = new Vector2Int(c1, row);
            _state.Selection.HasSelection = true;
            _state.Selection.Dragging = true;
            _state.Selection.SelectionMoved = false;
            _state.Selection.MultiClickSelection = true;
            _state.Selection.WordDragging = false;
            _state.Selection.LineDragging = true;
            _state.Selection.LineStart = row;
        }

        // A word, or the run of identical characters a non-word cell sits in.
        internal void DoubleClickSelect(Vector2Int cell)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return;

            var cells = TerminalColumns.Cells(buf.Runs[cell.y]);
            int len = TerminalColumns.ContentColumns(cells);
            if (cell.x < 0 || cell.x >= len) { ClearSelection(); return; }

            TerminalColumns.WordRange(cells, cell.x, out int c0, out int c1);
            _state.Selection.WordStart = new Vector2Int(c0, cell.y);
            _state.Selection.WordEnd = new Vector2Int(c1, cell.y);
            _state.Selection.A = _state.Selection.WordStart;
            _state.Selection.B = _state.Selection.WordEnd;
            _state.Selection.HasSelection = true;
            _state.Selection.Dragging = true;
            _state.Selection.SelectionMoved = false;
            _state.Selection.MultiClickSelection = true;
            _state.Selection.WordDragging = true;
            _state.Selection.LineDragging = false;
            CopyPrimarySelection();
        }

        internal void UpdateWordSelection(Vector2Int cell)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (cell.y < 0 || cell.y >= buf.Runs.Length) return;

            var cells = TerminalColumns.Cells(buf.Runs[cell.y]);
            int len = TerminalColumns.ContentColumns(cells);
            if (len == 0) return;
            int x = Mathf.Clamp(cell.x, 0, len - 1);
            TerminalColumns.WordRange(cells, x, out int c0, out int c1);

            var destinationStart = new Vector2Int(c0, cell.y);
            var destinationEnd = new Vector2Int(c1, cell.y);
            if (Before(cell, _state.Selection.WordStart))
            {
                _state.Selection.A = destinationStart;
                _state.Selection.B = _state.Selection.WordEnd;
            }
            else
            {
                _state.Selection.A = _state.Selection.WordStart;
                _state.Selection.B = destinationEnd;
            }
            _state.Selection.HasSelection = true;
        }

        static bool Before(Vector2Int a, Vector2Int b) =>
            a.y < b.y || (a.y == b.y && a.x < b.x);

        // The row, not the logical line: the daemon does not mark where one wrapped.
        internal void TripleClickSelect(int row)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (row < 0 || row >= buf.Runs.Length) return;

            int len = TerminalColumns.ContentColumns(TerminalColumns.Cells(buf.Runs[row]));
            if (len == 0) { ClearSelection(); return; }
            SelectSpan(row, 0, len - 1);
            CopyPrimarySelection();
        }

        internal void SelectLineRange(int anchor, int row)
        {
            var buf = DisplayedBuf();
            if (buf == null) return;
            EnsureRuns(buf);
            if (buf.Runs.Length == 0) return;

            anchor = Mathf.Clamp(anchor, 0, buf.Runs.Length - 1);
            row = Mathf.Clamp(row, 0, buf.Runs.Length - 1);
            int first = Mathf.Min(anchor, row);
            int last = Mathf.Max(anchor, row);
            int len = TerminalColumns.ContentColumns(TerminalColumns.Cells(buf.Runs[last]));
            _state.Selection.A = new Vector2Int(0, first);
            _state.Selection.B = new Vector2Int(Mathf.Max(0, len - 1), last);
            _state.Selection.HasSelection = true;
        }

    }
}
