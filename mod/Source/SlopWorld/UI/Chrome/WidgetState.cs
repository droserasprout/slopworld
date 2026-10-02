using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Ambient IMGUI state belongs to the caller. Public widgets take this scope before
    // changing any of it so exceptions cannot tint or reflow the rest of a window.
    internal struct WidgetState : IDisposable
    {
        readonly Color _color;
        readonly GameFont _font;
        readonly TextAnchor _anchor;
        readonly bool _wrap;

        WidgetState(Color color, GameFont font, TextAnchor anchor, bool wrap)
        {
            _color = color;
            _font = font;
            _anchor = anchor;
            _wrap = wrap;
        }

        public static WidgetState Save() => new WidgetState(
            GUI.color, Text.Font, Text.Anchor, Text.WordWrap);

        public void Dispose()
        {
            Text.WordWrap = _wrap;
            Text.Anchor = _anchor;
            Text.Font = _font;
            GUI.color = _color;
        }
    }
}
