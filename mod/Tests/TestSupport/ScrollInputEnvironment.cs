using UnityEngine;

namespace SlopWorld
{
    static class UiScrollbar
    {
        public static Rect Track(Rect outer) => outer;

        public static Rect Hit(Rect outer, Rect track) => outer;

        public static float ThumbHeight(Rect track, float viewport, float max) => 10f;

        public static Rect Thumb(Rect track, float height, float normalized) => track;

        public static void Draw(Rect hit, Rect track, Rect thumb, bool held) { }
    }
}
