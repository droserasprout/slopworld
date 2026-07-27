using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The ">_" that means terminal everywhere, drawn in code. It is two strokes
    /// and a bar, which is not worth a PNG in the mod folder - and drawn white so
    /// callers can tint it to an agent's state.
    /// </summary>
    // The attribute is only to quiet the startup scan: StaticConstructorOnStartupUtility
    // warns about any type holding a static Texture2D without it, whether or not the
    // texture is built off the main thread. This one is built lazily on first draw,
    // which is as main-thread as it gets, so the attribute changes nothing at runtime.
    [StaticConstructorOnStartup]
    public static class TerminalIcon
    {
        const int N = 32;

        static Texture2D _tex;

        public static Texture2D Tex => _tex != null ? _tex : _tex = Build();

        static Texture2D Build()
        {
            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            // The chevron, drawn from its point so both strokes meet cleanly.
            Stroke(px, 9f, 23f, 17f, 16f, 2.1f);
            Stroke(px, 17f, 16f, 9f, 9f, 2.1f);
            // The prompt bar underneath it.
            Stroke(px, 17f, 8f, 25f, 8f, 1.6f);

            var tex = new Texture2D(N, N, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Lays a stroke down by distance to the segment, which antialiases
        /// the diagonals for free - a plain Bresenham line reads as a staircase once
        /// the gizmo scales this up.</summary>
        static void Stroke(Color[] px, float x0, float y0, float x1, float y1, float half)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len2 = dx * dx + dy * dy;

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float px0 = x + 0.5f - x0, py0 = y + 0.5f - y0;
                    float t = len2 > 0f ? Mathf.Clamp01((px0 * dx + py0 * dy) / len2) : 0f;
                    float ox = px0 - dx * t, oy = py0 - dy * t;
                    float d = Mathf.Sqrt(ox * ox + oy * oy);

                    float a = Mathf.Clamp01(half + 0.5f - d);
                    if (a <= 0f) continue;

                    int i = y * N + x;
                    // Keep the strongest coverage where strokes overlap.
                    if (a > px[i].a) px[i] = new Color(1f, 1f, 1f, a);
                }
            }
        }
    }
}
