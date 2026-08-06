using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A rounded rectangle - filled, outlined, or raised off the surface with a shadow under
    // it. Drawn in code for the reason every other texture here is: this install keeps its
    // art in asset bundles, so a content path is a widget somebody's copy draws invisibly.
    //
    // **Only the corners are a texture.** The obvious build - one rounded box cut into nine
    // and stretched - draws a 1px line along every one of its eight internal seams. Bilinear
    // filtering samples half a texel outside each tile at its edges, and a tile boundary is
    // where two of those errors meet; at button sizes that is four faint lines across the
    // face and four more around it. Insetting the texture coordinates trades them for soft
    // corners, and point filtering trades them for jagged ones.
    //
    // So nothing with shape in it is ever stretched. Four corner textures are drawn at their
    // own size, and the straight runs between them are flat colour off `BaseContent.WhiteTex`
    // - a solid rect has no texel grid to disagree about, so there is no seam to see.
    //
    // **Everything here is measured in screen pixels**, and that is the whole of the second
    // seam. A GUI pixel is a screen pixel only at UI scale 1: at 1.75 a rect rounded to whole
    // GUI coordinates lands on 1.75ths of a screen pixel, and where a corner ends at 8.75 and
    // the run beside it begins there, the pixel they share is rasterised into one of them, or
    // neither, or both. Both is a doubled alpha - a dark line; neither is the window showing
    // through - a light one. Which of the two, and whether it happens at all, comes out of the
    // button's absolute position, so it appears on some buttons and not on others of the same
    // size in the same window. `Snap` puts every edge on the screen grid before it is drawn,
    // through `GUIToScreenPoint` rather than by multiplying - groups and scroll views each
    // add an offset of their own, and only the transform knows the sum of them.
    [StaticConstructorOnStartup]
    public static class Slab
    {
        // The corner radius, in GUI pixels. Small enough that a 22px row button still reads as
        // a rectangle with the corners taken off rather than as a pill.
        const float R = 5f;

        // The outline's width, laid inside the border so an outlined box covers exactly the
        // rect it was handed. 1.3 rather than 1: the stroke is a curve here, and at exactly 1
        // the antialiasing spreads it over two half-lit rows and it reads as a smear.
        const float Stroke = 1.3f;

        // Under a raised box, and along the top inside it. The pair is the whole of the
        // relief: light where a surface faces up, dark where it hangs over what is behind.
        static readonly Color Shadow = new Color(0f, 0f, 0f, 0.30f);
        static readonly Color Sheen = new Color(1f, 1f, 1f, 0.07f);

        // Indexed by corner: 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right. Baked at
        // the screen resolution they will be drawn at and redone when that changes, so the
        // curve is never resampled - at 1.75 a 5px texture stretched to 8.75 is the one soft
        // thing on a button whose every other edge is exact.
        static Texture2D[] _fill, _ring;
        static int _bakedPx;

        // The corner's size in screen pixels, and the same size back in GUI pixels - which is
        // what the geometry below is laid out in, so that a corner is a whole number of screen
        // pixels however odd the scale.
        static int CornerPx => Mathf.Max(2, Mathf.RoundToInt(R * Prefs.UIScale));

        static float Rg => CornerPx / Prefs.UIScale;

        // A border a whole screen pixel thick, and at least one. At 1.75 that is two, which is
        // what keeps it the same weight as the stroke baked into the corners beside it.
        static float Line => Mathf.Max(1f, Mathf.Round(Stroke * Prefs.UIScale)) / Prefs.UIScale;

        static void Ensure()
        {
            int want = CornerPx;
            if (_fill != null && _bakedPx == want) return;

            _bakedPx = want;
            _fill = Build(false, want);
            _ring = Build(true, want);
        }

        // A box in one colour.
        public static void Fill(Rect r, Color c)
        {
            if (!Paint(ref r, c)) return;

            var was = GUI.color;
            GUI.color = c;
            float k = Rg;

            Corners(r, _fill, k);

            // The straight runs: a band down the middle at full width, and the two strips
            // between the corners above and below it.
            Flat(new Rect(r.x, r.y + k, r.width, r.height - k * 2f));
            Flat(new Rect(r.x + k, r.y, r.width - k * 2f, k));
            Flat(new Rect(r.x + k, r.yMax - k, r.width - k * 2f, k));

            GUI.color = was;
        }

        // The same box's border and nothing inside it.
        public static void Outline(Rect r, Color c)
        {
            if (!Paint(ref r, c)) return;

            var was = GUI.color;
            GUI.color = c;
            float k = Rg, w = Line;

            Corners(r, _ring, k);

            Flat(new Rect(r.x + k, r.y, r.width - k * 2f, w));
            Flat(new Rect(r.x + k, r.yMax - w, r.width - k * 2f, w));
            Flat(new Rect(r.x, r.y + k, w, r.height - k * 2f));
            Flat(new Rect(r.xMax - w, r.y + k, w, r.height - k * 2f));

            GUI.color = was;
        }

        // A face and the line round it, flat on the surface. What an inset control wants - a
        // box to type in is a hole, and a hole does not cast a shadow.
        public static void Box(Rect r, Color face, Color edge)
        {
            Fill(r, face);
            Outline(r, edge);
        }

        // The same, sitting a pixel above what is behind it: a hairline of shadow along the
        // bottom outside, and one of light along the top inside. Pressed, both go - the box
        // is level with the surface again, which is the whole of the animation.
        //
        // The shadow is a **line under the box**, not a copy of the box shifted down. The
        // first pass did the latter, which is right only for an opaque face: through a face
        // at any transparency the whole shadow reads through, and what shows is its corner
        // arcs a pixel out of step with the ones over them - two dark verticals down each
        // end of the button, worst where the button is short enough that they nearly meet.
        public static void Raised(Rect r, Color face, Color edge, bool pressed = false)
        {
            Fill(r, face);
            Outline(r, edge);

            if (pressed) return;
            float k = Rg, w = Line;
            Flat(new Rect(r.x + k, r.yMax, r.width - k * 2f, w), Shadow);
            Flat(new Rect(r.x + k, r.y + w, r.width - k * 2f, w), Sheen);
        }

        // Nothing to do for a colour that is not there - a ghost at rest asks for all of this
        // in nothing, and it is a bill for no box.
        static bool Paint(ref Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return false;
            if (c.a <= 0f) return false;

            Ensure();
            r = Snap(r);

            // Too small to round: the corners would overlap and each draw the other's curve
            // backwards. A plain rect is the honest answer at that size anyway.
            if (r.width < Rg * 2f || r.height < Rg * 2f)
            {
                var was = GUI.color;
                GUI.color = c;
                GUI.DrawTexture(r, BaseContent.WhiteTex);
                GUI.color = was;
                return false;
            }
            return true;
        }

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

        static void Corners(Rect r, Texture2D[] tex, float k)
        {
            GUI.DrawTexture(Snap(new Rect(r.x, r.y, k, k)), tex[0]);
            GUI.DrawTexture(Snap(new Rect(r.xMax - k, r.y, k, k)), tex[1]);
            GUI.DrawTexture(Snap(new Rect(r.x, r.yMax - k, k, k)), tex[2]);
            GUI.DrawTexture(Snap(new Rect(r.xMax - k, r.yMax - k, k, k)), tex[3]);
        }

        static void Flat(Rect r)
        {
            r = Snap(r);
            if (r.width > 0f && r.height > 0f) GUI.DrawTexture(r, BaseContent.WhiteTex);
        }

        static void Flat(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var was = GUI.color;
            GUI.color = c;
            Flat(r);
            GUI.color = was;
        }

        // The four rotations of one corner, baked rather than flipped at draw time: a
        // negative-width texture coordinate mirrors about a texel edge and lands the curve
        // half a pixel off on two of the four, which is the same hairline this file exists to
        // avoid. Four textures of a dozen pixels a side is nothing to keep.
        static Texture2D[] Build(bool ring, int n)
        {
            var made = new Texture2D[4];
            for (int i = 0; i < 4; i++)
                made[i] = Bake(ring, n, flipX: i % 2 == 1, flipY: i >= 2);
            return made;
        }

        // 4x4 supersampled coverage, the same as MarkIcon's and for the same reason: the only
        // edge here is a curve, and a curve without it is a staircase.
        static Texture2D Bake(bool ring, int n, bool flipX, bool flipY)
        {
            const int S = 4;
            var px = new Color[n * n];

            // The stroke in this texture's own pixels, which are screen pixels.
            float pen = Mathf.Max(1f, Stroke * n / R);

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < S; sy++)
                        for (int sx = 0; sx < S; sx++)
                        {
                            float ax = x + (sx + 0.5f) / S, ay = y + (sy + 0.5f) / S;
                            // The arc's centre is the inner corner of this tile; mirroring
                            // moves it to whichever corner this copy is drawn in.
                            float dx = flipX ? ax : n - ax;
                            float dy = flipY ? ay : n - ay;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d <= n && (!ring || d >= n - pen)) hits++;
                        }

                    if (hits == 0) continue;
                    // Texture rows run up from the bottom and a rect's run down from the top,
                    // so what is baked as the top row is written into the last.
                    px[(n - 1 - y) * n + x] = new Color(1f, 1f, 1f, (float)hits / (S * S));
                }
            }

            var tex = new Texture2D(n, n, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(px);
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
    }
}
