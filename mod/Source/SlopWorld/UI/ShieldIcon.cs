using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A shield, drawn in code for the same reason GearIcon is: a sandbox is a wall, and
    // there is no wall in TexButton. White, so the caller tints it. The attribute only
    // quiets the startup scan, which warns about any type holding a static Texture2D.
    [StaticConstructorOnStartup]
    public static class ShieldIcon
    {
        const int N = 32;

        static Texture2D _tex;

        public static Texture2D Tex => _tex != null ? _tex : _tex = Build();

        static Texture2D Build()
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
                            if (Inside(x + (sx + 0.5f) / S, N - (y + (sy + 0.5f) / S)))
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

        // A bar across the bottom, and a body that narrows to a point at the top.
        static bool Inside(float x, float y)
        {
            if (x >= 6f && x <= 26f && y >= 23f && y <= 27f) return true;
            if (y < 4f || y > 27f) return false;
            // Half-width shrinks from 10 at the bar to 0 at the point.
            float half = 10f * (1f - (27f - y) / 23f);
            return Mathf.Abs(x - 16f) <= half;
        }
    }
}