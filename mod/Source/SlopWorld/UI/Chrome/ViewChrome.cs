using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The project headings and their small status notes are shared by the file and git
    // trees. Keeping them here makes both views use the same filter, spacing and typography.
    static class ViewChrome
    {
        const float Indent = 11f;
        static float Pad => UiTheme.GapS;
        static float CellX => UiTheme.GapS;

        static float RowH => UiTheme.TinyRowH;

        public static void Empty(Rect body, string reason)
        {
            float inset = Mathf.Min(CellX, Mathf.Max(0f, body.width) / 2f);
            var r = new Rect(body.x + inset, body.y + Pad,
                Mathf.Max(0f, body.width - inset * 2f), RowH * 3f);
            UiText.PlainStatusLabel(r, reason, UiTheme.Faint, GameFont.Tiny);
        }

        public static float Note(float width, float y, int depth, string text, Color color)
        {
            float available = Mathf.Max(0f, width - Pad);
            // Reserve label space even when a deeply nested tree is squeezed narrow.
            float labelSpace = Mathf.Min(available, UiTheme.LineH * 3f);
            float x = Mathf.Min(CellX + Mathf.Max(0, depth) * Indent, available - labelSpace);
            using (WidgetState.Save())
            {
                GUI.color = color;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                var r = new Rect(x, y, Mathf.Max(0f, available - x), RowH);
                UiText.RowLabel(r, text);
            }
            return y + RowH;
        }
    }
}
