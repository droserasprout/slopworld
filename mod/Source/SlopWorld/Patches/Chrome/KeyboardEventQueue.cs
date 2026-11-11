using System;
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
            bool Repeat,
            long Stroke
        );

        readonly List<Entry> _pending = new List<Entry>();

        readonly Dictionary<KeyCode, long> _strokes = new Dictionary<KeyCode, long>();
        long _nextStroke;
        long _textStroke;
        long _suppressedStroke = -1;
        Entry? _dispatching;

        internal void Key(KeyCode key, bool pressed, bool repeat, EventModifiers modifiers)
        {
            if (pressed && (!repeat || !_strokes.ContainsKey(key)))
                _strokes[key] = ++_nextStroke;
            _strokes.TryGetValue(key, out long stroke);
            if (pressed)
                _textStroke = stroke;
            _pending.Add(
                new Entry(
                    key,
                    '\0',
                    modifiers,
                    pressed ? EventType.KeyDown : EventType.KeyUp,
                    repeat,
                    stroke
                )
            );
        }

        internal void Text(char character, EventModifiers modifiers) =>
            _pending.Add(
                new Entry(KeyCode.None, character, modifiers, EventType.KeyDown, false, _textStroke)
            );

        internal Entry[] Drain()
        {
            var batch = _pending.ToArray();
            _pending.Clear();
            return batch;
        }

        // Text callbacks can arrive after BeginFrame, or after the physical key's
        // release. Retain stroke identity across frames; a fresh press starts a new one.
        internal void SuppressOpeningKey(KeyCode key)
        {
            if (key == KeyCode.None)
                return;
            if (_dispatching is Entry current && current.Key == key)
                _suppressedStroke = current.Stroke;
            else if (_strokes.TryGetValue(key, out long stroke))
                _suppressedStroke = stroke;
        }

        internal void Dispatch(Entry input, Action send)
        {
            if (input.Stroke == _suppressedStroke)
                return;
            var previous = _dispatching;
            _dispatching = input;
            try
            {
                send();
            }
            finally
            {
                _dispatching = previous;
            }
        }

        internal void Clear() => _pending.Clear();

        internal void Reset()
        {
            Clear();
            _strokes.Clear();
            _textStroke = 0;
            _suppressedStroke = -1;
            _dispatching = null;
        }
    }
}
