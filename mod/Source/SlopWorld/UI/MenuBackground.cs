using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Bakes a ramp plus a Depths x Phases frame grid; see mod-background.md.
    [StaticConstructorOnStartup]
    public static class MenuBackground
    {
        // Preset parameters and the frame set shape; adding one also requires Prep/Stage support.
        sealed class Preset
        {
            public readonly string Name;
            // A single depth has no moving layer.
            public readonly int Depths;
            public readonly int Phases;
            // Closed motion must be played in phase order; independent noise does not.
            public readonly bool Closed;

            public Preset(string name, int depths, int phases, bool closed)
            {
                Name = name; Depths = depths; Phases = phases; Closed = closed;
            }

            public int Total => Onset + Depths * Phases;
        }

        // Resident RGB24 frames make this a memory limit (~110MB for the rotting set).
        const int Onset = 14;
        static readonly Preset Rotting = new Preset("rot", 5, 8, false);
        static readonly Preset Sparkling = new Preset("glow", 1, 24, true);

        static Preset Chosen => Settings.GrandmaMode ? Sparkling : Rotting;

        const int MaxSide = 1024; // frame-side ceiling

        // The ramp stays between clean and fully dark.
        const float BreatheLow = 0.7f;
        const float BreatheHigh = 0.9f;
        static float BreatheMid => (BreatheLow + BreatheHigh) * 0.5f;

        const float OnsetSecs = 2f;

        // Mean-reverting walk avoids a learnable period; step/pull/jitter set the baked range.
        const float WalkStep = 0.28f;
        const float WalkPull = 0.22f;
        const float WalkJitter = 0.24f;
        // Gaps larger than this are treated as an alt-tab rather than a new frame.
        const float WalkGapMax = 8f;

        // Duration of one moving frame.
        const float PhaseSecs = 0.11f;

        // Loop duration for closed presets and star blink rates.
        const float LoopSecs = 3.6f;

        // ---- Rot ----

        // SlopPlagueGas's violet, matched by eye rather than by reference.
        static readonly Color Sick = new Color(0.80f, 0.38f, 0.86f, 0.9f);

        // A third of the way down the rot: a picture alight at stage one has nowhere to go.
        const float FireFrom = 0.33f;
        // Tuned to the source luminance so fire remains visible before pure white.
        const float FuelFloor = 0.4f;
        const float FuelFull = 0.65f;
        // Minimum bright-region radius for fuel and star filtering.
        const int FuelErode = 3;
        // Fuel decay over roughly a hundred pixels at 1280px height.
        const float FuelDecay = 0.98f;
        // Stretch the fBm mid-band into a distinct flame body and gap.
        const float NoiseLow = 0.38f;
        const float NoiseHigh = 0.62f;
        const float FireGain = 1.15f;

        // Noise is drawn at 1/N resolution and sampled bilinearly.
        const int NoiseDiv = 4;
        const float NoiseFreq = 0.035f;
        const int Octaves = 4;
        // Phase separation keeps consecutive fire frames from reading as a pan.
        const float StagePhase = 37.7f;

        static readonly Color Ember = new Color(1f, 0.24f, 0.05f, 1f);
        static readonly Color Flame = new Color(1f, 0.76f, 0.28f, 1f);

        // ---- Grandma's visiting ----

        // Non-integer diagonal hue period avoids a spatial seam while closing in time.
        const float SheenCycles = 1.6f;
        // Palette swing around grey; this is chroma at full mask.
        const float SheenGain = 0.85f;
        // Band the still fBm field so the sheen has a body and gap rather than an even wash.
        const float HazeLow = 0.40f;
        const float HazeHigh = 0.66f;
        // Keep sheen phases distinct from fire phases.
        const float HazePhase = 11.3f;

        // The constellation is rolled once from this, so the bake stays reproducible.
        const int SparkSeed = 1971;
        const int SparkCount = 140;
        // Star arm lengths at MaxSide, scaled with the frame.
        const float SparkArmMin = 7f;
        const float SparkArmMax = 22f;
        const float SparkThick = 7f;
        const float SparkGain = 1.1f;
        // Integer blink rates ensure closed-loop alignment.
        const int SparkRateMin = 1;
        const int SparkRateMax = 4;

        // Reproducible 256x256 float value-noise LUT; cheaper to sample than Unity Perlin.
        const int LutSide = 256;
        const int LutMask = LutSide - 1;
        static readonly float[] _noiseLut = new float[LutSide * LutSide];

        // Tabulated cosine hue palette: branch-free full-circle hues at lower per-pixel cost.
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

        // Wrap hue indices because hue is circular; the mask also handles negative values.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Color Hue(float t) => _hueLut[Mathf.FloorToInt(t * HueSide) & HueMask];

        // Low JPEG quality supplies the intended block/chroma artifacts and keeps the cache small.
        const int JpegQuality = 10;

        // Bump when bake arithmetic changes; tuning constants are already in the cache key.
        const int Version = 3;

        // Batch scratch buffers to cap transient memory and parallelism independently of set size.
        const int BakeBudgetMB = 96;

        // Remove cache directories unused for this long.
        const int KeepDays = 30;

        // The resident set is a layout concern; animation never needs to know how it was
        // loaded or which source texture produced it.
        struct LayoutState
        {
            public Texture2D[] Frames;
            public Texture2D Source;
            public string SourceKey;
            public Preset Preset;
        }

        // The ramp and phase walk are one small state machine. Keeping them together prevents
        // a preset reload from accidentally preserving half of the old animation.
        struct AnimationState
        {
            public float Began;
            public float Depth;
            public int Phase;
            public float WalkAt;
            public float PhaseAt;
            public System.Random WalkRng;
        }

        static LayoutState _layout;
        static AnimationState _animation = new AnimationState
        {
            Began = -1f,
            WalkRng = new System.Random(),
        };

        // These aliases keep the animation and bake code readable while making the ownership
        // explicit in the two state structs above.
        static Texture2D[] _frames { get => _layout.Frames; set => _layout.Frames = value; }
        static Texture2D _src { get => _layout.Source; set => _layout.Source = value; }
        static string _srcKey { get => _layout.SourceKey; set => _layout.SourceKey = value; }
        static Preset _preset { get => _layout.Preset; set => _layout.Preset = value; }
        static float _began { get => _animation.Began; set => _animation.Began = value; }

        public static bool HasFrames => _frames != null;

        // A null source means "whatever you baked from last time". Null back means there is
        // nothing to bake from and the caller should leave the field alone.
        public static Texture2D Current(Texture2D source)
        {
            // Keep using the baked source during a preset switch so Ready can observe the change.
            if (_frames != null && _preset != Chosen) source = _src;

            if (source != null && !Ready(source)) return null;
            if (_frames == null) return null;

            float now = Time.realtimeSinceStartup;
            if (_began < 0f)
            {
                _began = now;
                _depth = 0.5f;
                _phase = 0;
                _walkAt = 0f;
                _phaseAt = 0f;
            }

            float t = now - _began;

            // Smoothstepped, a ramp that starts instantly being a cut. Frame zero is the game's
            // own picture untouched, so this leaves it where the menu had it.
            if (t < OnsetSecs)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / OnsetSecs);
                return _frames[Mathf.Clamp(Mathf.FloorToInt(u * Onset), 0, Onset - 1)];
            }

            Walk(t - OnsetSecs);
            int depth = Mathf.Clamp(
                Mathf.RoundToInt(_depth * (_preset.Depths - 1)), 0, _preset.Depths - 1);
            return _frames[Onset + depth * _preset.Phases + _phase];
        }

        // Stepped off absolute times rather than a delta, so a frame that asks twice gets one
        // answer - the menu patch and eco both call Current.
        static float _depth { get => _animation.Depth; set => _animation.Depth = value; }
        static int _phase { get => _animation.Phase; set => _animation.Phase = value; }
        static float _walkAt { get => _animation.WalkAt; set => _animation.WalkAt = value; }
        static float _phaseAt { get => _animation.PhaseAt; set => _animation.PhaseAt = value; }
        // Time-seeded, unlike everything the bake rolls: the frames are meant to match across
        // installs and the path across them is meant not to.
        static System.Random _walkRng => _animation.WalkRng;

        // Three uniforms sum to a bounded bell, which is what a walk that must not bolt wants.
        static float Gauss() =>
            (float)(_walkRng.NextDouble() + _walkRng.NextDouble() + _walkRng.NextDouble()) - 1.5f;

        static void Walk(float t)
        {
            // Ornstein-Uhlenbeck: left free the walk settles at an end of the band and stays.
            if (_preset.Depths > 1)
            {
                if (t - _walkAt > WalkGapMax) _walkAt = t - WalkStep;
                while (t - _walkAt >= WalkStep)
                {
                    _walkAt += WalkStep;
                    _depth = Mathf.Clamp01(_depth + (0.5f - _depth) * WalkPull + WalkJitter * Gauss());
                }
            }

            if (_preset.Phases < 2) return;

            // A closed preset's phases are the loop itself and must arrive in order.
            if (_preset.Closed)
            {
                _phase = Mathf.FloorToInt(t / LoopSecs * _preset.Phases) % _preset.Phases;
                return;
            }

            if (t - _phaseAt < PhaseSecs) return;
            _phaseAt = t;

            // Drawn from the others, so a redraw always redraws; picking uniformly would repeat
            // one time in Phases, which reads as the animation catching.
            int step = 1 + _walkRng.Next(_preset.Phases - 1);
            _phase = (_phase + step) % _preset.Phases;
        }

        // False means we could not get any and the caller should stand down.
        static bool Ready(Texture2D source)
        {
            if (source == null) return false;

            Preset preset = Chosen;
            string key = Key(source, preset);
            if (_frames != null && _srcKey == key) return true;

            _src = source;
            _srcKey = key;
            _preset = preset;
            _began = -1f;

            // The set being replaced is left to Unity. Its textures are marked
            // DontUnloadUnusedAsset, so they stay resident - the same price an expansion
            // background switch has always cost, paid once more if the setting is turned over.
            try
            {
                _frames = Load(key, preset) ?? Bake(source, key, preset);
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

            if (_frames != null) Sweep(key);
            return _frames != null;
        }

        // The name changes when the player picks another expansion's background, so keying on
        // it is what makes that switch rebake. The preset is in here for the same reason and
        // one more: both sets survive on disk, so turning the setting back is a load rather
        // than a second bake.
        static string Key(Texture2D src, Preset preset)
        {
            string name = string.IsNullOrEmpty(src.name) ? "bg" : src.name;
            return $"{name}-{src.width}x{src.height}-{preset.Name}{preset.Total}-{Tuning}-v{Version}";
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
        static void Sweep(string keep)
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

        static Texture2D[] Load(string key, Preset preset)
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

        static Shared Prep(Preset preset, Color[] clean, int w, int h) =>
            preset == Sparkling
                ? new Shared { Haze = Haze(w, h), Sparks = Constellation(w, h) }
                : new Shared { Fuel = Fuel(clean, w, h) };

        // How far into the preset a stage has arrived, and which draw of the moving part it
        // holds. The ramp's phase keeps moving, so the fire is alive while the picture arrives.
        static void Where(Preset preset, int stage, out float k, out int phase)
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

        static void Stage(Preset preset, Color[] clean, Color[] dst, Shared shared,
                          int w, int h, int stage)
        {
            Where(preset, stage, out float k, out int phase);
            if (preset == Sparkling)
                Sparkle(clean, dst, shared.Haze, shared.Sparks, w, h, phase / (float)preset.Phases, k);
            else
                Rot(clean, dst, shared.Fuel, w, h, k, phase);
        }

        static Texture2D[] Bake(Texture2D src, string key, Preset preset)
        {
            int w = src.width, h = src.height;
            float scale = Mathf.Min(1f, (float)MaxSide / Mathf.Max(w, h));
            int bw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int bh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            Color[] clean = Downsample(ReadBack(src), w, h, bw, bh);
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

        // Read bundle textures back from the GPU because they are non-readable.
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

        // Root generated textures across map switches.
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

    // Patch the draw path: loading screens bypass MainMenuDrawer.Init, and BackgroundOnGUI owns
    // aspect fitting, letterboxing, and the expansion crossfade.
    [HarmonyPatch(typeof(UI_BackgroundMain), nameof(UI_BackgroundMain.BackgroundOnGUI))]
    public static class Patch_MenuBackgroundRot
    {
        static void Prefix(UI_BackgroundMain __instance)
        {
            Texture2D src = __instance.overrideBGImage;

            // Never rebake from an already-rotted frame.
            if (MenuBackground.IsOurs(src)) src = null;

            // Supply vanilla's source only before the first bake; ContentFinder scans loaded mods.
            if (src == null && !MenuBackground.HasFrames)
                src = ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false);

            Texture2D frame = MenuBackground.Current(src);
            if (frame != null) __instance.overrideBGImage = frame;
        }
    }
}
