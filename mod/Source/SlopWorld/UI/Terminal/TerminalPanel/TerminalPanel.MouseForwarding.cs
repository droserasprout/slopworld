using UnityEngine;

namespace SlopWorld
{
    sealed partial class TerminalPanel
    {
        // The press is forwarded provisionally. If the app does not claim drag input, the
        // provisional press is closed and the gesture becomes a text selection.
        bool _mouseFwd;
        int _fwdButton;
        Vector2Int _fwdCell;

        internal void ReleasePanelInput()
        {
            Flush();
            if (_mouseFwd && _state.Name != null)
                SessionHub.Instance.Terminal.SendMouse(_state.Name, "release", _fwdButton,
                    _fwdCell.x, _fwdCell.y);
            _mouseFwd = false;
            _state.Selection.Dragging = false;
            _state.Selection.WordDragging = false;
            _state.Selection.LineDragging = false;
            _state.Selection.EdgeDirection = 0;
            ReleaseSelection();
            if (_historyBarDragging) GUIUtility.hotControl = 0;
            _historyBarDragging = false;
        }

        internal bool OwnsForwardedMouse(Event e) => _mouseFwd && e.button == _fwdButton &&
            (MouseType(e) == EventType.MouseDrag || MouseType(e) == EventType.MouseUp);

        // An app in click-reporting mode (Claude Code is one) said nothing about motion. Therefore,
        // A drag across its output was never its to receive - forwarded anyway, it left no way to
        // select text short of holding Shift. The press goes over as a press. The moment it turns
        // into a drag that click is closed and the rest taken as a selection.
        internal bool HandleMouseForward(Rect body, Event e)
        {
            int btn = Mathf.Clamp(e.button, 0, 2);
            var cell = CellAt(body, e.mousePosition);
            var live = SessionHub.Instance.Screen(_state.Name);

            switch (MouseType(e))
            {
                case EventType.MouseDown:
                    if (!body.Contains(e.mousePosition)) return true;
                    JumpToLive();
                    ClearSelection();
                    SessionHub.Instance.Terminal.SendMouse(_state.Name, "press", btn, cell.x, cell.y);
                    _mouseFwd = true;
                    _fwdButton = btn;
                    _fwdCell = cell;
                    e.Use();
                    return true;

                case EventType.MouseDrag:
                    if (!_mouseFwd) return false;
                    if (live != null && live.AppDrag)
                    {
                        SessionHub.Instance.Terminal.SendMouse(_state.Name, "drag", btn, cell.x, cell.y);
                        e.Use();
                        return true;
                    }
                    // Only the left button selects. Anything else is swallowed.
                    SessionHub.Instance.Terminal.SendMouse(_state.Name, "release", btn, _fwdCell.x, _fwdCell.y);
                    _mouseFwd = false;
                    if (btn != 0) { e.Use(); return true; }
                    _state.Selection.A = _fwdCell;
                    _state.Selection.Dragging = true;
                    CaptureSelection(body);
                    return false;

                case EventType.MouseUp:
                    if (!_mouseFwd) return false;
                    SessionHub.Instance.Terminal.SendMouse(_state.Name, "release", btn, cell.x, cell.y);
                    _mouseFwd = false;
                    e.Use();
                    return true;
            }
            return true;
        }

    }
}
