using UnityEngine;

namespace SlopWorld
{
    // Shared scroll-body geometry only: callers retain their own SmoothScroll lifetime and
    // content measurement because previews, tabs, and trailing fields have different hosts.
    public static class UiScrollBody
    {
        public static Rect View(Rect frame, float contentHeight) =>
            new Rect(0f, 0f, frame.width - UiWidgets.ScrollbarW,
                Mathf.Max(contentHeight, frame.height));
    }
}
