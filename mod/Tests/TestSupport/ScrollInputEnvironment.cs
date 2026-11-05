using UnityEngine;

namespace SlopWorld
{
    // The real scroll owner and router run against a controllable native sample.
    // X11 sampler/recovery have their own native boundary tests.
    static class X11ScrollInput
    {
        public static Vector2 Sample;
        public static bool Available;
        public static int Discards;

        public static bool TryRead(out Vector2 units, bool refresh = false)
        {
            units = Sample;
            return Available;
        }

        public static void DiscardPendingMovement()
        {
            Discards++;
            Sample = Vector2.zero;
            Available = false;
        }
    }

    static class UiScrollbar
    {
        public static Rect Track(Rect outer) => outer;

        public static Rect Hit(Rect outer, Rect track) => outer;

        public static float ThumbHeight(Rect track, float viewport, float max) => 10f;

        public static Rect Thumb(Rect track, float height, float normalized) => track;

        public static void Draw(Rect hit, Rect track, Rect thumb, bool held) { }
    }
}
