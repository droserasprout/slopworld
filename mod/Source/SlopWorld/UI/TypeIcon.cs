using UnityEngine;
using Verse;

namespace SlopWorld
{
    // An "Aa" monogram for the Appearance tab, drawn in code for the reason GearIcon and
    // TerminalIcon give: this install keeps its textures in asset bundles, so a content
    // path that resolves to null draws a button you cannot see.
    [StaticConstructorOnStartup]
    public static class TypeIcon
    {
        const int N = 32;

        // The two letters share the box: "A" on the left, "a" on the right, both with
        // enough room to read as themselves rather than as blobs. Drawn a sample per pixel
        // rather than subsampled the way TabIcons does it: every shape here is an
        // anti-aliased distance field already, so the edges come out soft without it.
        static Texture2D _tex;

        public static Texture2D Tex => _tex != null ? _tex : _tex = Build();

        static Texture2D Build()
        {
            var px = new Color[N * N];

            // The shapes below are written the way the letters read - y down from the cap
            // line - while SetPixels fills from the bottom row up. `N - y` is what turns
            // one into the other, the same conversion TabIcons.Build does; without it the
            // A stands on its apex and reads as a V.
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float cx = x + 0.5f, cy = N - (y + 0.5f);

                    // "A" on the left: an inverted V with a crossbar.
                    // Left leg from (3, 27) to the apex at (9, 4), right leg back down to
                    // (15, 27), crossbar across at y = 18.
                    float a = 0f;
                    a = Mathf.Max(a, Stroke(cx, cy, 3f, 27f, 9f, 4f, 2.2f));
                    a = Mathf.Max(a, Stroke(cx, cy, 9f, 4f, 15f, 27f, 2.2f));
                    a = Mathf.Max(a, Stroke(cx, cy, 4.5f, 18f, 13.5f, 18f, 2f));

                    // "a" on the right: a ring for the bowl, not a disc - a filled circle
                    // is a bullet, and the counter is most of what makes it a letter.
                    a = Mathf.Max(a, Ring(cx, cy, 23f, 19f, 6f, 2f));
                    // The stem down the right of the bowl, and its foot.
                    a = Mathf.Max(a, Stroke(cx, cy, 29f, 13f, 29f, 27f, 2f));

                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
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

        // Anti-aliased distance to a line segment
        static float Stroke(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float lenSq = dx * dx + dy * dy;
            if (lenSq == 0f) return 0f;

            float t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / lenSq);
            float px = x0 + t * dx, py = y0 + t * dy;
            float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            return Mathf.Clamp01(r + 0.5f - d);
        }

        // Anti-aliased annulus: `r` is the centre line of a stroke `w` wide, so the counter
        // inside stays open. Same falloff as Stroke, being the same distance question.
        static float Ring(float x, float y, float cx, float cy, float r, float w)
        {
            float dx = x - cx, dy = y - cy;
            float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r);
            return Mathf.Clamp01(w * 0.5f + 0.5f - d);
        }
    }
}
