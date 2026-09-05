using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class CommandPalette
    {
        void DrawInput(Rect r)
        {
            // One entry round the whole line, prompt included: in sub-mode the prompt is part
            // of what is being typed into, not a label beside a second box.
            UiWidgets.FieldFrame(r, GUI.GetNameOfFocusedControl() == "paletteInput");
            var inner = r.ContractedBy(UiWidgets.FieldPadX, UiWidgets.FieldPadY);

            var e = Event.current;
            bool isKeyDown = e.type == EventType.KeyDown;

            // IMGUI emits Space as key and character events; consume both in checklist mode, but toggle only on the key event so Space is not typed into the filter.
            if (isKeyDown && _mode == Mode.Sub && Checklist &&
                (e.keyCode == KeyCode.Space || e.character == ' '))
            {
                if (e.keyCode == KeyCode.Space) ToggleSub();
                e.Use();
                return;
            }

            // Handle navigation keys before the text field, which would otherwise consume
            // arrows, Escape and Enter for its own cursor motion and focus management.
            if (isKeyDown && HandleNavigation(e)) return;

            if (_mode == Mode.Sub)
            {
                if (DrawSubInput(inner, e, isKeyDown)) return;
            }
            else
            {
                DrawCommandInput(inner);
            }

            if (_focusInput)
            {
                GUI.FocusControl("paletteInput");
                _focusInput = false;
            }
        }

        bool HandleNavigation(Event e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    if (_mode == Mode.Sub) BackSub();
                    else Close();
                    e.Use();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_mode == Mode.Sub) ExecuteSub();
                    else ExecuteSelected();
                    e.Use();
                    return true;

                case KeyCode.UpArrow:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Max(0, _subIndex - 1);
                        ScrollToSub();
                    }
                    else if (_matches.Count > 0)
                    {
                        _selectedIndex = _selectedIndex == 0
                            ? _matches.Count - 1
                            : _selectedIndex - 1;
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;

                case KeyCode.DownArrow:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Min(_subShown.Count - 1, _subIndex + 1);
                        ScrollToSub();
                    }
                    else if (_matches.Count > 0)
                    {
                        _selectedIndex = _selectedIndex == _matches.Count - 1
                            ? 0
                            : _selectedIndex + 1;
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;

                case KeyCode.PageUp:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Max(0, _subIndex - PageSize);
                        ScrollToSub();
                    }
                    else
                    {
                        _selectedIndex = Mathf.Max(0, _selectedIndex - PageSize);
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;

                case KeyCode.PageDown:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Min(_subShown.Count - 1, _subIndex + PageSize);
                        ScrollToSub();
                    }
                    else
                    {
                        _selectedIndex = Mathf.Min(_matches.Count - 1, _selectedIndex + PageSize);
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;
            }
            return false;
        }

        bool DrawSubInput(Rect inner, Event e, bool isKeyDown)
        {
            // Prompt on the left, filter input on the right.
            string prompt = _subPrompt + " ";
            float promptW = UiWidgets.Wide(prompt);
            var labelRect = new Rect(inner.x, inner.y, promptW, inner.height);
            var fieldRect = new Rect(inner.x + promptW, inner.y,
                inner.width - promptW, inner.height);

            GUI.color = UiWidgets.Dim;
            UiWidgets.RowLabel(labelRect, prompt);
            GUI.color = Color.white;

            bool hadFilter = _subHasFilter;
            string wasSub = _subFilter;
            _subFilter = UiWidgets.BareField(fieldRect, "paletteInput", _subFilter);
            _subHasFilter = !string.IsNullOrEmpty(_subFilter);
            if (_subFilter != wasSub)
            {
                RebuildSub();
                _subIndex = 0;
                _scroll.JumpTo(Vector2.zero);
                Resize();
            }

            // Backspace on empty filter in sub-mode: go back to command list.
            // The text field was empty so it didn't consume the key; ours to take.
            if (isKeyDown && e.keyCode == KeyCode.Backspace && !hadFilter)
            {
                BackSub();
                e.Use();
                return true;
            }
            return false;
        }

        float DrawCommandInput(Rect inner)
        {
            string was = _input;
            _input = UiWidgets.BareField(inner, "paletteInput", _input);
            if (_input != was)
            {
                _filter = _input.ToLowerInvariant();
                RebuildMatches();
                _selectedIndex = 0;
                _scroll.JumpTo(Vector2.zero);
                Resize();
            }
            return inner.height;
        }

        void BackToCommands()
        {
            _mode = Mode.Commands;
            _subCmd = null;
            _subStack.Clear();
            _subFilter = "";
            _subHasFilter = false;
            _input = "";
            _filter = "";
            _selectedIndex = 0;
            _scroll.JumpTo(Vector2.zero);
            _focusInput = true;
            RebuildMatches();
            Resize();
        }

        // The box is as tall as what is in it: filtered down to one answer, a palette
        // holding its opening height is mostly empty dark. Lands next frame, this one's
        // window group having been opened already.
        void Resize()
        {
            float h = ContentHeight();
            if (Mathf.Abs(windowRect.height - h) > 0.5f) windowRect.height = h;
        }

        // Walk the same order as DrawCommandList to find the y of the selected item,
        // then scroll to keep it visible.
        void ScrollToSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _matches.Count) return;

            float y = 0f;
            string prev = null;
            for (int i = 0; i < _selectedIndex; i++)
            {
                if (Grouped)
                {
                    string group = GroupOf(i);
                    if (group != prev) { y += GroupH; prev = group; }
                }
                y += RowH;
            }

            _scroll.Reveal(y, RowH, _listH);
        }

        void ScrollToSub()
        {
            float y = _subIndex * RowH;
            _scroll.Reveal(y, RowH, _listH);
        }

        // Page navigation follows the number of complete rows visible in the list. A page
        // jump is still at least one row when the palette is shorter than a row.
        int PageSize => Mathf.Max(1, Mathf.FloorToInt(_listH / RowH));
    }
}
