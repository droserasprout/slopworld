using System;
using UnityEngine;

namespace SlopWorld
{
    // Owns the screen-space tip panel and its clip, independently of vanilla tip-window sizing.
    static class LoadingTipPanel
    {
        public static Rect Frame(int screenWidth, int screenHeight)
        {
            float width = Mathf.Min(540f, Mathf.Max(280f, screenWidth * 0.45f));
            width = Mathf.Min(width, Mathf.Max(1f, screenWidth - 40f));
            return new Rect(
                Mathf.Max(0f, screenWidth - width),
                0f,
                width,
                Mathf.Max(1f, screenHeight)
            );
        }

        public static int VisibleLines(float height, float glyphHeight, float lineAdvance) =>
            Math.Max(1, (int)Math.Floor((height - glyphHeight) / lineAdvance) + 1);

        public static void Draw(Rect frame, Vector2 margin, Action draw)
        {
            var inner = new Rect(
                frame.x + margin.x,
                frame.y + margin.y,
                Mathf.Max(1f, frame.width - margin.x * 2f),
                Mathf.Max(1f, frame.height - margin.y * 2f)
            );
            GUI.BeginGroup(inner);
            try
            {
                draw();
            }
            finally
            {
                GUI.EndGroup();
            }
        }
    }
}
