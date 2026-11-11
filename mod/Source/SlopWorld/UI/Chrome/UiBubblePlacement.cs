using UnityEngine;

namespace SlopWorld
{
    // Shared screen-space placement; pointer motion never changes an existing anchor.
    internal static class UiBubblePlacement
    {
        internal static Rect Place(Vector2 anchor, Vector2 size, Vector2 screen)
        {
            const float margin = 8f;
            float y = anchor.y - size.y - 12f;
            if (y < margin)
                y = anchor.y + 12f;
            y = Mathf.Max(margin, Mathf.Min(y, screen.y - margin - size.y));
            float x = Mathf.Max(
                margin,
                Mathf.Min(anchor.x - size.x / 2f, screen.x - margin - size.x)
            );
            return new Rect(x, y, size.x, size.y);
        }
    }
}
