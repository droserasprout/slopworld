using UnityEngine;

namespace SlopWorld
{
    sealed partial class TerminalPanel
    {
        // Selection gestures remain ordered by TerminalSelectionInput. This coordinator owns the
        // panel-specific cell translation and clipboard text extraction. Selection state owns
        // the row coordinates and their translation as live and displayed frames advance.
        sealed class TerminalSelectionCoordinator
        {
            readonly TerminalPanel _panel;

            public ulong? LastLiveSeq => _panel._state.Selection.LastLiveSeq;

            public TerminalSelectionCoordinator(TerminalPanel window)
            {
                _panel = window;
            }

            public void NoteLiveFrame(ulong liveSeq, int liveShift) =>
                _panel._state.Selection.NoteLiveFrame(liveSeq, liveShift);

            public void SyncOffset(int offset) => _panel._state.Selection.SyncOffset(offset);

            public void ResetForNewRun() => _panel._state.Selection.ResetCoordinates();

            public void ResetLiveSequence() => _panel._state.Selection.ResetLiveSequence();

            public Vector2Int CellAt(Rect body, Vector2 mouse)
            {
                _panel.SyncSnap();
                float cw = _panel.DisplayCellW(),
                    ch = TerminalFont.CellH;
                if (cw <= 0.01f || ch <= 0.01f)
                    return Vector2Int.zero;
                int col = Mathf.FloorToInt((mouse.x - body.x) / cw);
                int row = Mathf.FloorToInt(
                    (mouse.y - body.y - _panel.DisplayedHistoryShift(ch)) / ch
                );
                return new Vector2Int(col, row);
            }

            public string SelectionText(ScreenBuf buf)
            {
                return _panel._history.SelectionText(
                    buf,
                    _panel._state.Selection.A.x,
                    _panel._state.Selection.A.y,
                    _panel._state.Selection.B.x,
                    _panel._state.Selection.B.y
                );
            }
        }
    }
}
