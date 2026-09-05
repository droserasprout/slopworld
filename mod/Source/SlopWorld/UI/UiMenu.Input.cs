using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // Keyboard navigation is kept separate from the pointer renderer so the menu's two
    // input models can evolve without making the layout path harder to follow.
    public partial class UiMenu
    {
        // Keyboard selection is independent of the pointer: a menu opened from a focused
        // control may have no useful mouse position at all, and moving an arrow key should
        // not be reset by the pointer's hover pass.
        int _selected = -1;
        bool _keyboardSelection;
        Event _acceptEvent;

        // Keyboard navigation belongs to the menu rather than the control that opened it.
        // Use rawType because an absorbing window may consume event.type before this body
        // runs. Unhandled keys still dismiss the menu and remain unused, so a global shortcut
        // such as F1 can act on the same press.
        bool HandleKeyboard()
        {
            // Accept is dispatched by WindowStack before window contents. The menu handles
            // it there so an accepted form underneath cannot submit at the same time; skip
            // the same event when the body is reached.
            if (Event.current != null &&
                ReferenceEquals(Event.current, _acceptEvent)) return true;

            // WindowStack draws parents before children. Give the deepest open menu first
            // refusal so an arrow or Enter cannot answer for a row hidden behind its child.
            if (_child != null && _child.HandleKeyboard())
            {
                _keyboardSelection = true;
                return true;
            }

            // Never on the frame it opened. A menu put up from a key - the palette's own
            // pickers are - would otherwise be shut by the character event IMGUI sends after
            // the key that produced it, the press arriving as two events and both being read.
            if (Time.frameCount == _born) return false;

            var e = Event.current;
            if (e == null || e.rawType != EventType.KeyDown || e.keyCode == KeyCode.None)
                return false;
            // A modifier on its own is somebody reaching for a chord, not a key.
            if (Modifier(e.keyCode)) return false;

            _keyboardSelection = true;

            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    e.Use();
                    return true;

                case KeyCode.DownArrow:
                    MoveSelection(1);
                    e.Use();
                    return true;

                case KeyCode.PageUp:
                    MoveSelection(-PageSize);
                    e.Use();
                    return true;

                case KeyCode.PageDown:
                    MoveSelection(PageSize);
                    e.Use();
                    return true;

                case KeyCode.Home:
                    _selected = FindSelectable(0, 1);
                    RevealSelection();
                    e.Use();
                    return true;

                case KeyCode.End:
                    _selected = FindSelectable(_options.Count - 1, -1);
                    RevealSelection();
                    e.Use();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    EnsureSelection();
                    e.Use();
                    ActivateSelection();
                    return true;

                case KeyCode.RightArrow:
                    EnsureSelection();
                    if (IsSelectable(_selected))
                    {
                        var sub = Sub(_selected);
                        if (sub != null) OpenChild(_selected, sub);
                    }
                    e.Use();
                    return true;

                case KeyCode.LeftArrow:
                    if (_parent != null)
                    {
                        var parent = _parent;
                        parent.CloseChild();
                    }
                    e.Use();
                    return true;

                default:
                    CloseTree();
                    return true;
            }
        }

        int PageSize => Mathf.Max(1, Mathf.Min(Mathf.Max(1, _options.Count - 1),
            Mathf.FloorToInt((windowRect.height - PadY * 2f) / RowH)));

        bool IsSelectable(int i) => i >= 0 && i < _options.Count &&
            !(_options[i] is SeparatorOption) && !_options[i].Disabled;

        int FindSelectable(int start, int step)
        {
            if (_options.Count == 0) return -1;
            for (int n = 0; n < _options.Count; n++)
            {
                int i = (start + step * n) % _options.Count;
                if (i < 0) i += _options.Count;
                if (IsSelectable(i)) return i;
            }
            return -1;
        }

        void EnsureSelection()
        {
            if (IsSelectable(_selected)) return;
            _selected = IsSelectable(_hot) ? _hot : FindSelectable(0, 1);
            RevealSelection();
        }

        void MoveSelection(int delta)
        {
            if (_options.Count == 0) return;

            int direction = delta < 0 ? -1 : 1;
            int steps = Mathf.Abs(delta);
            if (!IsSelectable(_selected))
            {
                _selected = IsSelectable(_hot) ? _hot :
                    FindSelectable(direction > 0 ? 0 : _options.Count - 1, direction);
                // With no hover, the first arrow establishes the nearest sensible end.
                // With a hover, it advances from the row the pointer is already on.
                if (!IsSelectable(_hot)) steps = 0;
            }

            for (int n = 0; n < steps; n++)
            {
                int next = FindSelectable(_selected + direction, direction);
                if (next < 0 || next == _selected) break;
                _selected = next;
            }
            RevealSelection();
        }

        void RevealSelection()
        {
            if (!IsSelectable(_selected)) return;

            float top = 0f;
            for (int i = 0; i < _selected; i++) top += Height(_options[i]);
            _scroll.Reveal(top, Height(_options[_selected]),
                Mathf.Max(1f, windowRect.height - PadY * 2f));
        }

        void ActivateSelection()
        {
            if (!IsSelectable(_selected)) return;

            var sub = _options[_selected] as UiSubmenu;
            if (sub != null)
            {
                OpenChild(_selected, sub);
                return;
            }

            var option = _options[_selected];
            SoundDefOf.Click.PlayOneShotOnCamera();
            CloseTree();
            if (option.action != null) option.action();
        }

        public override void OnAcceptKeyPressed()
        {
            _acceptEvent = Event.current;
            EnsureSelection();
            Event.current.Use();
            ActivateSelection();
        }

        static bool Modifier(KeyCode k) =>
            k == KeyCode.LeftShift || k == KeyCode.RightShift ||
            k == KeyCode.LeftControl || k == KeyCode.RightControl ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt ||
            k == KeyCode.LeftCommand || k == KeyCode.RightCommand;
    }
}
