using UnityEngine;

namespace SlopWorld
{
    // Unity adapter for shared, game-free layout geometry.
    public static class UiRect
    {
        public static UiLayoutRect FromRect(Rect rect) =>
            new UiLayoutRect(rect.x, rect.y, rect.width, rect.height);
        public static Rect ToRect(UiLayoutRect rect) =>
            new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
