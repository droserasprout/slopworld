using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Flat rectangular chrome with no textures. Geometry is snapped through GUIToScreenPoint
    // so edges land on the screen pixel grid at non-integer UI scales.
    public static class Slab
    {
        // A border is one screen pixel whatever the UI scale. A line that thickens with the
        // scale turns this deliberately light rectangular frame into a heavy box. Public
        // Two boxes that share an edge must overlap by this width.
        // For example, this prevents a double seam between a menu and its submenu.
        public static float LineW => 1f / Prefs.UIScale;

        // What a focus ring is worth: two screen pixels, outside the widget. Therefore, the control
        // keeps the rect it was handed and the ring is the only thing that grew.
        static float RingW => 2f / Prefs.UIScale;

        // A box in one color.
        public static void Fill(Rect r, Color c) => Flat(r, c);

        // The same box's border and nothing inside it, laid inside the rect so an outlined
        // box covers exactly what it was given.
        public static void Outline(Rect r, Color c)
        {
            if (!Paint(c)) return;

            r = Snap(r);
            float w = LineW;

            Flat(new Rect(r.x, r.y, r.width, w), c);
            Flat(new Rect(r.x, r.yMax - w, r.width, w), c);
            Flat(new Rect(r.x, r.y + w, w, r.height - w * 2f), c);
            Flat(new Rect(r.xMax - w, r.y + w, w, r.height - w * 2f), c);
        }

        // A face and the line round it. Every control here is this: a button, a field, a
        // panel and a check box differ by which two colors they hand over, and by nothing
        // else. Flat, with no relief: a pressed button is a darker face rather than a box
        // that moves.
        public static void Box(Rect r, Color face, Color edge)
        {
            Fill(r, face);
            Outline(r, edge);
        }

        // The keyboard's answer to hover: a ring hugging the outside of the control, in the accent.
        // Drawn as four bands rather than as the outline of a larger rect. Therefore, it is exactly
        // `RingW` on every side however the rect underneath it snapped.
        public static void Ring(Rect r, Color c)
        {
            if (!Paint(c)) return;

            r = Snap(r);
            float w = RingW;

            Flat(new Rect(r.x - w, r.y - w, r.width + w * 2f, w), c);
            Flat(new Rect(r.x - w, r.yMax, r.width + w * 2f, w), c);
            Flat(new Rect(r.x - w, r.y, w, r.height), c);
            Flat(new Rect(r.xMax, r.y, w, r.height), c);
        }

        // Draw a one-screen-pixel top rule regardless of UI scale.
        public static void Hairline(Rect r, Color c) =>
            Flat(new Rect(r.x, r.y, r.width, LineW), c);

        // The vertical counterpart, for a panel boundary that stays one screen pixel wide.
        public static void VHairline(Rect r, Color c) =>
            Flat(new Rect(r.x, r.y, LineW, r.height), c);

        // Nothing to do for a color that is not there - a ghost at rest asks for all of this in
        // nothing. It is a bill for no box.
        static bool Paint(Color c) =>
            Event.current.type == EventType.Repaint && c.a > 0f;

        // Snap both edges of each axis in screen space. Nested GUI groups make arithmetic
        // inversion through ScreenToGUIPoint unreliable. TerminalWindow uses the same transform.
        static Rect Snap(Rect r)
        {
            var p0 = GUIUtility.GUIToScreenPoint(Vector2.zero);
            var p1 = GUIUtility.GUIToScreenPoint(Vector2.one);

            float sx = p1.x - p0.x, sy = p1.y - p0.y;
            if (Mathf.Abs(sx) < 0.0001f || Mathf.Abs(sy) < 0.0001f) return r;

            float x0 = (Mathf.Round(r.x * sx + p0.x) - p0.x) / sx;
            float y0 = (Mathf.Round(r.y * sy + p0.y) - p0.y) / sy;
            float x1 = (Mathf.Round(r.xMax * sx + p0.x) - p0.x) / sx;
            float y1 = (Mathf.Round(r.yMax * sy + p0.y) - p0.y) / sy;

            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        // Snap a label baseline to the same screen-pixel grid as Slab geometry.
        public static float SnapY(float y)
        {
            var p0 = GUIUtility.GUIToScreenPoint(Vector2.zero);
            var p1 = GUIUtility.GUIToScreenPoint(Vector2.one);

            float sy = p1.y - p0.y;
            if (Mathf.Abs(sy) < 0.0001f) return y;

            return (Mathf.Round(y * sy + p0.y) - p0.y) / sy;
        }

        static void Flat(Rect r, Color c)
        {
            if (!Paint(c)) return;

            r = Snap(r);
            if (r.width <= 0f || r.height <= 0f) return;

            var was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, BaseContent.WhiteTex);
            GUI.color = was;
        }
    }
}
