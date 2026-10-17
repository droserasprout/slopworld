using UnityEngine;

namespace SlopWorld
{
    // Shared scroll-body geometry only: callers retain their own SmoothScroll lifetime and
    // content measurement because previews, tabs, and trailing fields have different hosts.
    public static class UiScrollBody
    {
        public static UiScrollBodyGeometry Measure(
            Rect frame,
            float contentHeight,
            UiScrollbarReservation reservation,
            float scrollbarWidth = UiTheme.ScrollbarW
        )
        {
            var layout = ScrollableGeometry.Measure(
                new UiLayoutRect(frame.x, frame.y, frame.width, frame.height),
                contentHeight,
                reservation,
                scrollbarWidth
            );
            return new UiScrollBodyGeometry(
                layout,
                new Rect(0f, 0f, layout.View.Width, layout.View.Height)
            );
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

        // Compact list rows paint up to the rail, but their controls and click regions
        // must leave the scrollbar's wider, invisible mouse target unobstructed.
        public float ScrollbarHitInset =>
            ReservesScrollbar ? Mathf.Max(0f, UiTheme.ScrollHitW - UiTheme.ScrollTrackXInset) : 0f;
    }
}
