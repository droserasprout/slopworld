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
        // enough room to read as themselves rather than as blobs.
        static Texture2D _tex;

        public static Texture2D Tex => _tex != null ? _tex : _tex = Build();

        // A as two sloping strokes and a crossbar. Subsampled the way TabIcons builds.
        static bool LetterA(float x, float y)
        {
            // Left leg: top-left to bottom-mid
            float u = (x - 2f) / 13f, v = (y - 3f) / 26f;
            if (u <= 0f) return false;

            float thick = Mathf.Lerp(2.8f, 2f, u);
            if (Mathf.Abs(v - (1f - u)) * 27f > thick) return false;

            // Right leg: top-left to bottom-right, but the right side is sheared
            // Actually: two triangles forming an A shape
            return true; // placeholder
        }

        static Texture2D Build()
        {
            var px = new Color[N * N];

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    // Simple approach: just draw a stylised "Aa" using basic shapes
                    float cx = x + 0.5f, cy = y + 0.5f;

                    // "A" on the left: an inverted V with a crossbar
                    // Left leg: from (3, 27) to (9, 4)
                    // Right leg: from (9, 4) to (15, 27)
                    // Crossbar: across at y = 18

                    float a = 0f;

                    // Left leg of A
                    a = Mathf.Max(a, Stroke(cx, cy, 3f, 4f, 9f, 27f, 2.2f));
                    // Right leg of A
                    a = Mathf.Max(a, Stroke(cx, cy, 15f, 4f, 9f, 27f, 2.2f));
                    // Crossbar of A
                    a = Mathf.Max(a, Stroke(cx, cy, 4.5f, 18f, 13.5f, 18f, 2f));

                    // "a" on the right: a circle with a tail
                    // Bowl
                    a = Mathf.Max(a, Disc(cx, cy, 23f, 18f, 6.5f) ? 1f : 0f);
                    // Tail rising from the bowl
                    a = Mathf.Max(a, Stroke(cx, cy, 23f, 24.5f, 23f, 12f, 2f));

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

        static bool Disc(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }
    }
}