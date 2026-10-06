using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Closed-loop sheen and star pixel math. Bake owns textures and returned frames.
    internal static partial class MenuBackgroundBake
    {
        // Sparkle adds closed-loop sheen and stars to the graded source. The caller owns `dst`.
        static void Sparkle(Color[] src, Color[] dst, float[] haze, Spark[] sparks,
                            int w, int h, float s, float k)
        {
            Grade g = Cheer(k);

            // Diagonal ramp keeps the sheen from forming horizontal bars.
            float ramp = SheenCycles / (w + h - 2);
            float sheen = SheenGain * k;

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    Color c = Shade(ref g, src[i]);

                    float m = haze[i] * sheen;
                    if (m > 0f)
                    {
                        // Add chroma around the palette's neutral 0.5 midpoint, not grey light.
                        Color hue = Hue((x + y) * ramp + s);
                        c = new Color(
                            Mathf.Clamp01(c.r + (hue.r - 0.5f) * m),
                            Mathf.Clamp01(c.g + (hue.g - 0.5f) * m),
                            Mathf.Clamp01(c.b + (hue.b - 0.5f) * m),
                            1f);
                    }

                    dst[i] = c;
                }
            }

            Twinkle(dst, sparks, w, h, s, k);
        }

        // A star: where it is, how far it reaches, where it sits on the wheel, and the blink it
        // is in the middle of.
        struct Spark
        {
            public int x, y;
            public float arm;
            public float hue;
            public int rate;      // blinks per loop, whole
            public float phase;   // where in that blink phase zero finds it
        }

        // Use one fixed, uniform constellation for every stage so stars blink without moving.
        static Spark[] Constellation(int w, int h)
        {
            var rng = new System.Random(SparkSeed);

            // Scale star arms with the baked frame relative to MaxSide.
            float scale = Mathf.Max(w, h) / (float)MaxSide;

            var sparks = new Spark[SparkCount];
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = new Spark
                {
                    x = rng.Next(w),
                    y = rng.Next(h),
                    arm = Mathf.Lerp(SparkArmMin, SparkArmMax, (float)rng.NextDouble()) * scale,
                    hue = (float)rng.NextDouble(),
                    rate = rng.Next(SparkRateMin, SparkRateMax + 1),
                    phase = (float)rng.NextDouble(),
                };
            }

            return sparks;
        }

        // Each star is lit by a raised cosine of s, on its own rate and its own phase. Therefore,
        // The constellation is never all up at once and never all down. Squared, because a blink
        // wants a short peak and a long dark where the cosine gives it even halves.
        static void Twinkle(Color[] px, Spark[] sparks, int w, int h, float s, float k)
        {
            for (int i = 0; i < sparks.Length; i++)
            {
                Spark sp = sparks[i];

                float lit = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (sp.rate * s + sp.phase));
                lit *= lit * k;
                if (lit <= 0.004f) continue;    // under the encoder's floor anyway

                Color tint = Hue(sp.hue);
                float amp = lit * SparkGain;

                int arm = Mathf.CeilToInt(sp.arm);
                int x0 = Mathf.Max(0, sp.x - arm), x1 = Mathf.Min(w - 1, sp.x + arm);
                int y0 = Mathf.Max(0, sp.y - arm), y1 = Mathf.Min(h - 1, sp.y + arm);
                float inv = 1f / sp.arm;

                for (int y = y0; y <= y1; y++)
                {
                    int row = y * w;
                    float ay = Mathf.Abs(y - sp.y) * inv;
                    float fy = 1f - ay; if (fy < 0f) fy = 0f; else fy *= fy;
                    float ty = 1f - ay * SparkThick; if (ty < 0f) ty = 0f;

                    for (int x = x0; x <= x1; x++)
                    {
                        float ax = Mathf.Abs(x - sp.x) * inv;
                        float fx = 1f - ax; if (fx < 0f) fx = 0f; else fx *= fx;
                        float tx = 1f - ax * SparkThick; if (tx < 0f) tx = 0f;

                        // The cross is one arm long and thin and the same turned over. Their sum
                        // alone gives a middle no brighter than twice an arm. Therefore, the core
                        // is a separate round term - and it is white rather than tinted, a star
                        // being hot in the middle and colored at the edges.
                        float cross = fx * ty + fy * tx;
                        float d = 1f - Mathf.Sqrt(ax * ax + ay * ay);
                        float core = d > 0f ? d * d * d : 0f;
                        if (cross <= 0f && core <= 0f) continue;

                        float a = cross * amp, c2 = core * amp;
                        Color c = px[row + x];
                        px[row + x] = new Color(
                            Mathf.Clamp01(c.r + tint.r * a + c2),
                            Mathf.Clamp01(c.g + tint.g * a + c2),
                            Mathf.Clamp01(c.b + tint.b * a + c2),
                            1f);
                    }
                }
            }
        }

        // Use one still, banded low-resolution fBm mask so the rainbow keeps a stable body/gap.
        static float[] Haze(int w, int h)
        {
            int nw = Mathf.Max(2, w / NoiseDiv), nh = Mathf.Max(2, h / NoiseDiv);
            float[] noise = Fbm(nw, nh, HazePhase);

            var dst = new float[w * h];
            float xScale = (nw - 1) / (float)w, yScale = (nh - 1) / (float)h;
            float band = 1f / (HazeHigh - HazeLow);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                var noiseRow = new BandedNoiseRow(noise, nw, nh, y * yScale);

                for (int x = 0; x < w; x++)
                    dst[row + x] = noiseRow.Sample(x * xScale, HazeLow, band);
            }

            return dst;
        }

    }
}
