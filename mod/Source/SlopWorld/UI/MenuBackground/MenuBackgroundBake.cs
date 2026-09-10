using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Owns the offline cache and image-generation side of the menu background.
    internal static class MenuBackgroundBake
    {
        // Reproducible 256x256 float value-noise LUT; cheaper to sample than Unity Perlin.
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

        // Wrap hue indices because hue is circular; the mask also handles negative values.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Color Hue(float t) => _hueLut[Mathf.FloorToInt(t * HueSide) & HueMask];

        // The name changes when the player picks another expansion's background, so keying on
        // it is what makes that switch rebake. The preset is in here for the same reason and
        // one more: both sets survive on disk, so turning the setting back is a load rather
        // than a second bake.
        internal static string Key(Texture2D src, MenuBackgroundPreset preset)
        {
            string name = string.IsNullOrEmpty(src.name) ? "bg" : src.name;
            return $"{name}-{src.width}x{src.height}-{preset.Name}{preset.Total}-{Tuning}-v{MenuBackgroundTuning.Version}";
        }

        // Every number the pixels depend on, hashed in. Under Version alone, a tuning change
        // that forgot to bump it rendered the old numbers - indistinguishable from a constant
        // that did nothing.
        static readonly string Tuning = HashTuning();

        static string HashTuning()
        {
            var sb = new StringBuilder();
            foreach (float f in new[]
            {
                BreatheLow, BreatheHigh, OnsetSecs,
                FireFrom, FuelFloor, FuelFull, FuelDecay, NoiseLow, NoiseHigh, FireGain,
                NoiseFreq, StagePhase,
                SheenCycles, SheenGain, HazeLow, HazeHigh, HazePhase,
                SparkArmMin, SparkArmMax, SparkThick, SparkGain,
                Sick.r, Sick.g, Sick.b, Ember.r, Ember.g, Ember.b, Flame.r, Flame.g, Flame.b,
            })
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture)).Append(';');

            foreach (int i in new[]
            {
                Onset, MaxSide, JpegQuality, FuelErode, NoiseDiv, Octaves, LutSide, HueSide,
                SparkSeed, SparkCount, SparkRateMin, SparkRateMax,
            })
                sb.Append(i).Append(';');

            uint h = 2166136261;
            string all = sb.ToString();
            for (int i = 0; i < all.Length; i++)
            {
                h ^= all[i];
                h *= 16777619;
            }
            return h.ToString("x8");
        }

        static string Root() => Path.Combine(Path.Combine(CacheRoot(), "slopworld"), "bg");

        static string Dir(string key) => Path.Combine(Root(), key);

        // Not GenFilePaths, which is config and saves; a derived texture is regenerable and so
        // safe to delete.
        static string CacheRoot()
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdg)) return xdg;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        }

        // Recency is the write time, which Load touches: two sets a player toggles between are
        // both in use however long ago they were baked.
        internal static void Sweep(string keep)
        {
            try
            {
                string root = Root();
                if (!Directory.Exists(root)) return;
                DateTime cutoff = DateTime.UtcNow.AddDays(-KeepDays);
                foreach (string dir in Directory.GetDirectories(root))
                {
                    if (Path.GetFileName(dir) == keep) continue;
                    if (Directory.GetLastWriteTimeUtc(dir) >= cutoff) continue;
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] background cache left unswept: {e.Message}");
            }
        }

        internal static Texture2D[] Load(string key, MenuBackgroundPreset preset)
        {
            string dir = Dir(key);
            if (!Directory.Exists(dir)) return null;

            var frames = new Texture2D[preset.Total];
            for (int i = 0; i < frames.Length; i++)
            {
                // D2 holds a set of up to a hundred; past that this needs widening.
                string path = Path.Combine(dir, $"{i:D2}.jpg");
                if (!File.Exists(path)) return null;

                // LoadImage sniffs the header. Non-readable drops the CPU-side copy Unity keeps
                // beside the GPU one, half the set's resident cost; nothing here reads pixels back.
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(File.ReadAllBytes(path), true))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;                    // a half-written cache rebakes
                }
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Keep(tex);
                frames[i] = tex;
            }

            try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); } catch { }
            return frames;
        }

        // The still inputs every stage of a preset shares, worked out once rather than per stage.
        sealed class Shared
        {
            public float[] Fuel;        // rot: where fire is allowed
            public float[] Haze;        // glow: where the sheen is bright
            public Spark[] Sparks;      // glow: where the stars are
        }

        static Shared Prep(MenuBackgroundPreset preset, Color[] clean, int w, int h) =>
            preset == MenuBackgroundPresets.Sparkling
                ? new Shared { Haze = Haze(w, h), Sparks = Constellation(w, h) }
                : new Shared { Fuel = Fuel(clean, w, h) };

        // How far into the preset a stage has arrived, and which draw of the moving part it
        // holds. The ramp's phase keeps moving, so the fire is alive while the picture arrives.
        static void Where(MenuBackgroundPreset preset, int stage, out float k, out int phase)
        {
            bool breathes = preset.Depths > 1;

            if (stage < Onset)
            {
                float arrived = stage / (float)(Onset - 1);
                k = breathes ? BreatheMid * arrived : arrived;
                phase = stage % preset.Phases;
                return;
            }

            int at = stage - Onset;
            phase = at % preset.Phases;
            k = breathes
                ? Mathf.Lerp(BreatheLow, BreatheHigh, (at / preset.Phases) / (float)(preset.Depths - 1))
                : 1f;
        }

        static void Stage(MenuBackgroundPreset preset, Color[] clean, Color[] dst, Shared shared,
                          int w, int h, int stage)
        {
            Where(preset, stage, out float k, out int phase);
            if (preset == MenuBackgroundPresets.Sparkling)
                Sparkle(clean, dst, shared.Haze, shared.Sparks, w, h, phase / (float)preset.Phases, k);
            else
                Rot(clean, dst, shared.Fuel, w, h, k, phase);
        }

        internal static Texture2D[] Bake(Texture2D src, string key, MenuBackgroundPreset preset)
        {
            int w = src.width, h = src.height;
            float scale = Mathf.Min(1f, (float)MaxSide / Mathf.Max(w, h));
            int bw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int bh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            Color[] clean = Downsample(TextureReadback.ReadBack(src), w, h, bw, bh);
            Shared shared = Prep(preset, clean, bw, bh);

            string dir = Dir(key);
            Directory.CreateDirectory(dir);

            // Pixel math is thread-safe; Texture2D encoding/loading must remain on the main
            // thread, so compute batches in parallel and write them in order.
            int total = preset.Total;
            int batch = Mathf.Clamp((BakeBudgetMB << 20) / Mathf.Max(1, bw * bh * 16), 1, total);
            var opts = new ParallelOptions { MaxDegreeOfParallelism = batch };

            var scratch = new Color[batch][];
            for (int b = 0; b < batch; b++) scratch[b] = new Color[bw * bh];

            var frames = new Texture2D[total];
            for (int start = 0; start < total; start += batch)
            {
                int end = Math.Min(total, start + batch);

                Parallel.For(start, end, opts,
                    i => Stage(preset, clean, scratch[i - start], shared, bw, bh, i));

                for (int i = start; i < end; i++)
                {
                    var tex = new Texture2D(bw, bh, TextureFormat.RGB24, false);
                    tex.SetPixels(scratch[i - start]);
                    tex.Apply();

                    byte[] jpg = tex.EncodeToJPG(JpegQuality);
                    File.WriteAllBytes(Path.Combine(dir, $"{i:D2}.jpg"), jpg);

                    // Reload the JPEG so the first launch and cached reload use identical pixels.
                    tex.LoadImage(jpg, true);
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    Keep(tex);
                    frames[i] = tex;
                }
            }

            Log.Message($"[SlopWorld] baked {preset.Name}: {Onset} ramp + {preset.Depths}x{preset.Phases} " +
                        $"at {bw}x{bh}, jpeg q{JpegQuality}, {batch} at a time, into {dir}");
            return frames;
        }

        // Apply smear, grade/tint, then emissive fire; JPEG artifacts are applied last.
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

        // Cheer raises contrast/blacks and boosts saturation without tint; at k=0 it is identity.
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
        // cannot tell a star from a lit planet; size can - a star vanishes under a window this
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

        // Fuel gates the fire; low-resolution bilinear noise supplies moving shape at bakeable cost.
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
                // Locate this output row in the noise field.
                float ny = y * yScale;
                int ny0 = (int)ny, ny1 = Mathf.Min(nh - 1, ny0 + 1);
                float fy = ny - ny0;
                int r0 = ny0 * nw, r1 = ny1 * nw;

                for (int x = 0; x < w; x++)
                {
                    float f = fuel[row + x];
                    if (f <= 0f) continue;

                    float nx = x * xScale;
                    int nx0 = (int)nx, nx1 = Mathf.Min(nw - 1, nx0 + 1);
                    float fx = nx - nx0;

                    float a = noise[r0 + nx0], c0 = noise[r1 + nx0];
                    a += (noise[r0 + nx1] - a) * fx;
                    c0 += (noise[r1 + nx1] - c0) * fx;
                    float n = a + (c0 - a) * fy;

                    // Fuel controls location; shaped noise controls form.
                    float s = (n - NoiseLow) * band;
                    s = s < 0f ? 0f : (s > 1f ? 1f : s);
                    float t = f * (s * s * (3f - 2f * s)) * heat;
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

        // Sparkle adds closed-loop sheen and stars to the graded source; the caller owns `dst`.
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

        // Each star is lit by a raised cosine of s, on its own rate and its own phase, so the
        // constellation is never all up at once and never all down. Squared, because a blink
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
                        // alone gives a middle no brighter than twice an arm, so the core is a
                        // separate round term - and it is white rather than tinted, a star being
                        // hot in the middle and colored at the edges.
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
                float ny = y * yScale;
                int ny0 = (int)ny, ny1 = Mathf.Min(nh - 1, ny0 + 1);
                float fy = ny - ny0;
                int r0 = ny0 * nw, r1 = ny1 * nw;

                for (int x = 0; x < w; x++)
                {
                    float nx = x * xScale;
                    int nx0 = (int)nx, nx1 = Mathf.Min(nw - 1, nx0 + 1);
                    float fx = nx - nx0;

                    float a = noise[r0 + nx0], c = noise[r1 + nx0];
                    a += (noise[r0 + nx1] - a) * fx;
                    c += (noise[r1 + nx1] - c) * fx;
                    float n = a + (c - a) * fy;

                    float t = (n - HazeLow) * band;
                    t = t < 0f ? 0f : (t > 1f ? 1f : t);
                    dst[row + x] = t * t * (3f - 2f * t);
                }
            }

            return dst;
        }

        // Bilinear sample of the wrapped 2D LUT; wrap x and y independently to avoid row seams.
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

        // A running-sum box blur is fast enough after contrast crush; grade during the same pass.
        static void Smear(Color[] src, Color[] dst, int w, int h, int radius, ref Grade t)
        {
            // Reciprocal rather than a divide per channel per pixel; the last bit of difference
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

                // The clamps only bite within a radius of either end, so the bulk of the row
                // runs with the window wholly inside it and no bounds arithmetic at all.
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

        // Averaging rather than sampling: point-sampling a starfield at half scale throws away
        // half the stars.
        static Color[] Downsample(Color[] src, int w, int h, int bw, int bh)
        {
            if (bw == w && bh == h) return src;

            var dst = new Color[bw * bh];
            float sx = (float)w / bw, sy = (float)h / bh;

            for (int y = 0; y < bh; y++)
            {
                int y0 = Mathf.FloorToInt(y * sy);
                int y1 = Mathf.Min(h, Mathf.Max(y0 + 1, Mathf.FloorToInt((y + 1) * sy)));

                for (int x = 0; x < bw; x++)
                {
                    int x0 = Mathf.FloorToInt(x * sx);
                    int x1 = Mathf.Min(w, Mathf.Max(x0 + 1, Mathf.FloorToInt((x + 1) * sx)));

                    float r = 0f, g = 0f, b = 0f;
                    int n = 0;

                    for (int j = y0; j < y1; j++)
                    {
                        for (int i = x0; i < x1; i++)
                        {
                            Color c = src[j * w + i];
                            r += c.r; g += c.g; b += c.b;
                            n++;
                        }
                    }

                    dst[y * bw + x] = new Color(r / n, g / n, b / n, 1f);
                }
            }

            return dst;
        }

        // Root generated textures across map switches.
        static void Keep(Texture2D tex) => tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
    }
}
