using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Rot/fire pixel math and its source-derived fuel. Bake owns textures and returned frames.
    internal static partial class MenuBackgroundBake
    {
        // Apply smear, grade/tint, and emissive fire. Apply JPEG artifacts last.
        // The caller owns `dst`.
        static void Rot(Color[] src, Color[] dst, float[] fuel, int w, int h, float k, int phase)
        {
            if (k <= 0f)
            {
                Array.Copy(src, dst, src.Length);
                return;
            }

            Grade g = Grading(k);

            // Horizontal smear reads as motion rather than defocus.
            int radius = Mathf.RoundToInt(Mathf.Lerp(0f, 24f, k * k));
            if (radius > 0) Smear(src, dst, w, h, radius, ref g);
            else for (int i = 0; i < dst.Length; i++) dst[i] = Shade(ref g, src[i]);

            Burn(dst, fuel, w, h, k, phase);
        }

        // Read off the clean picture, so the fire sits on the planet's own lit face. One upward
        // sweep with a decay, this running over ten megapixels. Pixels run bottom-up, so the
        // sweep and "up" agree without a flip.
        static float[] Fuel(Color[] clean, int w, int h)
        {
            var lit = new float[clean.Length];
            for (int i = 0; i < clean.Length; i++)
                lit[i] = Mathf.InverseLerp(FuelFloor, FuelFull, clean[i].grayscale);

            lit = Erode(lit, w, h, FuelErode);

            var fuel = new float[clean.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w, below = row - w;
                for (int x = 0; x < w; x++)
                {
                    float carried = y > 0 ? fuel[below + x] * FuelDecay : 0f;
                    fuel[row + x] = Mathf.Max(lit[row + x], carried);
                }
            }

            return fuel;
        }

        // What stands between a burning planet and a sky full of vertical streaks. Brightness
        // cannot tell a star from a lit planet. Size can - a star vanishes under a window this
        // wide and a planet does not notice it.
        static float[] Erode(float[] src, int w, int h, int r)
        {
            if (r <= 0) return src;

            var mid = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float lo = 1f;
                    for (int d = -r; d <= r; d++)
                        lo = Mathf.Min(lo, src[row + Mathf.Clamp(x + d, 0, w - 1)]);
                    mid[row + x] = lo;
                }
            }

            var dst = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float lo = 1f;
                    for (int d = -r; d <= r; d++)
                        lo = Mathf.Min(lo, mid[Mathf.Clamp(y + d, 0, h - 1) * w + x]);
                    dst[y * w + x] = lo;
                }
            }

            return dst;
        }

        // Fuel gates the fire. Low-resolution bilinear noise supplies moving shape at bakeable cost.
        static void Burn(Color[] px, float[] fuel, int w, int h, float k, int phase)
        {
            float heat = Mathf.InverseLerp(FireFrom, 1f, k);
            if (heat <= 0f) return;

            int nw = Mathf.Max(2, w / NoiseDiv), nh = Mathf.Max(2, h / NoiseDiv);
            float[] noise = Fbm(nw, nh, phase * StagePhase);

            // Hoist shared scales and the noise-band reciprocal out of the pixel loop.
            float xScale = (nw - 1) / (float)w, yScale = (nh - 1) / (float)h;
            float band = 1f / (NoiseHigh - NoiseLow);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                var noiseRow = new BandedNoiseRow(noise, nw, nh, y * yScale);

                for (int x = 0; x < w; x++)
                {
                    float f = fuel[row + x];
                    if (f <= 0f) continue;

                    // Fuel controls location. Shaped noise controls form.
                    float t = f * noiseRow.Sample(x * xScale, NoiseLow, band) * heat;
                    if (t <= 0f) continue;

                    // Add fire as light instead of replacing the source pixel.
                    float gain = t * FireGain;
                    Color c = px[row + x];
                    px[row + x] = new Color(
                        Mathf.Clamp01(c.r + (Ember.r + (Flame.r - Ember.r) * t) * gain),
                        Mathf.Clamp01(c.g + (Ember.g + (Flame.g - Ember.g) * t) * gain),
                        Mathf.Clamp01(c.b + (Ember.b + (Flame.b - Ember.b) * t) * gain),
                        1f);
                }
            }
        }

        // A running-sum box blur is fast enough after contrast crush. Grade during the same pass.
        static void Smear(Color[] src, Color[] dst, int w, int h, int radius, ref Grade t)
        {
            // Reciprocal rather than a divide per channel per pixel. The last bit of difference
            // is thrown away by the encoder several times over.
            float n = 1f / (radius * 2 + 1);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float r = 0f, g = 0f, b = 0f;

                // Prime the window on the first pixel, edges clamped.
                for (int d = -radius; d <= radius; d++)
                {
                    Color c = src[row + Mathf.Clamp(d, 0, w - 1)];
                    r += c.r; g += c.g; b += c.b;
                }

                // The clamps only bite within a radius of either end. Therefore, the bulk of the
                // row runs with the window wholly inside it and no bounds arithmetic at all.
                int lo = Mathf.Min(radius, w), hi = Mathf.Max(lo, w - radius - 1);

                for (int x = 0; x < lo; x++)
                {
                    dst[row + x] = Shade(ref t, r * n, g * n, b * n);
                    Color outgoing = src[row];
                    Color incoming = src[row + Mathf.Min(x + radius + 1, w - 1)];
                    r += incoming.r - outgoing.r;
                    g += incoming.g - outgoing.g;
                    b += incoming.b - outgoing.b;
                }

                for (int x = lo; x < hi; x++)
                {
                    dst[row + x] = Shade(ref t, r * n, g * n, b * n);
                    Color outgoing = src[row + x - radius];
                    Color incoming = src[row + x + radius + 1];
                    r += incoming.r - outgoing.r;
                    g += incoming.g - outgoing.g;
                    b += incoming.b - outgoing.b;
                }

                for (int x = hi; x < w; x++)
                {
                    dst[row + x] = Shade(ref t, r * n, g * n, b * n);
                    Color outgoing = src[row + Mathf.Max(x - radius, 0)];
                    Color incoming = src[row + w - 1];
                    r += incoming.r - outgoing.r;
                    g += incoming.g - outgoing.g;
                    b += incoming.b - outgoing.b;
                }
            }
        }

    }
}
