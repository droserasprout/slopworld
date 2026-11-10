using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Placement policy is shared by initial opening and live menu repositioning.
    internal static class MenuPlacement
    {
        public static Rect Place(
            Rect parent,
            IEnumerable<Rect> ancestors,
            Vector2 wanted,
            float anchor,
            float screenWidth,
            float screenHeight,
            float minWidth,
            float seam
        )
        {
            float roomRight = screenWidth - parent.xMax;
            float roomLeft = parent.x;
            float y = Mathf.Max(0f, Mathf.Min(anchor, screenHeight - wanted.y));
            float rightWidth = Mathf.Min(wanted.x, Mathf.Max(roomRight, minWidth));
            float leftWidth = Mathf.Min(wanted.x, Mathf.Max(roomLeft, minWidth));
            var right = new Rect(Mathf.Max(0f, parent.xMax - seam), y, rightWidth, wanted.y);
            var left = new Rect(Mathf.Max(0f, parent.x - leftWidth + seam), y, leftWidth, wanted.y);
            bool rightClear = right.xMax <= screenWidth;
            bool leftClear = left.xMax <= screenWidth;
            foreach (var ancestor in ancestors)
            {
                rightClear &= !Overlaps(right, ancestor, seam);
                leftClear &= !Overlaps(left, ancestor, seam);
            }

            // A shared border is intentional. Prefer a clear side even if it needs to
            // narrow; when both sides are clear or blocked, retain the screen-space policy.
            bool onRight =
                rightClear != leftClear
                    ? rightClear
                    : wanted.x <= roomRight || roomRight >= roomLeft;
            return onRight ? right : left;
        }

        static bool Overlaps(Rect a, Rect b, float seam) =>
            Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.x, b.x) > seam
            && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.y, b.y) > 0f;
    }
}
