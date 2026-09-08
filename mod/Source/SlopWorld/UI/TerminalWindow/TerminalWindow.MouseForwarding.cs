using UnityEngine;

namespace SlopWorld
{
    // TerminalWindow app-mouse forwarding.
    public partial class TerminalWindow
    {
        // The press is forwarded provisionally. If the app does not claim drag input, the
        // provisional press is closed and the gesture becomes a text selection.
        bool _mouseFwd;
        int _fwdButton;
        Vector2Int _fwdCell;

        internal bool OwnsForwardedMouse(Event e) => _mouseFwd && e.button == _fwdButton &&
            (MouseType(e) == EventType.MouseDrag || MouseType(e) == EventType.MouseUp);

        // An app in click-reporting mode (Claude Code is one) said nothing about motion, so a
        // drag across its output was never its to receive - forwarded anyway, it left no way
        // to select text short of holding Shift. The press goes over as a press, and the
        // moment it turns into a drag that click is closed and the rest taken as a selection.
        internal bool HandleMouseForward(Rect body, Event e)
        {
            int btn = Mathf.Clamp(e.button, 0, 2);
            var cell = CellAt(body, e.mousePosition);
            var live = SessionHub.Instance.Screen(_name);

            switch (MouseType(e))
            {
                case EventType.MouseDown:
                    if (!body.Contains(e.mousePosition)) return true;
                    JumpToLive();
                    ClearSelection();
                    SessionHub.Instance.SendMouse(_name, "press", btn, cell.x, cell.y);
                    _mouseFwd = true;
                    _fwdButton = btn;
                    _fwdCell = cell;
                    e.Use();
                    return true;

                case EventType.MouseDrag:
                    if (!_mouseFwd) return false;
                    if (live != null && live.AppDrag)
                    {
                        SessionHub.Instance.SendMouse(_name, "drag", btn, cell.x, cell.y);
                        e.Use();
                        return true;
                    }
                    // Only the left button selects; anything else is swallowed.
                    SessionHub.Instance.SendMouse(_name, "release", btn, _fwdCell.x, _fwdCell.y);
                    _mouseFwd = false;
                    if (btn != 0) { e.Use(); return true; }
                    _selA = _fwdCell;
                    _dragging = true;
                    CaptureSelection(body);
                    return false;

                case EventType.MouseUp:
                    if (!_mouseFwd) return false;
                    SessionHub.Instance.SendMouse(_name, "release", btn, cell.x, cell.y);
                    _mouseFwd = false;
                    e.Use();
                    return true;
            }
            return true;
        }

    }
}
