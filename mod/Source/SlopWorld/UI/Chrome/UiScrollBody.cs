using UnityEngine;

namespace SlopWorld
{
    // Shared scroll-body geometry only: callers retain their own SmoothScroll lifetime and
    // content measurement because previews, tabs, and trailing fields have different hosts.
    public static class UiScrollBody
    {
        public static UiScrollBodyGeometry Measure(Rect frame, float contentHeight,
                                                    UiScrollbarReservation reservation)
        {
            var layout = ScrollableGeometry.Measure(
                new UiLayoutRect(frame.x, frame.y, frame.width, frame.height),
                contentHeight, reservation, UiWidgets.ScrollbarW);
            return new UiScrollBodyGeometry(layout, new Rect(0f, 0f,
                layout.View.Width, layout.View.Height));
        }
    }

    public readonly struct UiScrollBodyGeometry
    {
        public readonly ScrollableGeometry Layout;
        public readonly Rect View;

        public UiScrollBodyGeometry(ScrollableGeometry layout, Rect view)
        {
            Layout = layout;
            View = view;
        }

        public float ContentWidth => Layout.ContentWidth;
        public float ContentHeight => Layout.ContentHeight;
        public bool ReservesScrollbar => Layout.ReservesScrollbar;
    }
}
