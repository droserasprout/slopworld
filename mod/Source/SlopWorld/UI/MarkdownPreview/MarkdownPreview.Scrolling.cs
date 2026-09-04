using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    sealed class MarkdownScrollView
    {
        readonly SmoothScroll _scroll = new SmoothScroll();

        public Vector2 Position => _scroll.Position;

        public void Reset()
        {
            _scroll.JumpTo(Vector2.zero);
        }

        public void Draw(Rect body, float width, float height, Action<float, float> draw)
        {
            var view = new Rect(0f, 0f, width, Mathf.Max(body.height, height));
            using (WidgetState.Save())
            using (_scroll.Scope(body, view))
            {
                bool repaint = Event.current == null || Event.current.type == EventType.Repaint;
                if (repaint) draw(_scroll.Position.y, _scroll.Position.y + body.height);
            }
        }
    }
}
