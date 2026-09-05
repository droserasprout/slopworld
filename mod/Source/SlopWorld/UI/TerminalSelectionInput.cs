using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns mouse selection gesture state and routing. The terminal window still owns the
    // selection model for now; keeping this event choreography here prevents input dispatch
    // from also knowing how a drag, word selection, and line selection are completed.
    sealed class TerminalSelectionInput
    {
        readonly TerminalWindow _window;
        readonly MouseClickSequence _clicks = new MouseClickSequence();

        public TerminalSelectionInput(TerminalWindow window)
        {
            _window = window;
        }

        public bool TryHandleMultiClick(Rect body, Event e)
        {
            if (TerminalWindow.MouseType(e) != EventType.MouseDown || e.button != 0 ||
                !body.Contains(e.mousePosition)) return false;

            int clickCount = _clicks.Observe(e, Time.realtimeSinceStartup);
            if (clickCount < 2) return false;

            _window.CaptureSelection(body);
            var cell = _window.CellAt(body, e.mousePosition);
            if (clickCount >= 3) _window.TripleClickSelect(cell.y);
            else _window.DoubleClickSelect(cell);
            if (clickCount >= 3) _clicks.Reset();
            e.Use();
            return true;
        }

        public void Handle(Rect body, Event e)
        {
            switch (TerminalWindow.MouseType(e))
            {
                case EventType.MouseDown:
                    Begin(body, e);
                    return;
                case EventType.MouseDrag:
                    Drag(body, e);
                    return;
                case EventType.MouseUp:
                    End(body, e);
                    return;
            }
        }

        void Begin(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition)) return;
            _window.SelectionA = _window.SelectionB = _window.CellAt(body, e.mousePosition);
            _window.SelectionMouse = e.mousePosition;
            _window.SelectionEdgeDirection = 0;
            _window.SelectionEdgeFrame = -1;
            _window.Dragging = true;
            _window.SelectionMoved = false;
            _window.MultiClickSelection = false;
            _window.WordDragging = false;
            _window.LineDragging = false;
            _window.HasSelection = false;
            _window.CaptureSelection(body);
            e.Use();
        }

        void Drag(Rect body, Event e)
        {
            if (!_window.Dragging) return;
            _window.SelectionMouse = e.mousePosition;
            _window.SelectionMoved = true;
            var cell = _window.CellAt(body, e.mousePosition);
            if (e.mousePosition.y < body.y || e.mousePosition.y >= body.yMax)
            {
                int rows = Mathf.Max(1, _window.Rows > 0 ? _window.Rows :
                    SessionHub.Instance.Screen(_window.SessionName)?.Rows ?? 1);
                cell.y = Mathf.Clamp(cell.y, 0, rows - 1);
            }
            if (_window.LineDragging)
                _window.SelectLineRange(_window.LineStart, cell.y);
            else if (_window.WordDragging)
                _window.UpdateWordSelection(cell);
            else
            {
                _window.SelectionB = cell;
                _window.HasSelection = true;
            }
            e.Use();
        }

        void End(Rect body, Event e)
        {
            if (!_window.Dragging) return;
            var cell = _window.CellAt(body, e.mousePosition);
            if (e.mousePosition.y < body.y || e.mousePosition.y >= body.yMax)
            {
                int rows = Mathf.Max(1, _window.Rows > 0 ? _window.Rows :
                    SessionHub.Instance.Screen(_window.SessionName)?.Rows ?? 1);
                cell.y = Mathf.Clamp(cell.y, 0, rows - 1);
            }

            // A double click selects a word and a triple click replaces it with a row. Do not
            // copy the intermediate word to CLIPBOARD; the completed triple-click line is
            // published to PRIMARY by TripleClickSelect.
            bool copy = !_window.MultiClickSelection || _window.SelectionMoved;
            if (_window.LineDragging)
            {
                _window.SelectLineRange(_window.LineStart, cell.y);
                _window.LineDragging = false;
                _window.Dragging = false;
                _window.SelectionMoved = false;
                _window.ReleaseSelection();
                if (copy) _window.CopySelection();
            }
            else if (_window.WordDragging)
            {
                _window.UpdateWordSelection(cell);
                _window.WordDragging = false;
                _window.Dragging = false;
                _window.ReleaseSelection();
                if (_window.HasSelection && copy) _window.CopySelection();
            }
            else
            {
                _window.Dragging = false;
                _window.SelectionB = cell;
                if (_window.SelectionMoved || _window.SelectionA != _window.SelectionB)
                {
                    _window.HasSelection = true;
                    if (copy) _window.CopySelection();
                }
                else _window.HasSelection = false;
                _window.SelectionMoved = false;
                _window.ReleaseSelection();
            }
            _window.SelectionMoved = false;
            _window.MultiClickSelection = false;
            _window.SelectionEdgeDirection = 0;
            _window.SelectionEdgeFrame = -1;
            e.Use();
        }
    }
}
