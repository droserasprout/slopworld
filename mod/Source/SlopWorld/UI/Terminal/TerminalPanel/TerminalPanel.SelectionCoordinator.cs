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
            public int LastLiveSeq { get; private set; } = -1;

            public TerminalSelectionCoordinator(TerminalPanel window)
            {
                _panel = window;
            }

            public void NoteLiveFrame(int liveSeq, int liveShift)
            {
                if (LastLiveSeq >= 0 && Offset == 0 &&
                    (_panel._hasSel || _panel._dragging || _panel._wordDragging))
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
                LastLiveSeq = -1;
            }

            public void ResetLiveSequence() => LastLiveSeq = -1;

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
                    _panel._selA.x,
                    _panel._selA.y,
                    _panel._selB.x,
                    _panel._selB.y);
            }

            void MoveRows(int delta)
            {
                if (delta == 0) return;
                var a = _panel._selA;
                var b = _panel._selB;
                var wordStart = _panel._wordStart;
                var wordEnd = _panel._wordEnd;
                a.y += delta;
                b.y += delta;
                wordStart.y += delta;
                wordEnd.y += delta;
                _panel._selA = a;
                _panel._selB = b;
                _panel._wordStart = wordStart;
                _panel._wordEnd = wordEnd;
                if (_panel._lineDragging) _panel._lineStart += delta;
            }
        }
    }
}
