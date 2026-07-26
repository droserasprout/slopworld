using System;
using UnityEngine;

namespace SlopWorld
{
    /// <summary>
    /// The transport pair - a play triangle and a stop square - drawn in code for
    /// the same reason <see cref="TerminalIcon"/> is: two solid shapes are not
    /// worth a PNG in the mod folder. White, so callers can tint them.
    /// </summary>
    public static class PowerIcon
    {
        const int N = 32;

        static Texture2D _start, _stop;

        public static Texture2D StartTex => _start != null ? _start : _start = Build(InTriangle);

        public static Texture2D StopTex => _stop != null ? _stop : _stop = Build(InSquare);

        /// <summary>Points right, sitting on the same optical centre as the square.</summary>
        static bool InTriangle(float x, float y) =>
            x >= 10f && x <= 24f && Mathf.Abs(y - 16f) <= (24f - x) * 0.62f;

        static bool InSquare(float x, float y) =>
            x >= 10f && x <= 22f && y >= 10f && y <= 22f;

        /// <summary>
        /// Fills by 4x4 supersampled coverage, which antialiases the triangle's
        /// slopes for free - the gizmo scales this up well past its 32px, and a
        /// hard-edged hypotenuse reads as a staircase there.
        /// </summary>
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
                            if (inside(x + (sx + 0.5f) / S, y + (sy + 0.5f) / S)) hits++;

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
            return tex;
        }
    }
}
