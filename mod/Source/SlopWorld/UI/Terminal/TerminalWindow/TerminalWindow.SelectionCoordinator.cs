using UnityEngine;

namespace SlopWorld
{
    public partial class TerminalWindow
    {
        // Selection gestures remain ordered by TerminalSelectionInput. This coordinator owns the
        // coordinate-space bookkeeping that gestures should not have to understand: live-frame
        // shifts, displayed-history offsets, cell translation, and clipboard text extraction.
        sealed class TerminalSelectionCoordinator
        {
            readonly TerminalWindow _window;

            public int Offset { get; private set; }
            public int LastLiveSeq { get; private set; } = -1;

            public TerminalSelectionCoordinator(TerminalWindow window)
            {
                _window = window;
            }

            public void NoteLiveFrame(int liveSeq, int liveShift)
            {
                if (LastLiveSeq >= 0 && Offset == 0 &&
                    (_window._hasSel || _window._dragging || _window._wordDragging))
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
                TerminalWindow.SyncSnap();
                float cw = TerminalWindow.DisplayCellW(), ch = TerminalFont.CellH;
                if (cw <= 0.01f || ch <= 0.01f) return Vector2Int.zero;
                int col = Mathf.FloorToInt((mouse.x - body.x) / cw);
                int row = Mathf.FloorToInt(
                    (mouse.y - body.y - _window.DisplayedHistoryShift(ch)) / ch);
                return new Vector2Int(col, row);
            }

            public string SelectionText(ScreenBuf buf)
            {
                return _window._history.SelectionText(
                    buf,
                    _window._selA.x,
                    _window._selA.y,
                    _window._selB.x,
                    _window._selB.y);
            }

            void MoveRows(int delta)
            {
                if (delta == 0) return;
                var a = _window._selA;
                var b = _window._selB;
                var wordStart = _window._wordStart;
                var wordEnd = _window._wordEnd;
                a.y += delta;
                b.y += delta;
                wordStart.y += delta;
                wordEnd.y += delta;
                _window._selA = a;
                _window._selB = b;
                _window._wordStart = wordStart;
                _window._wordEnd = wordEnd;
                if (_window._lineDragging) _window._lineStart += delta;
            }
        }
    }
}
