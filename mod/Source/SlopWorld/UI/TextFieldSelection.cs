using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SlopWorld
{
    // TextEditor still owns drawing, caret movement, and ordinary click-drag selection. This
    // layer owns the gestures that differ between Unity versions and the terminal: click-count
    // recovery after an event was consumed, word/line selection, and dragging those selections.
    static class TextFieldSelection
    {
        internal enum PrepareResult
        {
            None,
            TrackedMouseDown,
            MultiClick,
            SelectionDrag
        }

        sealed class State
        {
            public readonly MouseClickSequence Clicks = new MouseClickSequence();
            public FieldLifetime Lifetime;
            public bool Dragging;
            public bool WordDragging;
            public bool LineDragging;
            public int WordStart;
            public int WordEnd;
            public int LineStart;
            public int LineEnd;
            public int ControlId;
            public TextEditor Editor;

            public void Reset(FieldLifetime lifetime)
            {
                Clicks.Reset();
                Lifetime = lifetime;
                Dragging = false;
                WordDragging = false;
                LineDragging = false;
                WordStart = WordEnd = LineStart = LineEnd = 0;
                ControlId = 0;
                Editor = null;
            }
        }

        static readonly Dictionary<string, State> States = new Dictionary<string, State>();
        static string _lastClickName;
        static FieldLifetime _lastClickLifetime;
        static string _activeName;
        static State _activeState;

        // Run before GUI.TextField. Unity's TextEditor treats a drag that starts on a
        // selection as text drag-and-drop, which changes the value instead of extending the
        // selection. Multi-clicks also need to be claimed before the native editor sees them;
        // otherwise its own click handling can replace the range on the next event.
        public static PrepareResult Prepare(Rect rect, string name, string text, GUIStyle style,
                                            TextEditor editor, FieldLifetime lifetime)
        {
            var e = Event.current;
            if (e == null || string.IsNullOrEmpty(name)) return PrepareResult.None;

            var state = GetState(name, lifetime);
            if (editor != null) state.Editor = editor;
            EventType type = RawType(e);
            if (type == EventType.MouseDown && e.button == 0 &&
                !rect.Contains(e.mousePosition))
            {
                if (_activeName == name) ClearActive(state);
                if (_lastClickName == name) state.Clicks.Reset();
                return PrepareResult.None;
            }

            int controlId = ControlId(name, editor, state);
            if (controlId == 0) return PrepareResult.None;

            if (type == EventType.MouseDown && e.button == 0)
            {
                if (_lastClickName != name || _lastClickLifetime != lifetime)
                    state.Clicks.Reset();

                int clicks = state.Clicks.Observe(e, Time.realtimeSinceStartup);
                _lastClickName = name;
                _lastClickLifetime = lifetime;
                _activeName = name;
                _activeState = state;
                state.ControlId = controlId;
                state.Dragging = true;
                state.WordDragging = false;
                state.LineDragging = false;

                if (clicks < 2) return PrepareResult.TrackedMouseDown;

                var activeEditor = editor ?? state.Editor;
                if (activeEditor == null) return PrepareResult.TrackedMouseDown;

                int index = IndexAt(activeEditor, rect, style, text, e.mousePosition);
                if (clicks >= 3)
                {
                    LineRange(text, index, out state.LineStart, out state.LineEnd);
                    state.LineDragging = true;
                    Select(activeEditor, state.LineStart, state.LineEnd);
                    state.Clicks.Reset();
                }
                else
                {
                    WordRange(text, index, out state.WordStart, out state.WordEnd);
                    state.WordDragging = true;
                    Select(activeEditor, state.WordStart, state.WordEnd);
                }

                GUIUtility.hotControl = controlId;
                GUI.changed = true;
                e.Use();
                return PrepareResult.MultiClick;
            }

            if (_activeName != name || _activeState != state || !state.Dragging ||
                (!state.WordDragging && !state.LineDragging) || e.button > 0)
                return PrepareResult.None;

            if (type == EventType.MouseDrag || type == EventType.MouseUp)
            {
                // Keep rawType intact so Handle can finish the gesture after the native field
                // has drawn, but make the native field see Used and skip text drag-and-drop.
                GUIUtility.hotControl = state.ControlId;
                if (e.type != EventType.Used) e.Use();
                return PrepareResult.SelectionDrag;
            }

            return PrepareResult.None;
        }

        // A window above the field can consume MouseDrag/MouseUp after the field captured the
        // press. Replay those events so native single-click drags keep working, then apply our
        // word/line range below to make the result deterministic.
        public static bool ShouldReplay(string name, Event e, FieldLifetime lifetime)
        {
            if (e == null || _activeState == null || _activeName != name ||
                _activeState.Lifetime != lifetime || !_activeState.Dragging ||
                e.type != EventType.Used)
                return false;

            return RawType(e) == EventType.MouseDrag || RawType(e) == EventType.MouseUp;
        }

        public static void ReleaseFocus()
        {
            if (_activeState != null) ClearActive(_activeState);
            _lastClickName = null;
            _lastClickLifetime = null;
            GUI.FocusControl(null);
        }

        public static void Handle(Rect rect, string name, string text, GUIStyle style,
                                  TextEditor editor, FieldLifetime lifetime)
        {
            var e = Event.current;
            if (e == null || string.IsNullOrEmpty(name)) return;

            var state = GetState(name, lifetime);
            if (editor != null) state.Editor = editor;
            var activeEditor = editor ?? state.Editor;
            EventType type = RawType(e);
            bool inside = rect.Contains(e.mousePosition);

            if (type == EventType.MouseDown && e.button == 0)
            {
                if (!inside)
                {
                    if (_activeName == name) ClearActive(state);
                    if (_lastClickName == name) state.Clicks.Reset();
                    return;
                }

                int mouseControlId = ControlId(name, editor, state);
                if (mouseControlId == 0) return;

                if (_lastClickName != name || _lastClickLifetime != lifetime)
                    state.Clicks.Reset();

                int clicks = state.Clicks.Observe(e, Time.realtimeSinceStartup);
                _lastClickName = name;
                _lastClickLifetime = lifetime;
                _activeName = name;
                _activeState = state;
                state.ControlId = mouseControlId;
                state.Dragging = true;
                state.WordDragging = false;
                state.LineDragging = false;

                if (activeEditor == null || clicks < 2) return;

                int index = IndexAt(activeEditor, rect, style, text, e.mousePosition);
                if (clicks >= 3)
                {
                    LineRange(text, index, out state.LineStart, out state.LineEnd);
                    state.LineDragging = true;
                    Select(activeEditor, state.LineStart, state.LineEnd);
                    state.Clicks.Reset();
                }
                else
                {
                    WordRange(text, index, out state.WordStart, out state.WordEnd);
                    state.WordDragging = true;
                    Select(activeEditor, state.WordStart, state.WordEnd);
                }

                if (mouseControlId != 0) GUIUtility.hotControl = mouseControlId;
                GUI.changed = true;
                e.Use();
                return;
            }

            int controlId = ControlId(name, editor, state);
            if (controlId == 0) return;

            if (_activeName != name || _activeState != state || !state.Dragging) return;

            // Once the second click is consumed, IMGUI is not guaranteed to keep sending
            // MouseDrag/MouseUp to the field (especially when the pointer leaves its window).
            // Keep the terminal-style range alive from repaint events while the physical
            // button is held, and close it on the first repaint after release.
            if (type == EventType.Repaint &&
                (state.WordDragging || state.LineDragging))
            {
                if (Input.GetMouseButton(0))
                {
                    UpdateDrag(activeEditor, rect, text, style, state);
                }
                else
                {
                    EndDrag(state, name);
                }
                return;
            }

            if (type == EventType.MouseDrag && e.button <= 0)
            {
                UpdateDrag(activeEditor, rect, text, style, state);
                e.Use();
                return;
            }

            if (type == EventType.MouseUp && e.button <= 0)
            {
                UpdateDrag(activeEditor, rect, text, style, state);
                EndDrag(state, name);
                e.Use();
            }
            else if (type != EventType.MouseDrag && type != EventType.MouseDown &&
                     !Input.GetMouseButton(0))
            {
                ClearActive(state);
            }
        }

        static void UpdateDrag(TextEditor editor, Rect rect, string text, GUIStyle style,
                               State state)
        {
            if (editor == null || (!state.WordDragging && !state.LineDragging)) return;
            var e = Event.current;
            if (e == null) return;

            int index = IndexAt(editor, rect, style, text, e.mousePosition);
            if (state.LineDragging)
                UpdateLine(editor, text, index, state);
            else
                UpdateWord(editor, text, index, state);
            GUI.changed = true;
        }

        static void EndDrag(State state, string name)
        {
            state.Dragging = false;
            state.WordDragging = false;
            state.LineDragging = false;
            if (GUIUtility.hotControl == state.ControlId) GUIUtility.hotControl = 0;
            if (_activeName == name)
            {
                _activeName = null;
                _activeState = null;
            }
        }

        static State GetState(string name, FieldLifetime lifetime)
        {
            if (!States.TryGetValue(name, out var state))
            {
                state = new State();
                States.Add(name, state);
            }
            if (state.Lifetime != lifetime) state.Reset(lifetime);
            return state;
        }

        static void ClearActive(State state)
        {
            state.Dragging = false;
            state.WordDragging = false;
            state.LineDragging = false;
            if (GUIUtility.hotControl == state.ControlId) GUIUtility.hotControl = 0;
            if (ReferenceEquals(_activeState, state))
            {
                _activeName = null;
                _activeState = null;
            }
        }

        static EventType RawType(Event e) =>
            e.type == EventType.Used ? e.rawType : e.type;

        static int ControlId(string name, TextEditor editor, State state)
        {
            if (editor != null && GUI.GetNameOfFocusedControl() == name)
            {
                int id = GUIUtility.keyboardControl;
                if (id != 0) return id;
            }
            return _activeState != null && _activeName == name ? _activeState.ControlId : 0;
        }

        static int IndexAt(TextEditor editor, Rect rect, GUIStyle style, string text,
                           Vector2 position)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            editor.position = rect;
            editor.style = style;
            editor.text = text;
            editor.MoveCursorToPosition(position);
            return Mathf.Clamp(editor.cursorIndex, 0, text.Length);
        }

        static void Select(TextEditor editor, int anchor, int active)
        {
            int length = editor.text == null ? 0 : editor.text.Length;
            anchor = Mathf.Clamp(anchor, 0, length);
            active = Mathf.Clamp(active, 0, length);
            editor.selectIndex = anchor;
            editor.cursorIndex = active;
        }

        static void UpdateWord(TextEditor editor, string text, int index, State state)
        {
            WordRange(text, index, out int start, out int end);
            if (start < state.WordStart)
                Select(editor, state.WordEnd, start);
            else if (start > state.WordStart)
                Select(editor, state.WordStart, end);
            else
                Select(editor, state.WordStart, state.WordEnd);
        }

        static void UpdateLine(TextEditor editor, string text, int index, State state)
        {
            LineRange(text, index, out int start, out int end);
            if (start < state.LineStart)
                Select(editor, state.LineEnd, start);
            else if (start > state.LineStart)
                Select(editor, state.LineStart, end);
            else
                Select(editor, state.LineStart, state.LineEnd);
        }

        // Match the terminal's word rule: Unicode letters/digits/marks and underscore words,
        // otherwise a run of identical non-word code points. Keep indices on code-point
        // boundaries so a supplementary character can never be split by a selection.
        static void WordRange(string text, int index, out int start, out int end)
        {
            if (string.IsNullOrEmpty(text)) { start = end = 0; return; }
            int at = CodePointIndex(text, index);
            int anchor = CodePointAt(text, at);
            bool word = IsWordChar(text, at);
            start = at;
            end = NextCodePoint(text, at);
            while (start > 0)
            {
                int previous = PreviousCodePoint(text, start);
                if (!SameClass(text, previous, anchor, word)) break;
                start = previous;
            }
            while (end < text.Length && SameClass(text, end, anchor, word))
                end = NextCodePoint(text, end);
        }

        static void LineRange(string text, int index, out int start, out int end)
        {
            if (string.IsNullOrEmpty(text)) { start = end = 0; return; }
            int at = Mathf.Clamp(index, 0, text.Length - 1);
            start = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
            int newline = text.IndexOf('\n', at);
            end = newline < 0 ? text.Length : newline + 1;
        }

        static bool SameClass(string text, int index, int anchor, bool word) =>
            word ? IsWordChar(text, index) : CodePointAt(text, index) == anchor;

        static bool IsWordChar(string text, int index)
        {
            if (text[index] == '_') return true;
            switch (CharUnicodeInfo.GetUnicodeCategory(text, index))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return true;
                default:
                    return false;
            }
        }

        static int CodePointIndex(string text, int index)
        {
            int at = Mathf.Clamp(index, 0, text.Length - 1);
            if (at > 0 && char.IsLowSurrogate(text[at]) &&
                char.IsHighSurrogate(text[at - 1]))
                at--;
            return at;
        }

        static int CodePointAt(string text, int index) =>
            char.ConvertToUtf32(text, index);

        static int NextCodePoint(string text, int index)
        {
            int next = index + 1;
            if (next < text.Length && char.IsHighSurrogate(text[index]) &&
                char.IsLowSurrogate(text[next]))
                return next + 1;
            return next;
        }

        static int PreviousCodePoint(string text, int index)
        {
            int previous = index - 1;
            if (previous > 0 && char.IsLowSurrogate(text[previous]) &&
                char.IsHighSurrogate(text[previous - 1]))
                previous--;
            return previous;
        }
    }
}
