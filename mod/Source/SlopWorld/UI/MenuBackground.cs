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
    // The menu and loading-screen background, rotting - or, with grandma in the house, the same
    // picture under a rainbow. Nothing here ships art: the frames are baked from the game's own
    // planet on first run. Baked rather than computed per frame because the source is 4096x2560,
    // and cached to disk. Playback breathes on two sines of incommensurate period, summed, so it
    // never repeats visibly; the sparkling variant loops instead, and is baked to close.
    //
    // The two variants share everything above the per-stage transform - the read-back, the
    // downsample, the noise, the batching, the encoder and the cache - and differ only in what
    // Rot and Sparkle do to a stage and in how Current walks the set. Which one is resident is
    // the setting, folded into the cache key so a toggle switches the frames rather than
    // reinterpreting them.
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

        // ---- Grandma's visiting ----

        // How long one turn through every stage takes, and so also how long the slowest star
        // takes to blink. The set closes, so this is a loop and not a sweep with a cut in it.
        const float LoopSecs = 3.6f;

        // Hue periods along the diagonal. Deliberately not an integer: the sheen only has to
        // close in time, and one that closed in space too would put a seam down the picture
        // where the last band met the first.
        const float SheenCycles = 1.6f;
        // The palette's swing is +-0.5 about a flat grey, so this is the chroma at full mask.
        const float SheenGain = 0.85f;
        // The sheen rides a still fBm field, banded for the same reason the fire's is: taken
        // as it comes, fBm piles up around its middle and gives an even wash rather than a
        // band with a body and a gap.
        const float HazeLow = 0.40f;
        const float HazeHigh = 0.66f;
        // Nowhere near the fire's phases, so the two variants are not the same cloud twice.
        const float HazePhase = 11.3f;

        // The constellation is rolled once from this, so the bake stays reproducible.
        const int SparkSeed = 1971;
        const int SparkCount = 140;
        // Arm half-lengths in pixels at MaxSide, scaled down with the frame. A star is a cross
        // this long and SparkThick times thinner.
        const float SparkArmMin = 7f;
        const float SparkArmMax = 22f;
        const float SparkThick = 7f;
        const float SparkGain = 1.1f;
        // Blinks per loop. Integers, or a star would be caught mid-blink at the wrap.
        const int SparkRateMin = 1;
        const int SparkRateMax = 4;

        // Value noise off a 256x256 field: what the fire's shape is drawn from, and cheaper
        // than Unity's Perlin by the whole of a native call. Rolled from a fixed seed rather
        // than shipped, so the bake is reproducible; held as floats rather than bytes so a
        // sample is four loads and no conversion. 256KB, which stays in cache for all of Fbm.
        const int LutSide = 256;
        const int LutMask = LutSide - 1;
        static readonly float[] _noiseLut = new float[LutSide * LutSide];

        // The rainbow, as a cosine palette: three raised cosines a third of a turn apart, which
        // is a full hue circle with none of HSV's corners and no branch to sample it. Tabulated
        // off the same reasoning as the noise - a hue a pixel is three Cos calls over ten
        // megapixels otherwise. 256 entries and no interpolation between them: over a ramp this
        // long a step is under two pixels wide, and the encoder is about to throw away far more
        // than that.
        const int HueSide = 256;
        const int HueMask = HueSide - 1;
        static readonly Color[] _hueLut = new Color[HueSide];

        static MenuBackground()
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

        // Wrapped rather than clamped, hue being a circle and both callers walking off both
        // ends of it. Two's complement makes the mask do the negative side for free, which is
        // the trick Noise plays on its own indices.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Color Hue(float t) => _hueLut[Mathf.FloorToInt(t * HueSide) & HueMask];

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
        // Which transform the resident set was baked with, rather than what the setting says
        // this instant: the two disagree for the one frame between a toggle and the reload,
        // and walking rotted stages on the sparkling clock would be visible.
        static bool _glow;
        // So the onset ramp has a zero. Negative until the first draw.
        static float _began = -1f;

        public static bool HasFrames => _frames != null;

        // A null source means "whatever you baked from last time". Null back means there is
        // nothing to bake from and the caller should leave the field alone.
        public static Texture2D Current(Texture2D source)
        {
            // The caller stops handing us a source as soon as there are frames - it has our own
            // frame in the field by then and nulls it - so a setting turned over with the menu
            // still up would never reach Ready. Handing back what we baked from is what makes
            // the toggle land, and it is a bool compare rather than the key, which is a string
            // this would be formatting every frame.
            if (_frames != null && _glow != Settings.GrandmaMode) source = _src;

            if (source != null && !Ready(source)) return null;
            if (_frames == null) return null;

            float now = Time.realtimeSinceStartup;
            if (_began < 0f) _began = now;
            float t = now - _began;

            // Straight through the stages and around again. No breath and no onset: every stage
            // is the same picture at a different point of the same loop, where the rot's are a
            // depth it arrives at and then hovers around. The set was baked to close, so the
            // wrap is not a cut.
            if (_glow) return _frames[Mathf.FloorToInt(t * (Stages / LoopSecs)) % Stages];

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

            bool glow = Settings.GrandmaMode;
            string key = Key(source, glow);
            if (_frames != null && _srcKey == key) return true;

            _src = source;
            _srcKey = key;
            _glow = glow;
            _began = -1f;

            // The set being replaced is left to Unity. Its textures are marked
            // DontUnloadUnusedAsset, so they stay resident - the same price an expansion
            // background switch has always cost, paid once more if the setting is turned over.
            try
            {
                _frames = Load(key) ?? Bake(source, key, glow);
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
        // it is what makes that switch rebake. The variant is in here for the same reason and
        // one more: both sets survive on disk, so turning the setting back is a load rather
        // than a second bake.
        static string Key(Texture2D src, bool glow)
        {
            string name = string.IsNullOrEmpty(src.name) ? "bg" : src.name;
            return $"{name}-{src.width}x{src.height}-{Stages}-{(glow ? "glow" : "rot")}-v{Version}";
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

        static Texture2D[] Bake(Texture2D src, string key, bool glow)
        {
            int w = src.width, h = src.height;
            float scale = Mathf.Min(1f, (float)MaxSide / Mathf.Max(w, h));
            int bw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int bh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            Color[] clean = Downsample(ReadBack(src), w, h, bw, bh);

            // Each variant's one still input, read off the clean picture or rolled from a fixed
            // seed, and shared by every stage: where fire is allowed for the one, where the
            // sheen is bright and where the stars are for the other.
            float[] fuel = glow ? null : Fuel(clean, bw, bh);
            float[] haze = glow ? Haze(bw, bh) : null;
            Spark[] sparks = glow ? Constellation(bw, bh) : null;

            string dir = Dir(key);
            Directory.CreateDirectory(dir);

            // Stages are independent of each other and of the engine - Mathf and Color are
            // arithmetic on structs, and nothing below Rot or Sparkle touches a Unity object -
            // so the pixels are worked out off the main thread. The encoder is not: Texture2D,
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
                {
                    if (glow) Sparkle(clean, scratch[i - start], haze, sparks, bw, bh, i);
                    else Rot(clean, scratch[i - start], fuel, bw, bh, i / (float)(Stages - 1), i);
                });

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

            Log.Message($"[SlopWorld] baked {Stages} {(glow ? "sparkling" : "rotting")} background frames at {bw}x{bh}, jpeg q{JpegQuality}, {batch} at a time, into {dir}");
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

        static Grade Grading(float k) => Affine(
            Mathf.Lerp(1f, 2.3f, k),        // contrast
            Mathf.Lerp(0f, 0.06f, k),       // blacks off the floor: video, not ink
            Mathf.Lerp(0f, 0.75f, k),       // drain toward luminance
            Sick, Mathf.Lerp(0f, 0.42f, k));

        // Nothing is taken away here: a hair more contrast, the blacks lifted the same as the
        // rot lifts them, and a *negative* drain, which is a saturation boost written in the one
        // form Shade already knows. No tint - in this variant the colour is the sheen's and the
        // stars', and a picture already tinted would argue with both.
        static Grade Cheer() => Affine(1.06f, 0.05f, -0.35f, Color.white, 0f);

        static Grade Affine(float contrast, float lift, float drain, Color toward, float tint)
        {
            // Color.grayscale's own weights, which is where the drain's cross-terms come from.
            const float LR = 0.299f, LG = 0.587f, LB = 0.114f;

            float keep = 1f - drain;
            // (c - 0.5) * contrast + 0.5 + lift, with the c gathered into the matrix.
            float bias = 0.5f - 0.5f * contrast + lift;

            // Multiplied rather than blended: a blend fogs the frame evenly, where this leaves
            // the dark dark and puts colour where there is light. A per-channel scale, so it
            // multiplies the contrast term and the lift alike.
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

        // What Rot is not: nothing is taken away. The picture is graded a shade brighter and a
        // shade more saturated, a rainbow is laid over it, and the constellation is lit. Both of
        // the last two are functions of the stage over the whole set rather than of a depth, and
        // both close - the sheen slides exactly one hue period across the loop and every star
        // blinks a whole number of times - so the last stage meets the first with no cut. That
        // is the difference the playback in Current is about.
        // Writes into dst rather than allocating — the caller owns the buffer.
        static void Sparkle(Color[] src, Color[] dst, float[] haze, Spark[] sparks, int w, int h, int stage)
        {
            Grade g = Cheer();

            // Over Stages rather than Stages-1: the loop is open at the top, stage 0 being where
            // stage 30 would have landed. Closed on Stages-1 the wrap would show every stage
            // twice.
            float s = stage / (float)Stages;

            // Along the diagonal, so the band crosses the picture corner to corner rather than
            // lying in bars down it.
            float ramp = SheenCycles / (w + h - 2);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    Color c = Shade(ref g, src[i]);

                    float m = haze[i];
                    if (m > 0f)
                    {
                        // The palette's mean is a flat half in every channel, and it comes back
                        // out here: what is added is chroma alone. Over the sky that lights a
                        // colour into the black, over the lit face it swings the hue, and
                        // neither is the even grey fog that adding the palette whole would be.
                        Color hue = Hue((x + y) * ramp + s);
                        float gain = m * SheenGain;
                        c = new Color(
                            Mathf.Clamp01(c.r + (hue.r - 0.5f) * gain),
                            Mathf.Clamp01(c.g + (hue.g - 0.5f) * gain),
                            Mathf.Clamp01(c.b + (hue.b - 0.5f) * gain),
                            1f);
                    }

                    dst[i] = c;
                }
            }

            Twinkle(dst, sparks, w, h, s);
        }

        // A star: where it is, how far it reaches, where it sits on the wheel, and the blink it
        // is in the middle of.
        struct Spark
        {
            public int x, y;
            public float arm;
            public float hue;
            public int rate;      // blinks per loop, whole
            public float phase;   // where in that blink stage zero finds it
        }

        // Rolled once from a fixed seed, and the same for every stage - which is what lets a star
        // blink rather than jump about. Uniform over the frame: a star on the planet's own lit
        // face simply does not show, and that is a truer scattering than one that had been told
        // to avoid it.
        static Spark[] Constellation(int w, int h)
        {
            var rng = new System.Random(SparkSeed);

            // Arms are quoted at MaxSide, so a frame baked smaller gets smaller stars rather
            // than the same stars taking up more of it.
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

        // Each star is lit by a raised cosine of the stage, on its own rate and its own phase, so
        // the constellation is never all up at once and never all down. Squared, because a blink
        // wants a short peak and a long dark where the cosine gives it even halves.
        static void Twinkle(Color[] px, Spark[] sparks, int w, int h, float s)
        {
            for (int i = 0; i < sparks.Length; i++)
            {
                Spark sp = sparks[i];

                float lit = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (sp.rate * s + sp.phase));
                lit *= lit;
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
                        // hot in the middle and coloured at the edges.
                        float cross = fx * ty + fy * tx;
                        float d = 1f - Mathf.Sqrt(ax * ax + ay * ay);
                        float core = d > 0f ? d * d * d : 0f;
                        if (cross <= 0f && core <= 0f) continue;

                        float a = cross * amp, k = core * amp;
                        Color c = px[row + x];
                        px[row + x] = new Color(
                            Mathf.Clamp01(c.r + tint.r * a + k),
                            Mathf.Clamp01(c.g + tint.g * a + k),
                            Mathf.Clamp01(c.b + tint.b * a + k),
                            1f);
                    }
                }
            }
        }

        // How bright the sheen is allowed to be, and still across the whole loop: one fBm field
        // drawn small and read back bilinear the way the fire's is, banded so the rainbow has a
        // body and a gap. Still, because a mask that drifted would have to close with the loop
        // too, and a rainbow sliding under a moving cloud is one motion more than a background
        // behind a menu has any business having.
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
