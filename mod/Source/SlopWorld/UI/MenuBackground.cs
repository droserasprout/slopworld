using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The menu and loading-screen background, rotting. Nothing here ships art: the frames are
    // baked from the game's own planet on first run. Baked rather than computed per frame
    // because the source is 4096x2560, and cached to disk. Playback breathes on two sines of
    // incommensurate period, summed, so it never repeats visibly.
    [StaticConstructorOnStartup]
    public static class MenuBackground
    {
        // Current cuts between stages with nothing blending, so the count has to be high
        // enough that a neighbour reads as flicker rather than a jump. The ceiling is memory,
        // not bake time: each frame is a resident RGB24 texture up to MaxSide on its long
        // edge, ~8MB, ~380MB for the set. Raising this means lowering MaxSide.
        const int Stages = 30;

        const int MaxSide = 1024; // slightly more than god said

        // Never back to clean, never at the bottom of the well.
        const float BreatheLow = 0.7f;
        const float BreatheHigh = 0.9f;

        // Seconds, deliberately not a ratio of small integers. Short enough that the walk
        // across the stages is a flutter rather than a swell.
        const float SlowSecs = 4.15f;
        const float FastSecs = 1.27f;

        // How long the first rot takes, once, on the way in from clean.
        const float OnsetSecs = 2f;

        // SlopPlagueGas's violet, matched by eye rather than by reference.
        static readonly Color Sick = new Color(0.80f, 0.38f, 0.86f, 0.9f);

        // A third of the way down the rot: a picture alight at stage one has nowhere to go.
        const float FireFrom = 0.33f;
        // Read off the picture: its median luminance is 0.09 and its 95th percentile 0.69, so
        // a ramp reaching 1 only at pure white leaves the fire invisible.
        const float FuelFloor = 0.4f;
        const float FuelFull = 0.65f;
        // How far a bright thing must extend to count as fuel - the star filter, see Erode.
        const int FuelErode = 3;
        // Over a 1280-row frame, a plume of a hundred-odd pixels.
        const float FuelDecay = 0.98f;
        // fBm piles up around its middle, so 0..1 as-is is an even shimmer; stretching this
        // band gives a flame a lit body and a dark gap.
        const float NoiseLow = 0.38f;
        const float NoiseHigh = 0.62f;
        const float FireGain = 1.15f;

        // The noise field is drawn at 1/N of the frame and read back bilinear.
        const int NoiseDiv = 4;
        const float NoiseFreq = 0.035f;
        const int Octaves = 4;
        // Large enough that consecutive stages are unrelated draws; a fire that slid smoothly
        // would read as a pan across a still picture.
        const float StagePhase = 37.7f;

        static readonly Color Ember = new Color(1f, 0.24f, 0.05f, 1f);
        static readonly Color Flame = new Color(1f, 0.76f, 0.28f, 1f);

        // Value noise off a 256x256 field: what the fire's shape is drawn from, and cheaper
        // than Unity's Perlin by the whole of a native call. Rolled from a fixed seed rather
        // than shipped, so the bake is reproducible; held as floats rather than bytes so a
        // sample is four loads and no conversion. 256KB, which stays in cache for all of Fbm.
        const int LutSide = 256;
        const int LutMask = LutSide - 1;
        static readonly float[] _noiseLut = new float[LutSide * LutSide];

        static MenuBackground()
        {
            var bytes = new byte[_noiseLut.Length];
            new System.Random(42).NextBytes(bytes);
            for (int i = 0; i < bytes.Length; i++) _noiseLut[i] = bytes[i] * (1f / 255f);
        }

        // The artefacts are the point: 8x8 blocking and smeared chroma are the failure the
        // rest of this file imitates by hand, and the encoder does them for free. Also keeps
        // the cache to a few megabytes where PNG would be most of a gigabyte.
        const int JpegQuality = 10;

        // Bump it and every install rebakes - what a change to any constant above needs.
        const int Version = 2;

        // A stage's working buffer is bw*bh*16 bytes, so all thirty at once is a few hundred
        // megabytes of transient heap under a game that is already holding the menu. Stages are
        // computed in batches sized off this instead, and the buffers are reused across
        // batches, which makes the bake's footprint a constant rather than a function of
        // Stages. It also caps how many run at once, so this is the parallelism knob too.
        const int BakeBudgetMB = 96;

        static Texture2D[] _frames;
        // The cache key, and how a background switched in Options is noticed.
        static Texture2D _src;
        static string _srcKey;
        // So the onset ramp has a zero. Negative until the first draw.
        static float _began = -1f;

        public static bool HasFrames => _frames != null;

        // A null source means "whatever you baked from last time". Null back means there is
        // nothing to bake from and the caller should leave the field alone.
        public static Texture2D Current(Texture2D source)
        {
            if (source != null && !Ready(source)) return null;
            if (_frames == null) return null;

            float now = Time.realtimeSinceStartup;
            if (_began < 0f) _began = now;
            float t = now - _began;

            // Unequal amplitudes, so the fast term is a tremor over the swell rather than a
            // second beat.
            float breath =
                0.68f * Mathf.Sin(t * 2f * Mathf.PI / SlowSecs) +
                0.32f * Mathf.Sin(t * 2f * Mathf.PI / FastSecs);
            float band = Mathf.Lerp(BreatheLow, BreatheHigh, (breath + 1f) * 0.5f);

            // Smoothstep, a ramp that starts instantly being a cut.
            float onset = OnsetSecs <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / OnsetSecs));

            int i = Mathf.Clamp(Mathf.RoundToInt(band * onset * (Stages - 1)), 0, Stages - 1);
            return _frames[i];
        }

        // False means we could not get any and the caller should stand down.
        static bool Ready(Texture2D source)
        {
            if (source == null) return false;

            string key = Key(source);
            if (_frames != null && _srcKey == key) return true;

            _src = source;
            _srcKey = key;
            _began = -1f;

            try
            {
                _frames = Load(key) ?? Bake(source, key);
            }
            catch (Exception e)
            {
                // A background that will not bake is a cosmetic loss. Parallel.For hands back
                // an AggregateException whose own Message says only that one happened, so the
                // cause is unwrapped or the single line this path ever prints says nothing.
                if (e is AggregateException agg) e = agg.Flatten().InnerException ?? e;
                Log.Warning($"[SlopWorld] background bake failed, leaving it clean: {e}");
                _frames = null;
            }

            return _frames != null;
        }

        // The name changes when the player picks another expansion's background, so keying on
        // it is what makes that switch rebake.
        static string Key(Texture2D src)
        {
            string name = string.IsNullOrEmpty(src.name) ? "bg" : src.name;
            return $"{name}-{src.width}x{src.height}-{Stages}-v{Version}";
        }

        static string Dir(string key) =>
            Path.Combine(Path.Combine(CacheRoot(), "slopworld"), Path.Combine("bg", key));

        // Not GenFilePaths, which is config and saves; a derived texture is regenerable and so
        // safe to delete.
        static string CacheRoot()
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdg)) return xdg;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        }

        static Texture2D[] Load(string key)
        {
            string dir = Dir(key);
            if (!Directory.Exists(dir)) return null;

            var frames = new Texture2D[Stages];
            for (int i = 0; i < Stages; i++)
            {
                // D2 still holds the whole set; past 99 stages this needs widening.
                string path = Path.Combine(dir, $"{i:D2}.jpg");
                if (!File.Exists(path)) return null;

                // LoadImage sniffs the header.
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;                    // a half-written cache rebakes
                }
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Keep(tex);
                frames[i] = tex;
            }

            return frames;
        }

        static Texture2D[] Bake(Texture2D src, string key)
        {
            int w = src.width, h = src.height;
            float scale = Mathf.Min(1f, (float)MaxSide / Mathf.Max(w, h));
            int bw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int bh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            Color[] clean = Downsample(ReadBack(src), w, h, bw, bh);
            float[] fuel = Fuel(clean, bw, bh);

            string dir = Dir(key);
            Directory.CreateDirectory(dir);

            // Stages are independent of each other and of the engine - Mathf and Color are
            // arithmetic on structs, and nothing below Rot touches a Unity object - so the
            // pixels are worked out off the main thread. The encoder is not: Texture2D,
            // EncodeToJPG and LoadImage are all engine calls, so a batch is computed in
            // parallel and then written down in order on the way back.
            int batch = Mathf.Clamp((BakeBudgetMB << 20) / Mathf.Max(1, bw * bh * 16), 1, Stages);
            var opts = new ParallelOptions { MaxDegreeOfParallelism = batch };

            var scratch = new Color[batch][];
            for (int b = 0; b < batch; b++) scratch[b] = new Color[bw * bh];

            var frames = new Texture2D[Stages];
            for (int start = 0; start < Stages; start += batch)
            {
                int end = Math.Min(Stages, start + batch);

                Parallel.For(start, end, opts, i =>
                    Rot(clean, scratch[i - start], fuel, bw, bh, i / (float)(Stages - 1), i));

                for (int i = start; i < end; i++)
                {
                    var tex = new Texture2D(bw, bh, TextureFormat.RGB24, false);
                    tex.SetPixels(scratch[i - start]);
                    tex.Apply();

                    byte[] jpg = tex.EncodeToJPG(JpegQuality);
                    File.WriteAllBytes(Path.Combine(dir, $"{i:D2}.jpg"), jpg);

                    // Back in through the decoder: at this quality the difference is most of
                    // the look, and a first launch that came up clean-edged then blocked itself
                    // on restart would be reported as a bug.
                    tex.LoadImage(jpg);
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    Keep(tex);
                    frames[i] = tex;
                }
            }

            Log.Message($"[SlopWorld] baked {Stages} background frames at {bw}x{bh}, jpeg q{JpegQuality}, {batch} at a time, into {dir}");
            return frames;
        }

        // Order is load-bearing: smear first, because blurring after the contrast crush undoes
        // it; tint next, because a colour bias applied before a stretch comes back out of it;
        // fire last, being emissive. The encoder's damage lands after all of it, being the
        // transmission rather than the scene.
        // Writes into dst rather than allocating — the caller owns the buffer.
        static void Rot(Color[] src, Color[] dst, float[] fuel, int w, int h, float k, int stage)
        {
            if (k <= 0f)
            {
                Array.Copy(src, dst, src.Length);
                return;
            }

            Grade g = Grading(k);

            // Horizontal: a signal being dragged rather than a lens out of focus.
            int radius = Mathf.RoundToInt(Mathf.Lerp(0f, 24f, k * k));
            if (radius > 0) Smear(src, dst, w, h, radius, ref g);
            else for (int i = 0; i < dst.Length; i++) dst[i] = Shade(ref g, src[i]);

            Burn(dst, fuel, w, h, k, stage);
        }

        // The drain toward luminance, the contrast crush and the tint are three affine steps
        // per channel, and affine composed with affine is affine - so all of it is one 3x3 and
        // an offset, solved once a stage rather than nine Lerps and a grayscale a pixel. The
        // same arithmetic folded, not an approximation of it.
        struct Grade
        {
            public float rr, rg, rb, ro;
            public float gr, gg, gb, go;
            public float br, bg, bb, bo;
        }

        static Grade Grading(float k)
        {
            float contrast = Mathf.Lerp(1f, 2.3f, k);
            float lift = Mathf.Lerp(0f, 0.06f, k);      // blacks off the floor: video, not ink
            float drain = Mathf.Lerp(0f, 0.75f, k);     // toward luminance
            float tint = Mathf.Lerp(0f, 0.42f, k);      // then toward the plague

            // Color.grayscale's own weights, which is where the drain's cross-terms come from.
            const float LR = 0.299f, LG = 0.587f, LB = 0.114f;

            float keep = 1f - drain;
            // (c - 0.5) * contrast + 0.5 + lift, with the c gathered into the matrix.
            float bias = 0.5f - 0.5f * contrast + lift;

            // Multiplied rather than blended: a blend fogs the frame evenly, where this leaves
            // the dark dark and puts colour where there is light. A per-channel scale, so it
            // multiplies the contrast term and the lift alike.
            float tr = Mathf.Lerp(1f, Sick.r, tint), sr = contrast * tr;
            float tg = Mathf.Lerp(1f, Sick.g, tint), sg = contrast * tg;
            float tb = Mathf.Lerp(1f, Sick.b, tint), sb = contrast * tb;

            return new Grade
            {
                rr = (keep + drain * LR) * sr, rg = drain * LG * sr, rb = drain * LB * sr,
                gr = drain * LR * sg, gg = (keep + drain * LG) * sg, gb = drain * LB * sg,
                br = drain * LR * sb, bg = drain * LG * sb, bb = (keep + drain * LB) * sb,
                ro = bias * tr, go = bias * tg, bo = bias * tb,
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

        // Gated by Fuel, and rolled in with k so the planet catches as it rots. The noise is
        // drawn at a fraction of the frame and read back bilinear - four octaves over ten
        // megapixels is seconds of bake, and fire has no fine detail to lose. Each stage draws
        // from a different offset, which is what makes the flames move.
        static void Burn(Color[] px, float[] fuel, int w, int h, float k, int stage)
        {
            float heat = Mathf.InverseLerp(FireFrom, 1f, k);
            if (heat <= 0f) return;

            int nw = Mathf.Max(2, w / NoiseDiv), nh = Mathf.Max(2, h / NoiseDiv);
            float[] noise = Fbm(nw, nh, stage * StagePhase);

            // Hoisted: these are three divides a pixel over ten megapixels otherwise, and the
            // shaping below is InverseLerp into SmoothStep(0,1,..) written out - the clamp the
            // first does is the clamp the second would, so it is done once.
            float xScale = (nw - 1) / (float)w, yScale = (nh - 1) / (float)h;
            float band = 1f / (NoiseHigh - NoiseLow);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                // Where this row falls in the noise, and the two rows to blend.
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

                    // Fuel says where fire is allowed, the shaped noise what shape it takes.
                    // Multiplying the raw field in would let the noise decide both.
                    float s = (n - NoiseLow) * band;
                    s = s < 0f ? 0f : (s > 1f ? 1f : s);
                    float t = f * (s * s * (3f - 2f * s)) * heat;
                    if (t <= 0f) continue;

                    // Added rather than blended, so it lights the picture rather than painting
                    // over it.
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

        // One cell of the LUT, smoothstep-interpolated. The two axes are wrapped separately:
        // stepping a flat index by one runs off the end of a row into the start of the next
        // one, so a field sampled that way is discontinuous down every 256th column - a hard
        // vertical seam through the flames, and the phase walks it across the picture stage by
        // stage. The rows wrap for free, being the whole array.
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

        // Each octave stretched twice as far across x as up y, for the vertical grain;
        // isotropic noise is a cloud.
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

        // Box rather than gaussian: through the contrast crush the difference is invisible, and
        // a running sum makes the cost independent of the radius. Writes into dst, which the
        // caller owns, and grades on the way out rather than leaving a second sweep to do it:
        // the buffer is ten megabytes, so a pass over it costs more than all the arithmetic in
        // it and the two passes read the same pixel twice for nothing.
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

        // Core textures come out of the bundles unreadable, so the pixels are fetched back off
        // the GPU - the trip DeadCursor makes too.
        static Color[] ReadBack(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;

            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var copy = new Texture2D(src.width, src.height, TextureFormat.ARGB32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var px = copy.GetPixels();
            UnityEngine.Object.Destroy(copy);
            return px;
        }

        // A generated texture is referenced by nothing in the scene, so the
        // Resources.UnloadUnusedAssets on any map switch may take it - see TerminalFont.
        static void Keep(Texture2D tex) => tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

        // So the source capture below never bakes the rot from an already-rotted frame.
        public static bool IsOurs(Texture2D tex)
        {
            if (tex == null || _frames == null) return false;
            for (int i = 0; i < _frames.Length; i++)
                if (ReferenceEquals(_frames[i], tex)) return true;
            return false;
        }
    }

    // On the draw rather than on MainMenuDrawer.Init: Init runs once where this changes every
    // frame, and the loading screen draws the same background without going near Init.
    // BackgroundOnGUI reads overrideBGImage's size for the aspect fit, so handing it our frame
    // keeps scaling, letterboxing and the expansion crossfade working.
    [HarmonyPatch(typeof(UI_BackgroundMain), nameof(UI_BackgroundMain.BackgroundOnGUI))]
    public static class Patch_MenuBackgroundRot
    {
        static void Prefix(UI_BackgroundMain __instance)
        {
            Texture2D src = __instance.overrideBGImage;

            // Our own frame as a source would key the cache off a rotted picture.
            if (MenuBackground.IsOurs(src)) src = null;

            // A null field means vanilla is about to draw BGPlanet without consulting it, so
            // fetch the same texture - but only when nothing is baked, ContentFinder.Get
            // walking every loaded mod and this running once a frame.
            if (src == null && !MenuBackground.HasFrames)
                src = ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false);

            Texture2D frame = MenuBackground.Current(src);
            if (frame != null) __instance.overrideBGImage = frame;
        }
    }
}
