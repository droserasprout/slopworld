using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // TextEditor still owns drawing, caret movement, and ordinary click-drag selection. This layer
    // owns the gestures that differ between Unity versions and the terminal. Click-count recovery
    // after an event was consumed, word/line selection, and dragging those selections.
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

        static int BeginMouse(string name, FieldLifetime lifetime, State state, Event e,
                              int controlId)
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
            return clicks;
        }

        static void ApplyMultiClick(Rect rect, string text, GUIStyle style, TextEditor editor,
                                    State state, int clicks, Event e, int controlId)
        {
            int index = IndexAt(editor, rect, style, text, e.mousePosition);
            if (clicks >= 3)
            {
                LineRange(text, index, out state.LineStart, out state.LineEnd);
                state.LineDragging = true;
                Select(editor, state.LineStart, state.LineEnd);
                state.Clicks.Reset();
            }
            else
            {
                WordRange(text, index, out state.WordStart, out state.WordEnd);
                state.WordDragging = true;
                Select(editor, state.WordStart, state.WordEnd);
            }

            GUIUtility.hotControl = controlId;
            GUI.changed = true;
            e.Use();
        }

        // Run before GUI.TextField. Unity's TextEditor treats a drag that starts on a
        // selection as text drag-and-drop, which changes the value instead of extending the
        // selection. Multi-clicks also need to be claimed before the native editor sees them.
        // otherwise its own click handling can replace the range on the next event.
        public static PrepareResult Prepare(Rect rect, string name, string text, GUIStyle style,
                                            TextEditor editor, FieldLifetime lifetime)
        {
            var e = Event.current;
            if (e == null || string.IsNullOrEmpty(name) || lifetime == null || !lifetime.Alive) return PrepareResult.None;

            var state = GetState(name, lifetime);
            if (editor != null) state.Editor = editor;
            EventType type = UiEvent.RawType(e);
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
                int clicks = BeginMouse(name, lifetime, state, e, controlId);

                if (clicks < 2) return PrepareResult.TrackedMouseDown;

                var activeEditor = editor ?? state.Editor;
                if (activeEditor == null) return PrepareResult.TrackedMouseDown;

                ApplyMultiClick(rect, text, style, activeEditor, state, clicks, e, controlId);
                return PrepareResult.MultiClick;
            }

            if (_activeName != name || _activeState != state || !state.Dragging ||
                (!state.WordDragging && !state.LineDragging) || e.button > 0)
                return PrepareResult.None;

            if (type == EventType.MouseDrag || type == EventType.MouseUp)
            {
                // Keep rawType intact so Handle can finish the gesture after the native field has
                // drawn. However, Make the native field see Used and skip text drag-and-drop.
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

            return UiEvent.RawType(e) == EventType.MouseDrag ||
                UiEvent.RawType(e) == EventType.MouseUp;
        }

        // GUI.TextField still runs for a consumed multi-click event so it can draw and keep
        // focus. Some Unity versions nevertheless rewrite the TextEditor range during that
        // call. Put our word/line range back before the next drag event is processed.
        public static void FinishMultiClick(string name, TextEditor editor,
                                             FieldLifetime lifetime)
        {
            if (string.IsNullOrEmpty(name) || lifetime == null || !lifetime.Alive) return;
            var state = GetState(name, lifetime);
            if (_activeName != name || _activeState != state || !state.Dragging) return;

            var activeEditor = editor ?? state.Editor;
            if (activeEditor == null) return;
            state.Editor = activeEditor;
            if (state.LineDragging)
                Select(activeEditor, state.LineStart, state.LineEnd);
            else if (state.WordDragging)
                Select(activeEditor, state.WordStart, state.WordEnd);
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
            if (e == null || string.IsNullOrEmpty(name) || lifetime == null || !lifetime.Alive) return;

            var state = GetState(name, lifetime);
            if (editor != null) state.Editor = editor;
            var activeEditor = editor ?? state.Editor;
            EventType type = UiEvent.RawType(e);
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

                int clicks = BeginMouse(name, lifetime, state, e, mouseControlId);

                if (activeEditor == null || clicks < 2) return;

                ApplyMultiClick(rect, text, style, activeEditor, state, clicks, e,
                    mouseControlId);
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

        // Cancellation retires both gesture ownership and retained native editor references.
        internal static void Retire(FieldLifetime lifetime)
        {
            var retired = new List<string>();
            foreach (var pair in States)
            {
                if (pair.Value.Lifetime != lifetime) continue;
                if (ReferenceEquals(_activeState, pair.Value)) ClearActive(pair.Value);
                retired.Add(pair.Key);
            }
            foreach (var name in retired) States.Remove(name);
            if (_lastClickLifetime == lifetime)
            {
                _lastClickName = null;
                _lastClickLifetime = null;
            }
        }

        static State GetState(string name, FieldLifetime lifetime)
        {
            if (!States.TryGetValue(name, out var state))
            {
                state = new State();
                States.Add(name, state);
            }
            if (state.Lifetime != lifetime)
            {
                if (ReferenceEquals(_activeState, state)) ClearActive(state);
                state.Reset(lifetime);
            }
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
            var selection = TextSelectionRules.ExpandWordSelection(
                new TextSelectionRange(state.WordStart, state.WordEnd),
                new TextSelectionRange(start, end));
            Select(editor, selection.Anchor, selection.Focus);
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

        // Use the shared text rule. Unicode letters, digits, marks, and underscores form words.
        // Other selections contain consecutive identical non-word code points.
        // Keep indices on code-point boundaries to prevent selections from splitting supplementary characters.
        static void WordRange(string text, int index, out int start, out int end)
        {
            var range = TextSelectionRules.WordRange(text, index);
            start = range.Start;
            end = range.End;
        }

        static void LineRange(string text, int index, out int start, out int end)
        {
            var range = TextSelectionRules.LineRange(text, index);
            start = range.Start;
            end = range.End;
        }
    }
}
