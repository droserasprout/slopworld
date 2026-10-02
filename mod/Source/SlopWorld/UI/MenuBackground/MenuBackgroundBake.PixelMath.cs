using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Shared deterministic lookup tables, noise and grading for both background effects.
    internal static partial class MenuBackgroundBake
    {
        // Reproducible 256x256 float value-noise LUT. Cheaper to sample than Unity Perlin.
        static readonly float[] _noiseLut = new float[LutSide * LutSide];

        // Tabulated cosine hue palette: branch-free full-circle hues at lower per-pixel cost.
        static readonly Color[] _hueLut = new Color[HueSide];

        static MenuBackgroundBake()
        {
            var bytes = new byte[_noiseLut.Length];
            new System.Random(42).NextBytes(bytes);
            for (int i = 0; i < bytes.Length; i++) _noiseLut[i] = bytes[i] * (1f / 255f);

            const float Turn = 2f * Mathf.PI;
            for (int i = 0; i < HueSide; i++)
            {
                float t = i / (float)HueSide;
                _hueLut[i] = new Color(
                    0.5f + 0.5f * Mathf.Cos(Turn * t),
                    0.5f + 0.5f * Mathf.Cos(Turn * (t + 1f / 3f)),
                    0.5f + 0.5f * Mathf.Cos(Turn * (t + 2f / 3f)),
                    1f);
            }
        }

        // Wrap hue indices because hue is circular. The mask also handles negative values.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Color Hue(float t) => _hueLut[Mathf.FloorToInt(t * HueSide) & HueMask];

        // Combine drain, contrast, and tint into one affine transform per stage.
        struct Grade
        {
            public float rr, rg, rb, ro;
            public float gr, gg, gb, go;
            public float br, bg, bb, bo;
        }

        static Grade Grading(float k) => Affine(
            Mathf.Lerp(1f, 2.3f, k),        // contrast
            Mathf.Lerp(0f, 0.06f, k),       // blacks off the floor: video, not ink
            Mathf.Lerp(0f, 0.75f, k),       // drain toward luminance
            Sick, Mathf.Lerp(0f, 0.42f, k));

        // Cheer raises contrast/blacks and boosts saturation without tint. At k=0 it is identity.
        static Grade Cheer(float k) => Affine(
            Mathf.Lerp(1f, 1.06f, k), Mathf.Lerp(0f, 0.05f, k), Mathf.Lerp(0f, -0.35f, k),
            Color.white, 0f);

        static Grade Affine(float contrast, float lift, float drain, Color toward, float tint)
        {
            // Match Color.grayscale's channel weights.
            const float LR = 0.299f, LG = 0.587f, LB = 0.114f;

            float keep = 1f - drain;
            // Bias for (c - 0.5) * contrast + 0.5 + lift.
            float bias = 0.5f - 0.5f * contrast + lift;

            // Multiply per channel so dark regions stay dark instead of becoming a flat tint.
            float tr = Mathf.Lerp(1f, toward.r, tint), sr = contrast * tr;
            float tg = Mathf.Lerp(1f, toward.g, tint), sg = contrast * tg;
            float tb = Mathf.Lerp(1f, toward.b, tint), sb = contrast * tb;

            return new Grade
            {
                rr = (keep + drain * LR) * sr,
                rg = drain * LG * sr,
                rb = drain * LB * sr,
                gr = drain * LR * sg,
                gg = (keep + drain * LG) * sg,
                gb = drain * LB * sg,
                br = drain * LR * sb,
                bg = drain * LG * sb,
                bb = (keep + drain * LB) * sb,
                ro = bias * tr,
                go = bias * tg,
                bo = bias * tb,
            };
        }

        static Color Shade(ref Grade t, Color c) => Shade(ref t, c.r, c.g, c.b);

        static Color Shade(ref Grade t, float r, float g, float b) => new Color(
            Mathf.Clamp01(r * t.rr + g * t.rg + b * t.rb + t.ro),
            Mathf.Clamp01(r * t.gr + g * t.gg + b * t.gb + t.go),
            Mathf.Clamp01(r * t.br + g * t.bg + b * t.bb + t.bo),
            1f);

        // Bilinear sample of the wrapped 2D LUT. Wrap x and y independently to avoid row seams.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            int x0 = xi & LutMask, x1 = (xi + 1) & LutMask;
            int y0 = (yi & LutMask) * LutSide, y1 = ((yi + 1) & LutMask) * LutSide;

            float n00 = _noiseLut[y0 + x0], n10 = _noiseLut[y0 + x1];
            float n01 = _noiseLut[y1 + x0], n11 = _noiseLut[y1 + x1];
            return n00 + u * (n10 - n00) + v * (n01 - n00 + u * (n00 - n10 - n01 + n11));
        }

        // Stretch x twice as far as y to produce vertical grain rather than cloud-like noise.
        static float[] Fbm(int w, int h, float phase)
        {
            var field = new float[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f, weight = 0f, freq = NoiseFreq, amp = 1f;

                    for (int o = 0; o < Octaves; o++)
                    {
                        sum += amp * Noise(
                            (x * freq * 2f) + phase,
                            (y * freq) + phase);
                        weight += amp;
                        freq *= 2f;
                        amp *= 0.5f;
                    }

                    field[y * w + x] = sum / weight;
                }
            }

            return field;
        }

    }
}
