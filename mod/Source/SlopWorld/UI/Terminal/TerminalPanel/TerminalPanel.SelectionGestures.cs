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
            if (!_dragging || !_selectionMoved || !Input.GetMouseButton(0))
            {
                _selectionEdgeDirection = 0;
                return;
            }

            var e = Event.current;
            if (e != null && e.type == EventType.Repaint)
                _selectionMouse = e.mousePosition;

            float distance;
            int direction;
            if (_selectionMouse.y < body.y + SelectionEdgeBand)
            {
                direction = 1;
                distance = body.y + SelectionEdgeBand - _selectionMouse.y;
            }
            else if (_selectionMouse.y > body.yMax - SelectionEdgeBand)
            {
                direction = -1;
                distance = _selectionMouse.y - (body.yMax - SelectionEdgeBand);
            }
            else
            {
                _selectionEdgeDirection = 0;
                return;
            }

            _selectionEdgeDirection = direction;
            if (!historyInput || _selectionEdgeFrame == Time.frameCount) return;
            _selectionEdgeFrame = Time.frameCount;

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
            if (!_dragging || _selectionEdgeDirection == 0 || buf == null ||
                buf.Runs == null || buf.Runs.Length == 0)
                return;

            var cell = CellAt(body, _selectionMouse);
            cell.y = _selectionEdgeDirection > 0 ? 0 : buf.Runs.Length - 1;
            _selectionMoved = true;
            if (_lineDragging) SelectLineRange(_lineStart, cell.y);
            else if (_wordDragging) UpdateWordSelection(cell);
            else
            {
                _selB = cell;
                _hasSel = true;
            }
        }

        internal void CaptureSelection(Rect body)
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = GUIUtility.GetControlID(FocusType.Passive, body);
            GUIUtility.hotControl = _selectionControl;
        }

        internal void ReleaseSelection()
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = 0;
        }

        // Both ends inclusive, the way a dragged selection states them.
        void SelectSpan(int row, int c0, int c1)
        {
            _selA = new Vector2Int(c0, row);
            _selB = new Vector2Int(c1, row);
            _hasSel = true;
            _dragging = true;
            _selectionMoved = false;
            _multiClickSelection = true;
            _wordDragging = false;
            _lineDragging = true;
            _lineStart = row;
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

            char anchor = TerminalColumns.Glyph(cells, cell.x);
            bool word = IsWordChar(anchor);
            int c0 = cell.x, c1 = cell.x;
            while (c0 > 0 && SameClass(TerminalColumns.Glyph(cells, c0 - 1), anchor, word)) c0--;
            while (c1 + 1 < len && SameClass(TerminalColumns.Glyph(cells, c1 + 1), anchor, word)) c1++;
            _wordStart = new Vector2Int(c0, cell.y);
            _wordEnd = new Vector2Int(c1, cell.y);
            _selA = _wordStart;
            _selB = _wordEnd;
            _hasSel = true;
            _dragging = true;
            _selectionMoved = false;
            _multiClickSelection = true;
            _wordDragging = true;
            _lineDragging = false;
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
            char anchor = TerminalColumns.Glyph(cells, x);
            bool word = IsWordChar(anchor);
            int c0 = x, c1 = x;
            while (c0 > 0 && SameClass(TerminalColumns.Glyph(cells, c0 - 1), anchor, word)) c0--;
            while (c1 + 1 < len && SameClass(TerminalColumns.Glyph(cells, c1 + 1), anchor, word)) c1++;

            var destinationStart = new Vector2Int(c0, cell.y);
            var destinationEnd = new Vector2Int(c1, cell.y);
            if (Before(cell, _wordStart))
            {
                _selA = destinationStart;
                _selB = _wordEnd;
            }
            else
            {
                _selA = _wordStart;
                _selB = destinationEnd;
            }
            _hasSel = true;
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
            _selA = new Vector2Int(0, first);
            _selB = new Vector2Int(Mathf.Max(0, len - 1), last);
            _hasSel = true;
        }

        static bool SameClass(char c, char anchor, bool word) =>
            word ? IsWordChar(c) : c == anchor;

        static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_';
    }
}
