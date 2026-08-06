using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The sidebar's view selector, and the one switch that sits beside it. Drawn in code for
    // the reason GearIcon states: this install keeps its textures in asset bundles, so a
    // content path is a button somebody's copy draws invisibly. White, so the strip tints
    // them - the selector is grey for the view you are not looking at and white for the one
    // you are, and nothing here should carry a colour of its own.
    //
    // The robot head is not RobotFace_south: that is a pawn's faceplate, coloured, with eye
    // variants, and tinted flat at 18 pixels it is a blob.
    //
    // Everything that lands on the strip is drawn to one budget, because a row of icons is
    // read as a row and one that is half again the size of its neighbour is the only thing
    // anybody sees: the shape fits the 22x22 box centred on Mid, and its covered area comes
    // out near 250 of the 1024 pixels. The two numbers are what "the same size" and "the same
    // weight" mean here - bbox alone lets a solid slab sit beside a hairline and both be "22
    // wide". Bell and Cross are held to neither: they are marks inside a row and cells in a
    // grid, sized by what they sit in rather than by each other.
    [StaticConstructorOnStartup]
    public static class TabIcons
    {
        const int N = 32;

        // The canvas centre, and the half-width of that box. Written down because five shapes
        // now have to agree on them.
        const float Mid = N / 2f, Box = 11f;

        static Texture2D _agents, _files, _hidden, _bell, _auto, _hamburger, _config;

        public static Texture2D AgentsTex => _agents != null ? _agents : _agents = Build(Robot);

        public static Texture2D FilesTex => _files != null ? _files : _files = Build(Folder);

        public static Texture2D HiddenTex => _hidden != null ? _hidden : _hidden = Build(Eye);

        public static Texture2D BellTex => _bell != null ? _bell : _bell = Build(Bell);

        public static Texture2D HamburgerTex =>
            _hamburger != null ? _hamburger : _hamburger = Build(Hamburger);

        public static Texture2D AutoTex => _auto != null ? _auto : _auto = Build(Cross);

        public static Texture2D ConfigTex => _config != null ? _config : _config = Build(Cog);

        // ------------------------------------------------------------------ shapes
        //
        // All of them are written top-down, the way they are read: Build flips y on the way in,
        // Unity's own origin being the bottom left.

        // A plate with two eyes and a mouth slot punched out of it, and a stub of an aerial.
        // The same face the pawns wear, said in four shapes.
        //
        // The aerial is what costs this one: it takes four of the twenty-two off the top, so
        // the plate is the smallest face on the strip. Left that way rather than sized off the
        // plate alone, which would stand the knob out past everything beside it.
        static bool Robot(float x, float y)
        {
            if (Rect(x, y, 15.3f, 7.2f, 16.7f, 10.5f)) return true;  // the aerial
            if (Disc(x, y, 16f, 7.2f, 2f)) return true;              // and its knob

            if (!RRect(x, y, 6.5f, 9.5f, 25.5f, 27f, 5.6f)) return false;  // the plate

            if (Disc(x, y, 12.2f, 16.9f, 2.5f)) return false;        // eyes
            if (Disc(x, y, 19.8f, 16.9f, 2.5f)) return false;
            return !RRect(x, y, 11.25f, 21.5f, 20.75f, 24.2f, 1.35f); // mouth
        }

        // A folder: a tab at the top and a body below it. More recognisably
        // files-related than a spine with three leaves, which read as a list.
        //
        // The slot under the flap is the front panel's edge. Without it the body is an
        // unbroken slab, and a slab is the one thing here with no interior line at all - at
        // eighteen pixels the flap alone is not enough to keep it from reading as a rectangle.
        static bool Folder(float x, float y)
        {
            // The tab (the little flap)
            if (Rect(x, y, 10.5f, 6f, 19.7f, 9f)) return true;
            // The body (the main rectangle), minus the line across the front of it
            if (!RRect(x, y, 5f, 9f, 27f, 26f, 2.3f)) return false;
            return !Rect(x, y, 7.2f, 11.5f, 24.8f, 13f);
        }

        // A spine with three leaves off it. Used for the hamburger menu button
        // now that the files tab uses the folder icon.
        static bool Hamburger(float x, float y)
        {
            if (Rect(x, y, 6f, 5f, 8.2f, 27f)) return true;        // the spine
            for (int i = 0; i < 3; i++)
            {
                float row = 9.5f + i * 7.4f;
                if (Rect(x, y, 8.2f, row - 1.1f, 13f, row + 1.1f)) return true;
                if (Rect(x, y, 13f, row - 2.2f, 26f, row + 2.2f)) return true;
            }
            return false;
        }

        // The config door. The same cog GearIcon draws - the outer radius steps with the
        // angle, so both edges fall out of one distance - but stated as a predicate and put
        // through Build, so it antialiases the way its neighbours do. GearIcon keeps its own
        // copy: the options page draws it alone at 20 pixels, where running to the edge of the
        // canvas is right and this budget would only make it small.
        static bool Cog(float x, float y)
        {
            const float Tip = Box, Root = 7.9f, Hole = 3.5f, Teeth = 6f;

            float dx = x - Mid, dy = y - Mid;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r < Hole) return false;                            // the bore

            float t = Mathf.Cos(Teeth * Mathf.Atan2(dy, dx));
            return r <= Mathf.Lerp(Root, Tip,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.3f, 0.3f, t)));
        }

        // The dotfile switch. A lens is the two discs' overlap, and the ring is that lens
        // minus a slightly smaller one drawn off the same centres. The thinnest thing on the
        // strip whatever is done to it - an eye is mostly the hole - so it is drawn to the
        // full width of the box and given the thicker of the two rings that still close.
        static bool Eye(float x, float y)
        {
            if (Disc(x, y, Mid, Mid, 2.6f)) return true;           // the pupil
            return Lens(x, y, 13.3f) && !Lens(x, y, 10.7f);
        }

        // Centres seven and a half above and below, so the lens opens to nearly six while its
        // corners still land on the box - any narrower and the ring closes over the pupil at
        // the size this is actually drawn.
        static bool Lens(float x, float y, float r) =>
            Disc(x, y, Mid, Mid + 7.5f, r) && Disc(x, y, Mid, Mid - 7.5f, r);

        // An agent that rang. Drawn in code for the reason the other three are, and a bell
        // rather than a plain dot because it is one of several marks a row can carry and the
        // shape is what tells them apart at ten pixels.
        //
        // A dome with straight sides under it, a rim wider than both, and the clapper hanging
        // below - the silhouette everything from a hand bell to a notification badge shares.
        static bool Bell(float x, float y)
        {
            if (Rect(x, y, 14.6f, 3.5f, 17.4f, 7.5f)) return true;      // the handle
            if (RRect(x, y, 5.5f, 21f, 26.5f, 24.5f, 1.7f)) return true; // the rim
            if (Disc(x, y, 16f, 26.6f, 2.7f)) return true;              // the clapper
            // The dome, cut off where the straight sides take over, and those sides under it.
            if (y <= 15f) return Disc(x, y, 16f, 15f, 8f);
            return Rect(x, y, 8f, 15f, 24f, 21f);
        }

        // The usage picker's first cell, which is "no choice, you pick". A cross rather than
        // a thing, every other cell in that grid being an item's own icon, and an X rather
        // than a plus because a plus in a grid of things reads as one more of them.
        //
        // Two diagonal bars, clipped round so all four arms end on the same circle: cut to a
        // square they run longer on the diagonal than a bar's width accounts for and the X
        // comes out with a pinched middle.
        static bool Cross(float x, float y)
        {
            if (!Disc(x, y, 16f, 16f, 12.5f)) return false;
            return Mathf.Abs(x - y) <= 3f || Mathf.Abs(x + y - 32f) <= 3f;
        }

        static bool Disc(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        static bool Rect(float x, float y, float x0, float y0, float x1, float y1) =>
            x >= x0 && x <= x1 && y >= y0 && y <= y1;

        static bool RRect(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            if (!Rect(x, y, x0, y0, x1, y1)) return false;
            // Inside the corner boxes, the corner's own disc is the answer; everywhere else
            // the rectangle already was.
            float cx = Mathf.Clamp(x, x0 + r, x1 - r);
            float cy = Mathf.Clamp(y, y0 + r, y1 - r);
            return Disc(x, y, cx, cy, r);
        }

        // 4x4 supersampled coverage, the same way PowerIcon antialiases its triangle.
        static Texture2D Build(Func<float, float, bool> inside)
        {
            const int S = 4;
            var px = new Color[N * N];

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < S; sy++)
                        for (int sx = 0; sx < S; sx++)
                            if (inside(x + (sx + 0.5f) / S, N - (y + (sy + 0.5f) / S)))
                                hits++;

                    if (hits == 0) continue;
                    px[y * N + x] = new Color(1f, 1f, 1f, (float)hits / (S * S));
                }
            }

            var tex = new Texture2D(N, N, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(px);
            tex.Apply();
            // Nothing Unity can see roots a texture built here, so the unload the game runs
            // on any map switch would take it. Same trap as the terminal font.
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
    }
}
