using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A rectangle - filled, outlined, or ringed for focus. Square: the corners are corners,
    // which is the house style and also why there is no texture in this file at all. Every
    // shape here is flat colour off `BaseContent.WhiteTex`, and a solid rect has no texel
    // grid to disagree about, so there is no seam to see and nothing to bake at startup.
    //
    // **Everything here is measured in screen pixels**, which is the whole of what is left to
    // get wrong. A GUI pixel is a screen pixel only at UI scale 1: at 1.75 a rect rounded to
    // whole GUI coordinates lands on 1.75ths of a screen pixel, and where one edge ends and
    // the next begins, the pixel they share is rasterised into one of them, or neither, or
    // both. Both is a doubled alpha - a dark line; neither is the window showing through - a
    // light one. Which of the two, and whether it happens at all, comes out of the box's
    // absolute position, so it appears on some buttons and not on others of the same size in
    // the same window. `Snap` puts every edge on the screen grid before it is drawn, through
    // `GUIToScreenPoint` rather than by multiplying - groups and scroll views each add an
    // offset of their own, and only the transform knows the sum of them.
    public static class Slab
    {
        // A border is one screen pixel whatever the UI scale; a line that thickens with the
        // scale turns this deliberately light rectangular frame into a heavy box.
        static float Line => 1f / Prefs.UIScale;

        // What a focus ring is worth: two screen pixels, outside the widget, so the control
        // keeps the rect it was handed and the ring is the only thing that grew.
        static float RingW => 2f / Prefs.UIScale;

        // A box in one colour.
        public static void Fill(Rect r, Color c) => Flat(r, c);

        // The same box's border and nothing inside it, laid inside the rect so an outlined
        // box covers exactly what it was given.
        public static void Outline(Rect r, Color c)
        {
            if (!Paint(c)) return;

            r = Snap(r);
            float w = Line;

            Flat(new Rect(r.x, r.y, r.width, w), c);
            Flat(new Rect(r.x, r.yMax - w, r.width, w), c);
            Flat(new Rect(r.x, r.y + w, w, r.height - w * 2f), c);
            Flat(new Rect(r.xMax - w, r.y + w, w, r.height - w * 2f), c);
        }

        // A face and the line round it. Every control here is this: a button, a field, a
        // panel and a check box differ by which two colours they hand over, and by nothing
        // else. Flat, with no relief: a pressed button is a darker face rather than a box
        // that moves.
        public static void Box(Rect r, Color face, Color edge)
        {
            Fill(r, face);
            Outline(r, edge);
        }

        // The keyboard's answer to hover: a ring hugging the outside of the control, in the
        // accent. Drawn as four bands rather than as the outline of a larger rect, so it is
        // exactly `RingW` on every side however the rect underneath it snapped.
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

        // A line one screen pixel thick, along the top of the rect it is given. Not a rect a
        // GUI pixel tall: at 1.75 that is 1.75 screen pixels, which rasterises as one row at
        // full alpha and one at three quarters - a rule that is a different weight depending
        // on where the window happens to sit. This is the same trick everything else here
        // does, which is why it lives with them.
        public static void Hairline(Rect r, Color c) =>
            Flat(new Rect(r.x, r.y, r.width, 1f / Prefs.UIScale), c);

        // The vertical counterpart, for a panel boundary that stays one screen pixel wide.
        public static void VHairline(Rect r, Color c) =>
            Flat(new Rect(r.x, r.y, 1f / Prefs.UIScale, r.height), c);

        // Nothing to do for a colour that is not there - a ghost at rest asks for all of this
        // in nothing, and it is a bill for no box.
        static bool Paint(Color c) =>
            Event.current.type == EventType.Repaint && c.a > 0f;

        // Both edges of both axes onto the screen grid, independently. Rounding the origin and
        // the size separately would let a box one pixel wider than its neighbour end half a
        // pixel short of it; rounding the edges means two rects that were handed the same
        // coordinate come back with the same coordinate, which is the property the seams
        // needed and the only one that matters.
        //
        // Out to the screen and back **by arithmetic**, from a transform sampled at two
        // points. `ScreenToGUIPoint` is documented as the inverse of `GUIToScreenPoint` and
        // is not one inside a group: a window is drawn in several nested ones, the return
        // trip drops their offsets, and every box lands somewhere else on the screen while
        // the labels - which go through none of this - stay where they belong.
        //
        // `TerminalWindow.SyncSnap` samples the same transform for the same reason, that
        // being where the pane's black seam between coloured rows came from.
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
