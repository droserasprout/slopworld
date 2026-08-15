using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Owns the event boundary around TerminalWindow. The window remains responsible for
    // terminal state, selection, and rendering; this collaborator owns the input dispatch
    // tables and makes the order of IMGUI's mouse/key paths explicit.
    internal sealed class TerminalInputHandler
    {
        readonly TerminalWindow _window;
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _local;
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _terminal;

        public TerminalInputHandler(TerminalWindow window)
        {
            _window = window;
            _local = new Dictionary<KeyCode, System.Func<Event, bool>>
            {
                { KeyCode.Escape, window.HandleEscapeKey },
                { KeyCode.Return, window.HandleReturnKey },
            };
            _terminal = new Dictionary<KeyCode, System.Func<Event, bool>>
            {
                { KeyCode.Escape, window.ForwardMappedKey },
                { KeyCode.Return, window.ForwardMappedKey },
                { KeyCode.KeypadEnter, window.ForwardMappedKey },
                { KeyCode.Backspace, window.ForwardMappedKey },
                { KeyCode.Tab, window.ForwardMappedKey },
                { KeyCode.UpArrow, window.ForwardMappedKey },
                { KeyCode.DownArrow, window.ForwardMappedKey },
                { KeyCode.LeftArrow, window.ForwardMappedKey },
                { KeyCode.RightArrow, window.ForwardMappedKey },
                { KeyCode.Home, window.ForwardMappedKey },
                { KeyCode.End, window.ForwardMappedKey },
                { KeyCode.PageUp, window.ForwardMappedKey },
                { KeyCode.PageDown, window.ForwardMappedKey },
                { KeyCode.Delete, window.ForwardMappedKey },
                { KeyCode.Insert, window.ForwardMappedKey },
                { KeyCode.F1, window.ForwardMappedKey },
                { KeyCode.F2, window.ForwardMappedKey },
                { KeyCode.F3, window.ForwardMappedKey },
                { KeyCode.F4, window.ForwardMappedKey },
                { KeyCode.F5, window.ForwardMappedKey },
                { KeyCode.F6, window.ForwardMappedKey },
                { KeyCode.F7, window.ForwardMappedKey },
                { KeyCode.F8, window.ForwardMappedKey },
                { KeyCode.F9, window.ForwardMappedKey },
                { KeyCode.F10, window.ForwardMappedKey },
                { KeyCode.F11, window.ForwardMappedKey },
                { KeyCode.F12, window.ForwardMappedKey },
                { KeyCode.C, window.HandleControlC },
                { KeyCode.V, window.HandleControlV },
                { KeyCode.Semicolon, window.HandleSemicolonKey },
                { KeyCode.Colon, window.HandleSemicolonKey },
                { KeyCode.A, window.ForwardMappedKey },
                { KeyCode.B, window.ForwardMappedKey },
                { KeyCode.D, window.ForwardMappedKey },
                { KeyCode.E, window.ForwardMappedKey },
                { KeyCode.F, window.ForwardMappedKey },
                { KeyCode.G, window.ForwardMappedKey },
                { KeyCode.H, window.ForwardMappedKey },
                { KeyCode.I, window.ForwardMappedKey },
                { KeyCode.J, window.ForwardMappedKey },
                { KeyCode.K, window.ForwardMappedKey },
                { KeyCode.L, window.ForwardMappedKey },
                { KeyCode.M, window.ForwardMappedKey },
                { KeyCode.N, window.ForwardMappedKey },
                { KeyCode.O, window.ForwardMappedKey },
                { KeyCode.P, window.ForwardMappedKey },
                { KeyCode.Q, window.ForwardMappedKey },
                { KeyCode.R, window.ForwardMappedKey },
                { KeyCode.S, window.ForwardMappedKey },
                { KeyCode.T, window.ForwardMappedKey },
                { KeyCode.U, window.ForwardMappedKey },
                { KeyCode.W, window.ForwardMappedKey },
                { KeyCode.X, window.ForwardMappedKey },
                { KeyCode.Y, window.ForwardMappedKey },
                { KeyCode.Z, window.ForwardMappedKey },
            };
        }

        public void Handle(Rect body)
        {
            var e = Event.current;
            // WindowStack's high-priority pass may mark a key Used before the window body
            // runs. Keep the semicolon that pass swallowed; no other Used event is replayed.
            if (e.type == EventType.Used && e.rawType == EventType.KeyDown &&
                (e.character == ';' || TerminalWindow.IsSemicolonKey(e.keyCode)))
            {
                _window.HandleKey(e);
                return;
            }

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    _window.HandleWheel(body, e);
                    return;
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.MouseUp:
                    _window.HandleMouse(body, e);
                    return;
                case EventType.KeyDown:
                    _window.HandleKey(e);
                    return;
            }
        }

        public void HandleChrome(Event e)
        {
            if (e.type != EventType.KeyDown) return;

            if (TerminalWindow.HandleFunctionKey(e)) { e.Use(); return; }

            if (e.keyCode == KeyCode.Escape)
            {
                _window.Leave();
                e.Use();
                return;
            }

            int slot = TerminalHotkeys.SlotKey(e);
            if (slot >= 0 && e.alt)
            {
                _window.SwitchToSlot(slot);
                e.Use();
                return;
            }

            if (e.alt && (e.keyCode == KeyCode.Comma || e.keyCode == KeyCode.Period))
            {
                TerminalWindow.WalkSession(e.keyCode == KeyCode.Period ? 1 : -1);
                e.Use();
            }
        }

        public bool TryLocal(Event e) => Try(_local, e);

        public bool TryTerminal(Event e) => Try(_terminal, e);

        public void CaptureSemicolonInput() => _window.CaptureSemicolonInput();

        static bool Try(Dictionary<KeyCode, System.Func<Event, bool>> handlers, Event e)
        {
            return handlers.TryGetValue(e.keyCode, out var handler) && handler(e);
        }
    }
}
