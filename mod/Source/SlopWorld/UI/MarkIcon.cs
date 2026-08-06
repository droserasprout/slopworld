using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A tick and a cross, drawn in code for the same reason PowerIcon is: a row that says
    // yes or no about itself wants a mark, and there is none in TexButton that is not also
    // a button with its own border. White, so the caller tints them.
    [StaticConstructorOnStartup]
    public static class MarkIcon
    {
        const int N = 32;

        // Half the stroke. Wide enough to still read at the 18px a float-menu row gives it.
        const float Pen = 2.3f;

        static Texture2D _check, _cross, _dot;

        public static Texture2D CheckTex => _check != null ? _check : _check = Build(InCheck);

        public static Texture2D CrossTex => _cross != null ? _cross : _cross = Build(InCross);

        // A filled disc, for a line that says a state as a colour rather than as a word.
        // Baked rather than drawn as a tiny Slab: at ten pixels a rounded rect is still a
        // rounded rect, and what reads as a lamp is a circle.
        public static Texture2D DotTex => _dot != null ? _dot : _dot = Build(InDot);

        static bool InDot(float x, float y)
        {
            const float R = N / 2f - 1f;
            float dx = x - N / 2f, dy = y - N / 2f;
            return dx * dx + dy * dy <= R * R;
        }

        // Two strokes: the short one down into the corner, the long one back out of it and
        // past the height it started at, which is what makes a tick rather than a V.
        static bool InCheck(float x, float y) =>
            OnLine(x, y, 7f, 17f, 13f, 24f) || OnLine(x, y, 13f, 24f, 25f, 8f);

        static bool InCross(float x, float y) =>
            OnLine(x, y, 9f, 9f, 23f, 23f) || OnLine(x, y, 23f, 9f, 9f, 23f);

        // A capsule: anything within Pen of the segment, ends included - which rounds the
        // tips and fills the corner where two strokes meet for free.
        static bool OnLine(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            float ox = x - (ax + t * dx), oy = y - (ay + t * dy);
            return ox * ox + oy * oy <= Pen * Pen;
        }

        // 4x4 supersampled coverage, which is the whole of the antialiasing on strokes that
        // are diagonal everywhere. y is flipped on the way in the way ShieldIcon's is: a
        // texture's rows run up from the bottom, and a mark is written down the way it is
        // read.
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
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
    }
}
