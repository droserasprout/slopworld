using UnityEngine;

namespace SlopWorld
{
    // Fit visible ink, not its square atlas tile, into daemon-owned cell bounds.
    // Tall symbols can use the line height without borrowing neighboring columns.
    static class TextSpriteGeometry
    {
        public static void Fit(
            uint ink,
            int slot,
            int columns,
            int rows,
            int cellSize,
            Rect bounds,
            out Rect rect,
            out Rect uv
        )
        {
            float width = (ink >> 16) & 255;
            float height = (ink >> 24) & 255;
            // Use one scale for both axes. Cell height is a limit, not a target:
            // forcing a square symbol to fill a narrow cell distorts its shape.
            float scale = Mathf.Min(bounds.width / width, bounds.height / height) / 1.08f;
            float drawWidth = width * scale;
            float drawHeight = height * scale;
            rect = new Rect(
                bounds.x + (bounds.width - drawWidth) / 2f,
                bounds.y + (bounds.height - drawHeight) / 2f,
                drawWidth,
                drawHeight
            );
            uv = Uv(ink, slot, columns, rows, cellSize);
        }

        public static Rect Uv(uint ink, int slot, int columns, int rows, int cellSize)
        {
            float x = ink & 255;
            float y = (ink >> 8) & 255;
            float width = (ink >> 16) & 255;
            float height = (ink >> 24) & 255;
            return new Rect(
                (slot % columns * cellSize + x) / (columns * cellSize),
                ((rows - slot / columns) * cellSize - y - height) / (rows * cellSize),
                width / (columns * cellSize),
                height / (rows * cellSize)
            );
        }

        public static float FontSize(GUIStyle style, float fallback) =>
            style.fontSize > 0 ? style.fontSize
            : style.font != null && style.font.fontSize > 0 ? style.font.fontSize
            : fallback;

        public static float Baseline(Font font, float size) =>
            font != null && font.fontSize > 0 ? font.ascent * size / font.fontSize : size;
    }
}
