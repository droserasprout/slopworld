using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Own ordered native keyboard events until IMGUI can dispatch them safely.
    // Copy values: native key state and modifiers may change again before dispatch.
    internal sealed class KeyboardEventQueue
    {
        internal readonly record struct Entry(
            KeyCode Key,
            char Character,
            EventModifiers Modifiers,
            EventType Type,
            bool Repeat
        );

        readonly List<Entry> _pending = new List<Entry>();

        internal void Key(KeyCode key, bool pressed, bool repeat, EventModifiers modifiers) =>
            _pending.Add(
                new Entry(
                    key,
                    '\0',
                    modifiers,
                    pressed ? EventType.KeyDown : EventType.KeyUp,
                    repeat
                )
            );

        internal void Text(char character, EventModifiers modifiers) =>
            _pending.Add(new Entry(KeyCode.None, character, modifiers, EventType.KeyDown, false));

        internal Entry[] Drain()
        {
            var batch = _pending.ToArray();
            _pending.Clear();
            return batch;
        }

        internal void Clear() => _pending.Clear();
    }
}
