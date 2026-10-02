using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The shared scrollbar is a terminal-style rail: almost invisible at rest, with a
    // narrow solid thumb and a wider hit target for reliable mouse use.
    static class UiScrollbar
    {
        public static Rect Track(Rect outer) => new Rect(
            outer.xMax - UiTheme.ScrollTrackXInset,
            outer.y + UiTheme.ScrollTrackYInset,
            UiTheme.ScrollTrackW,
            Mathf.Max(1f, outer.height - UiTheme.ScrollTrackYInset * 2f));

        public static Rect Hit(Rect outer, Rect track) => new Rect(
            outer.xMax - UiTheme.ScrollHitW, track.y, UiTheme.ScrollHitW, track.height);

        public static float ThumbHeight(Rect track, float viewport, float maxScrollOffset) =>
            Mathf.Clamp(track.height * Mathf.Max(0f, viewport) /
                Mathf.Max(0.0001f, viewport + Mathf.Max(0f, maxScrollOffset)),
                Mathf.Min(UiTheme.ScrollMinThumbH, track.height), track.height);

        public static Rect Thumb(Rect track, float height, float normalized) => new Rect(
            track.x + UiTheme.ScrollThumbInset,
            track.y + (track.height - height) * Mathf.Clamp01(normalized),
            track.width - UiTheme.ScrollThumbInset * 2f,
            height);

        public static void Draw(Rect hit, Rect track, Rect thumb, bool held)
        {
            if (Event.current.type != EventType.Repaint) return;

            var rail = new Rect(track.x + (track.width - UiTheme.ScrollRailW) / 2f,
                                track.y, UiTheme.ScrollRailW, track.height);
            Widgets.DrawBoxSolid(rail, UiTheme.ScrollTrough);
            Widgets.DrawBoxSolid(thumb, held
                ? UiTheme.ScrollThumbHeld
                : hit.Contains(Event.current.mousePosition)
                    ? UiTheme.ScrollThumbHover : UiTheme.ScrollThumb);
        }
    }
}
