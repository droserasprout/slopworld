using UnityEngine;

namespace SlopWorld
{
    sealed partial class TerminalPanel
    {
        // Selection gestures remain ordered by TerminalSelectionInput. This coordinator owns the
        // coordinate-space bookkeeping that gestures should not have to understand: live-frame
        // shifts, displayed-history offsets, cell translation, and clipboard text extraction.
        sealed class TerminalSelectionCoordinator
        {
            readonly TerminalPanel _panel;

            public int Offset { get; private set; }
            public ulong? LastLiveSeq { get; private set; }

            public TerminalSelectionCoordinator(TerminalPanel window)
            {
                _panel = window;
            }

            public void NoteLiveFrame(ulong liveSeq, int liveShift)
            {
                if (LastLiveSeq.HasValue && Offset == 0 &&
                    (_panel._state.Selection.HasSelection || _panel._state.Selection.Dragging ||
                        _panel._state.Selection.WordDragging))
                    MoveRows(-liveShift);
                LastLiveSeq = liveSeq;
            }

            public void SyncOffset(int offset)
            {
                if (offset == Offset) return;
                MoveRows(offset - Offset);
                Offset = offset;
            }

            public void ResetForNewRun()
            {
                Offset = 0;
                LastLiveSeq = null;
            }

            public void ResetLiveSequence() => LastLiveSeq = null;

            public Vector2Int CellAt(Rect body, Vector2 mouse)
            {
                _panel.SyncSnap();
                float cw = _panel.DisplayCellW(), ch = TerminalFont.CellH;
                if (cw <= 0.01f || ch <= 0.01f) return Vector2Int.zero;
                int col = Mathf.FloorToInt((mouse.x - body.x) / cw);
                int row = Mathf.FloorToInt(
                    (mouse.y - body.y - _panel.DisplayedHistoryShift(ch)) / ch);
                return new Vector2Int(col, row);
            }

            public string SelectionText(ScreenBuf buf)
            {
                return _panel._history.SelectionText(
                    buf,
                    _panel._state.Selection.A.x,
                    _panel._state.Selection.A.y,
                    _panel._state.Selection.B.x,
                    _panel._state.Selection.B.y);
            }

            void MoveRows(int delta)
            {
                if (delta == 0) return;
                var a = _panel._state.Selection.A;
                var b = _panel._state.Selection.B;
                var wordStart = _panel._state.Selection.WordStart;
                var wordEnd = _panel._state.Selection.WordEnd;
                a.y += delta;
                b.y += delta;
                wordStart.y += delta;
                wordEnd.y += delta;
                _panel._state.Selection.A = a;
                _panel._state.Selection.B = b;
                _panel._state.Selection.WordStart = wordStart;
                _panel._state.Selection.WordEnd = wordEnd;
                if (_panel._state.Selection.LineDragging)
                    _panel._state.Selection.LineStart += delta;
            }
        }
    }
}
