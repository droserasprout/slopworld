using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns mouse selection gesture state and routing. The terminal window still owns the
    // selection model for now. Keeping this event choreography here prevents input dispatch
    // from also knowing how a drag, word selection, and line selection are completed.
    sealed class TerminalSelectionInput
    {
        readonly TerminalPanel _panel;
        readonly MouseClickSequence _clicks = new MouseClickSequence();

        public TerminalSelectionInput(TerminalPanel panel)
        {
            _panel = panel;
        }

        public bool TryHandleMultiClick(Rect body, Event e)
        {
            if (TerminalInputController.MouseType(e) != EventType.MouseDown || e.button != 0 ||
                !body.Contains(e.mousePosition)) return false;

            int clickCount = _clicks.Observe(e, Time.realtimeSinceStartup);
            if (clickCount < 2) return false;

            _panel.CaptureSelection(body);
            var cell = _panel.CellAt(body, e.mousePosition);
            if (clickCount >= 3) _panel.TripleClickSelect(cell.y);
            else _panel.DoubleClickSelect(cell);
            if (clickCount >= 3) _clicks.Reset();
            e.Use();
            return true;
        }

        public void Handle(Rect body, Event e)
        {
            switch (TerminalInputController.MouseType(e))
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
            _panel.SelectionA = _panel.SelectionB = _panel.CellAt(body, e.mousePosition);
            _panel.SelectionMouse = e.mousePosition;
            _panel.SelectionEdgeDirection = 0;
            _panel.SelectionEdgeFrame = -1;
            _panel.Dragging = true;
            _panel.SelectionMoved = false;
            _panel.MultiClickSelection = false;
            _panel.WordDragging = false;
            _panel.LineDragging = false;
            _panel.HasSelection = false;
            _panel.CaptureSelection(body);
            e.Use();
        }

        void Drag(Rect body, Event e)
        {
            if (!_panel.Dragging) return;
            _panel.SelectionMouse = e.mousePosition;
            _panel.SelectionMoved = true;
            var cell = _panel.CellAt(body, e.mousePosition);
            if (e.mousePosition.y < body.y || e.mousePosition.y >= body.yMax)
            {
                int rows = Mathf.Max(1, _panel.Rows > 0 ? _panel.Rows :
                    SessionHub.Instance.Screen(_panel.SessionName)?.Rows ?? 1);
                cell.y = Mathf.Clamp(cell.y, 0, rows - 1);
            }
            if (_panel.LineDragging)
                _panel.SelectLineRange(_panel.LineStart, cell.y);
            else if (_panel.WordDragging)
                _panel.UpdateWordSelection(cell);
            else
            {
                _panel.SelectionB = cell;
                _panel.HasSelection = true;
            }
            e.Use();
        }

        void End(Rect body, Event e)
        {
            if (!_panel.Dragging) return;
            var cell = _panel.CellAt(body, e.mousePosition);
            if (e.mousePosition.y < body.y || e.mousePosition.y >= body.yMax)
            {
                int rows = Mathf.Max(1, _panel.Rows > 0 ? _panel.Rows :
                    SessionHub.Instance.Screen(_panel.SessionName)?.Rows ?? 1);
                cell.y = Mathf.Clamp(cell.y, 0, rows - 1);
            }

            // A double click selects a word and a triple click replaces it with a row. Do not
            // copy the intermediate word to CLIPBOARD. The completed triple-click line is
            // published to PRIMARY by TripleClickSelect.
            // Mouse selection belongs to the host PRIMARY surface. Ordinary drag selection
            // must not overwrite CLIPBOARD. Explicit Ctrl+C and the menu still use it.
            bool copyPrimary = !_panel.MultiClickSelection || _panel.SelectionMoved;
            if (_panel.LineDragging)
            {
                _panel.SelectLineRange(_panel.LineStart, cell.y);
                _panel.LineDragging = false;
                _panel.Dragging = false;
                _panel.SelectionMoved = false;
                _panel.ReleaseSelection();
                if (copyPrimary) _panel.CopyPrimarySelection();
            }
            else if (_panel.WordDragging)
            {
                _panel.UpdateWordSelection(cell);
                _panel.WordDragging = false;
                _panel.Dragging = false;
                _panel.ReleaseSelection();
                if (_panel.HasSelection && copyPrimary) _panel.CopyPrimarySelection();
            }
            else
            {
                _panel.Dragging = false;
                _panel.SelectionB = cell;
                if (_panel.SelectionMoved || _panel.SelectionA != _panel.SelectionB)
                {
                    _panel.HasSelection = true;
                    _panel.CopyPrimarySelection();
                }
                else _panel.HasSelection = false;
                _panel.SelectionMoved = false;
                _panel.ReleaseSelection();
            }
            _panel.SelectionMoved = false;
            _panel.MultiClickSelection = false;
            _panel.SelectionEdgeDirection = 0;
            _panel.SelectionEdgeFrame = -1;
            e.Use();
        }
    }
}
