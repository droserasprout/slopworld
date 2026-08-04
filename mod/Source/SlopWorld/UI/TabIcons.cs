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
    [StaticConstructorOnStartup]
    public static class TabIcons
    {
        const int N = 32;

        static Texture2D _agents, _files, _hidden;

        public static Texture2D AgentsTex => _agents != null ? _agents : _agents = Build(Robot);

        public static Texture2D FilesTex => _files != null ? _files : _files = Build(Tree);

        public static Texture2D HiddenTex => _hidden != null ? _hidden : _hidden = Build(Eye);

        // ------------------------------------------------------------------ shapes
        //
        // All three are written top-down, the way they are read: Build flips y on the way in,
        // Unity's own origin being the bottom left.

        // A plate with two eyes and a mouth slot punched out of it, and a stub of an aerial.
        // The same face the pawns wear, said in four shapes.
        static bool Robot(float x, float y)
        {
            if (Rect(x, y, 15.2f, 2.5f, 16.8f, 8f)) return true;   // the aerial
            if (Disc(x, y, 16f, 3f, 2.4f)) return true;            // and its knob

            if (!RRect(x, y, 6f, 7f, 26f, 26f, 6f)) return false;  // the plate

            if (Disc(x, y, 12f, 15f, 2.7f)) return false;          // eyes
            if (Disc(x, y, 20f, 15f, 2.7f)) return false;
            return !RRect(x, y, 11f, 20f, 21f, 23f, 1.4f);         // mouth
        }

        // A spine with three leaves off it. A folder would say "a directory"; the point of
        // this view is that the directories are nested.
        static bool Tree(float x, float y)
        {
            if (Rect(x, y, 7.2f, 6f, 8.8f, 24.8f)) return true;    // the spine
            for (int i = 0; i < 3; i++)
            {
                float row = 10f + i * 6.5f;
                if (Rect(x, y, 8.8f, row - 0.8f, 13f, row + 0.8f)) return true;
                if (Rect(x, y, 13f, row - 1.8f, 25f, row + 1.8f)) return true;
            }
            return false;
        }

        // The dotfile switch. A lens is the two discs' overlap, and the ring is that lens
        // minus a slightly smaller one drawn off the same centres.
        static bool Eye(float x, float y)
        {
            if (Disc(x, y, 16f, 16f, 2.4f)) return true;           // the pupil
            return Lens(x, y, 13.5f) && !Lens(x, y, 11.5f);
        }

        // Centres eight above and eight below, so the lens opens to five and a half - any
        // narrower and the ring closes over the pupil at the size this is actually drawn.
        static bool Lens(float x, float y, float r) =>
            Disc(x, y, 16f, 24f, r) && Disc(x, y, 16f, 8f, r);

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
