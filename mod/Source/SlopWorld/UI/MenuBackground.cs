using System;
using System.IO;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The menu and loading-screen background, rotting. The game's own planet is
    /// what comes up - not a picture of ours - and what happens to it is a filter
    /// stack run over the game's own pixels on the player's own machine. Nothing
    /// here ships art: the source is whatever background this install was going to
    /// draw anyway, and the corrupted frames are baked from it on first run.
    ///
    /// That is the point of the effect as much as it is a licence question. The
    /// planet the player has looked at for six hundred hours going wrong in front
    /// of them is the whole joke, and it only works if it is demonstrably the same
    /// planet underneath.
    ///
    /// Baked rather than computed per frame because the source is 4096x2560 and a
    /// separable blur over ten megapixels is not a thing to do at 60fps. Cached to
    /// disk rather than rebuilt per launch because <see cref="Stages"/> frames of
    /// it is a few seconds of hitch, and a restart is cheap here by design (see
    /// AutoResume) - a cost paid once per install is fine, paid once per launch is
    /// the thing that would make a redeploy expensive.
    ///
    /// The playback breathes rather than settling: a single end state is a picture
    /// again, and the eye stops seeing a picture in about a second. Two sines of
    /// incommensurate period, summed, so it never repeats visibly - a single sine
    /// is a pulse, and a pulse is a metronome.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MenuBackground
    {
        /// Frames across the rot, and the whole of what the animation's smoothness
        /// is: <see cref="Current"/> picks one and cuts to it, so a stage is a frame
        /// and there is nothing blending between them.
        ///
        /// Fewer of them are seen than are baked. The breath rides
        /// <see cref="BreatheLow"/>..<see cref="BreatheHigh"/>, so once the onset is
        /// over the index only ever walks the top 55% of the range - at twelve that
        /// is stages 5..11, seven frames, where eight stages gave five. The rest are
        /// the way in from clean and are each seen once.
        ///
        /// Straight linear in the cache and the decode, which is the reason this is
        /// twelve and not thirty.
        const int Stages = 12;

        /// The long side the frames are baked at. Vanilla's is 4096, which is more
        /// than any screen this draws to and four times the filter cost; a picture
        /// this degraded has no detail left to lose.
        const int MaxSide = 2048;

        /// Where the breath sits. It never returns to clean - the mod is loaded and
        /// stays loaded - but it does not sit at the bottom of the well either.
        const float BreatheLow = 0.45f;
        const float BreatheHigh = 1.0f;

        /// The two periods, in seconds. Deliberately not a ratio of small integers.
        const float SlowSecs = 11.3f;
        const float FastSecs = 4.1f;

        /// How long the first rot takes, once, on the way in from clean.
        const float OnsetSecs = 6f;

        /// The sickness the palette is pulled toward - the plague's own pink, so the
        /// menu and the map are describing the same thing. Matches SlopPlagueGas in
        /// Defs/Flecks.xml by eye rather than by reference: a def lookup at bake
        /// time for a number that is not going to move is not worth it.
        static readonly Color Sick = new Color(0.95f, 0.42f, 0.72f, 1f);

        /// The fire. It starts a third of the way down the rot rather than at the
        /// top, so the first thing that happens to the planet is that it goes wrong
        /// and only then that it goes up - a picture that is already alight at stage
        /// one has nowhere left to travel.
        const float FireFrom = 0.30f;
        /// The band of luminance that counts as something which can burn: nothing
        /// below the floor, fully alight at the top. Both numbers are read off the
        /// picture rather than picked - the source's median luminance is 0.09 and
        /// its 95th percentile is 0.69, so a ramp that only reaches 1 at pure white
        /// leaves the lit face burning at a third and the fire invisible. The gate
        /// has to span the range the image actually occupies, not the range the
        /// format allows.
        const float FuelFloor = 0.40f;
        const float FuelFull = 0.70f;
        /// How far a bright thing has to extend before it counts as fuel, in pixels
        /// of the baked frame. This is the star filter; see <see cref="Erode"/>.
        const int FuelErode = 3;
        /// How far heat climbs off what is lit, per row. 0.985 over a 1280-row frame
        /// is a plume of a hundred-odd pixels - flames off the lit face, not a glow
        /// filling the sky.
        const float FuelDecay = 0.985f;
        /// The noise band the flame is cut out of. fBm piles up around its middle,
        /// so taking the whole 0..1 range as-is gives an even shimmer over
        /// everything that could burn; stretching this band out to the full range
        /// is what gives a flame a lit body and a dark gap beside it.
        const float NoiseLow = 0.38f;
        const float NoiseHigh = 0.72f;
        /// How hard the fire is added over the grade.
        const float FireGain = 1.15f;

        /// The noise field is drawn at 1/N of the frame and read back bilinear.
        const int NoiseDiv = 4;
        const float NoiseFreq = 0.035f;
        const int Octaves = 4;
        /// How far each stage walks through the noise. Large enough that consecutive
        /// stages are unrelated draws rather than the same flame nudged along - the
        /// breathing runs up and down this axis, and a fire that slid smoothly would
        /// read as a pan across a still picture.
        const float StagePhase = 37.7f;

        static readonly Color Ember = new Color(1f, 0.24f, 0.05f, 1f);
        static readonly Color Flame = new Color(1f, 0.76f, 0.28f, 1f);

        /// Cache format. Bump it and every install rebakes on the next launch,
        /// which is what a change to any constant above needs.
        const int Version = 4;

        static Texture2D[] _frames;
        /// The pixels everything is cut from, and what they were called - the cache
        /// key, and how a background switched in Options is noticed.
        static Texture2D _src;
        static string _srcKey;
        /// When the mod first got its hands on the screen, so the onset ramp has a
        /// zero. Negative until the first draw.
        static float _began = -1f;

        /// <summary>Whether there is a baked set standing, which is also whether a
        /// caller needs to go and find the source texture again.</summary>
        public static bool HasFrames => _frames != null;

        /// <summary>The frame this instant wants. A null <paramref name="source"/>
        /// means "whatever you baked from last time", which is what the draw hook
        /// passes once its own field is holding one of our frames.
        ///
        /// Null back means there is nothing baked and nothing to bake from, and the
        /// caller should leave the field alone - the right failure here being a menu
        /// with the game's own untouched background on it.</summary>
        public static Texture2D Current(Texture2D source)
        {
            if (source != null && !Ready(source)) return null;
            if (_frames == null) return null;

            float now = Time.realtimeSinceStartup;
            if (_began < 0f) _began = now;
            float t = now - _began;

            // Sum of two sines, normalised back to 0..1. Amplitudes are unequal so
            // the fast one reads as a tremor over the slow swell rather than as a
            // second beat of its own.
            float breath =
                0.68f * Mathf.Sin(t * 2f * Mathf.PI / SlowSecs) +
                0.32f * Mathf.Sin(t * 2f * Mathf.PI / FastSecs);
            float band = Mathf.Lerp(BreatheLow, BreatheHigh, (breath + 1f) * 0.5f);

            // The way in: clean to the breathing band, once. Smoothstep rather than
            // linear because a ramp that starts instantly reads as a cut.
            float onset = OnsetSecs <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / OnsetSecs));

            int i = Mathf.Clamp(Mathf.RoundToInt(band * onset * (Stages - 1)), 0, Stages - 1);
            return _frames[i];
        }

        /// <summary>Frames for this source, from the cache or from the filters.
        /// False means we could not get any and the caller should stand down.</summary>
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
                // A background that will not bake is a cosmetic loss, and the menu
                // behind it still works. Nothing here is worth taking the mod down.
                Log.Warning($"[SlopWorld] background bake failed, leaving it clean: {e.Message}");
                _frames = null;
            }

            return _frames != null;
        }

        /// <summary>Name, size and version. The name is what changes when the player
        /// picks another expansion's background in Options, so keying on it is what
        /// makes that switch rebake instead of showing the wrong planet rotted.</summary>
        static string Key(Texture2D src)
        {
            string name = string.IsNullOrEmpty(src.name) ? "bg" : src.name;
            return $"{name}-{src.width}x{src.height}-{Stages}-v{Version}";
        }

        static string Dir(string key) =>
            Path.Combine(Path.Combine(CacheRoot(), "slopworld"), Path.Combine("bg", key));

        /// <summary>XDG, with the spec's own fallback. Not GenFilePaths: that is the
        /// game's config and saves, and a derived texture is neither - it is
        /// regenerable, which is exactly what a cache directory means and exactly
        /// what makes it safe for a user to delete.</summary>
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
                string path = Path.Combine(dir, $"{i:D2}.png");
                if (!File.Exists(path)) return null;

                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;                    // a half-written cache rebakes
                }
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

            var frames = new Texture2D[Stages];
            for (int i = 0; i < Stages; i++)
            {
                Color[] px = Rot(clean, fuel, bw, bh, i / (float)(Stages - 1), i);

                var tex = new Texture2D(bw, bh, TextureFormat.RGB24, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.SetPixels(px);
                tex.Apply();
                Keep(tex);
                frames[i] = tex;

                File.WriteAllBytes(Path.Combine(dir, $"{i:D2}.png"), tex.EncodeToPNG());
            }

            Log.Message($"[SlopWorld] baked {Stages} background frames at {bw}x{bh} into {dir}");
            return frames;
        }

        /// <summary>One stage of the rot. <paramref name="k"/> runs 0 (the game's own
        /// picture, untouched) to 1 (as far as this goes).
        ///
        /// Order is load-bearing. The smear goes on first, because blurring after
        /// the contrast crush pulls the crushed values back toward the middle and
        /// undoes it; the tint goes next, because a colour bias applied before a
        /// contrast stretch comes back out of it; and the fire goes on last of all,
        /// because it is emissive - it is light arriving at the lens, not a property
        /// of the surface, so nothing downstream of it should be grading it.</summary>
        static Color[] Rot(Color[] src, float[] fuel, int w, int h, float k, int stage)
        {
            if (k <= 0f) return (Color[])src.Clone();

            // Motion blur: horizontal, because it reads as a signal being dragged
            // rather than as a lens out of focus. Separable and one-dimensional, so
            // the cost is the radius rather than its square.
            int radius = Mathf.RoundToInt(Mathf.Lerp(0f, 24f, k * k));
            Color[] px = radius > 0 ? Smear(src, w, h, radius) : (Color[])src.Clone();

            float contrast = Mathf.Lerp(1f, 2.3f, k);
            float lift = Mathf.Lerp(0f, 0.06f, k);      // blacks off the floor: video, not ink
            float drain = Mathf.Lerp(0f, 0.75f, k);     // toward luminance
            float tint = Mathf.Lerp(0f, 0.42f, k);      // then toward the plague

            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];

                float lum = c.grayscale;
                c.r = Mathf.Lerp(c.r, lum, drain);
                c.g = Mathf.Lerp(c.g, lum, drain);
                c.b = Mathf.Lerp(c.b, lum, drain);

                c.r = (c.r - 0.5f) * contrast + 0.5f + lift;
                c.g = (c.g - 0.5f) * contrast + 0.5f + lift;
                c.b = (c.b - 0.5f) * contrast + 0.5f + lift;

                // Multiplied in rather than blended over: a blend fogs the whole
                // frame toward pink evenly, where multiplying leaves what is already
                // dark dark and puts the colour where there is light to carry it.
                c.r = Mathf.Lerp(c.r, c.r * Sick.r, tint);
                c.g = Mathf.Lerp(c.g, c.g * Sick.g, tint);
                c.b = Mathf.Lerp(c.b, c.b * Sick.b, tint);

                px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }

            Burn(px, fuel, w, h, k, stage);
            return px;
        }

        /// <summary>What can catch, and where the heat off it goes. Read off the
        /// clean picture rather than the graded one so the fire sits on the planet's
        /// own lit face - the terminator, the ice caps, the highlights - instead of
        /// wherever the contrast crush happened to leave something bright.
        ///
        /// One upward sweep with a decay, which is the cheap way to say heat rises:
        /// a lit pixel seeds its column and the value climbs, fading, into the dark
        /// above it. Doing it per-pixel with a lookback window instead is the same
        /// picture for twenty times the work, and this runs over ten megapixels.
        ///
        /// Pixels run bottom-up, so the sweep and "up" agree without a flip.</summary>
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

        /// <summary>Specks knocked out of the fuel mask, by taking the darkest value
        /// in a small window around each pixel.
        ///
        /// This is what stands between "the planet is on fire" and a sky full of
        /// vertical streaks. The starfield is single bright pixels on black, and
        /// brightness alone cannot tell one from the lit face of a planet - so every
        /// star seeded a column and the fire climbed all of them. What tells them
        /// apart is not how bright a thing is but how big: a star vanishes under a
        /// window this size and a planet does not notice it. The lit face loses
        /// <see cref="FuelErode"/> pixels off its rim, which is nothing next to the
        /// plume that rises off it.
        ///
        /// Separable, so it is two cheap passes rather than one quadratic one.</summary>
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

        /// <summary>The fire, added over the graded frame. Shaped by fBm so it has
        /// the ragged edge a flame has, gated by <see cref="Fuel"/> so it only burns
        /// where there is something lit to burn, and rolled in with
        /// <paramref name="k"/> so the planet catches as it rots rather than
        /// arriving alight.
        ///
        /// The noise is drawn at a fraction of the frame and read back bilinear:
        /// four octaves of PerlinNoise over ten megapixels is seconds of bake, and
        /// fire has no fine detail to lose - what it has is a soft, moving edge,
        /// which survives the upsample intact.
        ///
        /// Each stage draws its own noise from a different offset, so cycling the
        /// stages is what makes the flames move. That is the whole animation: the
        /// breathing already runs up and down the stages, and fire that redraws
        /// itself on every step of that reads as guttering rather than as a
        /// still picture being faded.</summary>
        static void Burn(Color[] px, float[] fuel, int w, int h, float k, int stage)
        {
            float heat = Mathf.InverseLerp(FireFrom, 1f, k);
            if (heat <= 0f) return;

            int nw = Mathf.Max(2, w / NoiseDiv), nh = Mathf.Max(2, h / NoiseDiv);
            float[] noise = Fbm(nw, nh, stage * StagePhase);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                // Where this row falls in the noise, and the two rows to blend.
                float ny = (float)y / h * (nh - 1);
                int ny0 = (int)ny, ny1 = Mathf.Min(nh - 1, ny0 + 1);
                float fy = ny - ny0;

                for (int x = 0; x < w; x++)
                {
                    float f = fuel[row + x];
                    if (f <= 0f) continue;

                    float nx = (float)x / w * (nw - 1);
                    int nx0 = (int)nx, nx1 = Mathf.Min(nw - 1, nx0 + 1);
                    float fx = nx - nx0;

                    float n = Mathf.Lerp(
                        Mathf.Lerp(noise[ny0 * nw + nx0], noise[ny0 * nw + nx1], fx),
                        Mathf.Lerp(noise[ny1 * nw + nx0], noise[ny1 * nw + nx1], fx),
                        fy);

                    // The two do different jobs: fuel says where fire is allowed and
                    // the shaped noise says what shape it takes there. Multiplying
                    // the raw field in instead would let the noise decide both, and
                    // a field that never leaves its middle decides neither.
                    float shaped = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NoiseLow, NoiseHigh, n));
                    float t = f * shaped * heat;
                    if (t <= 0f) continue;

                    // Ember at the ragged edge, flame in the body of it, and added
                    // rather than blended, so it lights the picture instead of
                    // painting over it.
                    Color fire = Color.Lerp(Ember, Flame, t);
                    Color c = px[row + x];
                    px[row + x] = new Color(
                        Mathf.Clamp01(c.r + fire.r * t * FireGain),
                        Mathf.Clamp01(c.g + fire.g * t * FireGain),
                        Mathf.Clamp01(c.b + fire.b * t * FireGain),
                        1f);
                }
            }
        }

        /// <summary>Fractional brownian motion: octaves of PerlinNoise at doubling
        /// frequency and halving weight, normalised back to 0..1. Each octave is
        /// stretched twice as far across x as up y, which is what gives the field
        /// its vertical grain - flames are tall and thin, and isotropic noise is
        /// a cloud.</summary>
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
                        sum += amp * Mathf.PerlinNoise(
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

        /// <summary>A box blur along x, clamped at the edges. Box rather than
        /// gaussian because it is being run at a radius where the difference is not
        /// visible through the contrast crush that follows it, and because a running
        /// sum makes it independent of the radius.</summary>
        static Color[] Smear(Color[] src, int w, int h, int radius)
        {
            var dst = new Color[src.Length];
            float n = radius * 2 + 1;

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

                for (int x = 0; x < w; x++)
                {
                    dst[row + x] = new Color(r / n, g / n, b / n, 1f);

                    Color outgoing = src[row + Mathf.Clamp(x - radius, 0, w - 1)];
                    Color incoming = src[row + Mathf.Clamp(x + radius + 1, 0, w - 1)];
                    r += incoming.r - outgoing.r;
                    g += incoming.g - outgoing.g;
                    b += incoming.b - outgoing.b;
                }
            }

            return dst;
        }

        /// <summary>Box-average down to the bake size. Averaging every source pixel
        /// that lands in a target one rather than sampling: this is a starfield, and
        /// point-sampling a starfield at half scale throws away half the stars.</summary>
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

        /// <summary>Core textures come out of the bundles unreadable, so the pixels
        /// have to be fetched back off the GPU - the same trip <see cref="DeadCursor"/>
        /// makes, and for the same reason.</summary>
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

        /// <summary>A generated texture is an asset nothing in the scene references,
        /// so the Resources.UnloadUnusedAssets the game runs on any map switch is
        /// entitled to take it - the trap TerminalFont documents. Held frames come
        /// back as nulls and the menu draws black.</summary>
        static void Keep(Texture2D tex) => tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

        /// <summary>Whether a texture is one of ours, so the source capture below
        /// never bakes the rot from an already-rotted frame.</summary>
        public static bool IsOurs(Texture2D tex)
        {
            if (tex == null || _frames == null) return false;
            for (int i = 0; i < _frames.Length; i++)
                if (ReferenceEquals(_frames[i], tex)) return true;
            return false;
        }
    }

    /// <summary>
    /// Where the rot is put on. A prefix on the draw rather than a postfix on
    /// MainMenuDrawer.Init, for two reasons: Init runs once and this has to change
    /// every frame, and the loading screen draws this same background through
    /// Patch_LoadingLayout without going anywhere near Init - so hooking the draw
    /// is what gets the effect onto both screens from one place.
    ///
    /// overrideBGImage is an instance field, and vanilla's own BackgroundOnGUI
    /// reads its width and height to do the aspect fit. So handing it our frame is
    /// the whole integration: the scaling, the letterboxing and the expansion
    /// crossfade all keep working, and they work on the rotted picture because it
    /// carries the source's proportions.
    ///
    /// The source is captured before we ever write to that field, and re-captured
    /// whenever something that is not one of our own frames turns up in it - which
    /// is how a background changed in Options is noticed, since Init writes the new
    /// one straight back over ours.
    /// </summary>
    [HarmonyPatch(typeof(UI_BackgroundMain), nameof(UI_BackgroundMain.BackgroundOnGUI))]
    public static class Patch_MenuBackgroundRot
    {
        static void Prefix(UI_BackgroundMain __instance)
        {
            Texture2D src = __instance.overrideBGImage;

            // Our own frame from last time. Handing that back as a source would key
            // the cache off a rotted picture and then rot it again, so it is dropped
            // and the bake we already have answers.
            if (MenuBackground.IsOurs(src)) src = null;

            // A null field means vanilla is about to draw its own BGPlanet without
            // consulting the field at all, so we fetch the same texture it would -
            // but only when there is nothing baked yet, because ContentFinder.Get
            // walks every loaded mod and this runs once a frame.
            if (src == null && !MenuBackground.HasFrames)
                src = ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false);

            Texture2D frame = MenuBackground.Current(src);
            if (frame != null) __instance.overrideBGImage = frame;
        }
    }
}
