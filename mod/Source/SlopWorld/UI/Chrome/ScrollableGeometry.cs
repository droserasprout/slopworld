using System;

namespace SlopWorld
{
    public enum UiScrollbarReservation
    {
        Always,
        WhenNeeded,
    }

    // Pure scroll measurement. The viewport is the frame-sized content area after the named
    // scrollbar policy has reserved its gutter; callers still own the SmoothScroll instance.
    public readonly struct ScrollableGeometry
    {
        public readonly UiLayoutRect View;
        public readonly float ContentWidth;
        public readonly float ContentHeight;
        public readonly bool ReservesScrollbar;

        public ScrollableGeometry(UiLayoutRect frame, float contentHeight,
                                  UiScrollbarReservation reservation, float scrollbarWidth)
        {
            scrollbarWidth = Math.Max(0f, scrollbarWidth);
            contentHeight = Math.Max(0f, contentHeight);
            ReservesScrollbar = reservation == UiScrollbarReservation.Always ||
                contentHeight > frame.Height;
            ContentWidth = Math.Max(0f, frame.Width -
                (ReservesScrollbar ? scrollbarWidth : 0f));
            ContentHeight = Math.Max(frame.Height, contentHeight);
            View = new UiLayoutRect(0f, 0f, ContentWidth, ContentHeight);
        }

        public static ScrollableGeometry Measure(UiLayoutRect frame, float contentHeight,
                                                 UiScrollbarReservation reservation,
                                                 float scrollbarWidth) =>
            new ScrollableGeometry(frame, contentHeight, reservation, scrollbarWidth);
    }
}
