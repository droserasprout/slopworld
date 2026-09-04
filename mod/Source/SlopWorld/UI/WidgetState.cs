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

        WidgetState(bool capture)
        {
            _color = GUI.color;
            _font = Text.Font;
            _anchor = Text.Anchor;
            _wrap = Text.WordWrap;
        }

        public static WidgetState Save() => new WidgetState(true);

        public void Dispose()
        {
            Text.WordWrap = _wrap;
            Text.Anchor = _anchor;
            Text.Font = _font;
            GUI.color = _color;
        }
    }
}
