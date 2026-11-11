using System;
using UnityEngine;

namespace SlopWorld
{
    // Timed text sequence, independent of game state and drawing. The presenter owns
    // the clock and optional request lifetime; repeated GUI passes cannot advance early.
    internal sealed class UiBubbleSequence
    {
        const float Timeout = 3f;
        float _startedAt;
        int _remaining;
        Func<string> _next;
        Func<bool> _alive;

        internal string Text { get; private set; }
        internal Vector2 Anchor { get; private set; }

        internal void Show(
            string text,
            Vector2 anchor,
            float now,
            int followingHints = 0,
            Func<string> next = null,
            Func<bool> alive = null
        )
        {
            if (text == null)
            {
                Clear();
                return;
            }
            Text = text;
            Anchor = anchor;
            _startedAt = now;
            _remaining = followingHints;
            _next = next;
            _alive = alive;
        }

        internal bool Advance(float now)
        {
            if (Text == null)
                return false;
            if (_alive != null && !_alive())
            {
                Clear();
                return false;
            }
            if (now - _startedAt >= Timeout)
            {
                if (_remaining <= 0 || _next == null)
                {
                    Clear();
                    return false;
                }
                Text = _next();
                if (Text == null)
                {
                    Clear();
                    return false;
                }
                _remaining--;
                // A delayed repaint must still give the next hint its full three seconds.
                _startedAt = now;
            }
            return true;
        }

        internal void Clear()
        {
            Text = null;
            _remaining = 0;
            // Guards and content callbacks may capture windows; release them on retirement.
            _next = null;
            _alive = null;
        }
    }
}
