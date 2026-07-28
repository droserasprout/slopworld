using System;
using System.IO;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The menu and loading-screen background, rotting. The game's own planet is what
    // comes up and the corrupted frames are baked from it on first run: nothing here
    // ships art, and the joke only works if it is demonstrably the same planet
    // underneath.
    //
    // Baked rather than computed per frame because the source is 4096x2560, and
    // cached to disk rather than per launch because a restart is cheap here by
    // design. The playback breathes on two sines of incommensurate period, summed, so
    // it never repeats visibly - a single sine is a metronome.
    [StaticConstructorOnStartup]
    public static class MenuBackground
    {
        // Current picks one and cuts to it, so a stage is a frame with nothing blending
        // between them. Fewer are seen than are baked: the breath only ever walks the top
        // 55% of the range, and the rest are the way in from clean.
        const int Stages = 12;

        // Vanilla's is 4096, which is more than any screen this draws to and four times
        // the filter cost.
        const int MaxSide = 2048;

        // It never returns to clean, but it does not sit at the bottom of the well
        // either.
        const float BreatheLow = 0.45f;
        const float BreatheHigh = 1.0f;

        // The two periods, in seconds. Deliberately not a ratio of small integers.
        const float SlowSecs = 11.3f;
        const float FastSecs = 4.1f;

        // How long the first rot takes, once, on the way in from clean.
        const float OnsetSecs = 6f;

        // The plague's own pink, so the menu and the map describe the same thing. Matches
        // SlopPlagueGas by eye rather than by reference.
        static readonly Color Sick = new Color(0.95f, 0.42f, 0.72f, 1f);

        // Starts a third of the way down the rot, so the planet goes wrong before it goes
        // up - a picture already alight at stage one has nowhere to travel.
        const float FireFrom = 0.30f;
        // Read off the picture rather than picked: the source's median luminance is 0.09
        // and its 95th percentile 0.69, so a ramp that only reaches 1 at pure white
        // leaves the fire invisible.
        const float FuelFloor = 0.40f;
        const float FuelFull = 0.70f;
        // How far a bright thing has to extend before it counts as fuel. This is the star
        // filter; see Erode.
        const int FuelErode = 3;
        // 0.985 over a 1280-row frame is a plume of a hundred-odd pixels.
        const float FuelDecay = 0.985f;
        // fBm piles up around its middle, so taking 0..1 as-is gives an even shimmer;
        // stretching this band out is what gives a flame a lit body and a dark gap.
        const float NoiseLow = 0.38f;
        const float NoiseHigh = 0.72f;
        const float FireGain = 1.15f;

        // The noise field is drawn at 1/N of the frame and read back bilinear.
        const int NoiseDiv = 4;
        const float NoiseFreq = 0.035f;
        const int Octaves = 4;
        // Large enough that consecutive stages are unrelated draws: a fire that slid
        // smoothly would read as a pan across a still picture.
        const float StagePhase = 37.7f;

        static readonly Color Ember = new Color(1f, 0.24f, 0.05f, 1f);
        static readonly Color Flame = new Color(1f, 0.76f, 0.28f, 1f);

        // Bump it and every install rebakes, which is what a change to any constant above
        // needs.
        const int Version = 4;

        static Texture2D[] _frames;
        // The cache key, and how a background switched in Options is noticed.
        static Texture2D _src;
        static string _srcKey;
        // So the onset ramp has a zero. Negative until the first draw.
        static float _began = -1f;

        public static bool HasFrames => _frames != null;

        // A null source means "whatever you baked from last time". Null back means there
        // is nothing to bake from and the caller should leave the field alone.
        public static Texture2D Current(Texture2D source)
        {
            if (source != null && !Ready(source)) return null;
            if (_frames == null) return null;

            float now = Time.realtimeSinceStartup;
            if (_began < 0f) _began = now;
            float t = now - _began;

            // Amplitudes are unequal so the fast one reads as a tremor over the slow swell
            // rather than a second beat of its own.
            float breath =
                0.68f * Mathf.Sin(t * 2f * Mathf.PI / SlowSecs) +
                0.32f * Mathf.Sin(t * 2f * Mathf.PI / FastSecs);
            float band = Mathf.Lerp(BreatheLow, BreatheHigh, (breath + 1f) * 0.5f);

            // Smoothstep rather than linear, because a ramp that starts instantly is a cut.
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
                // A background that will not bake is a cosmetic loss; nothing here is worth
                // taking the mod down.
                Log.Warning($"[SlopWorld] background bake failed, leaving it clean: {e.Message}");
                _frames = null;
            }

            return _frames != null;
        }

        // The name is what changes when the player picks another expansion's background,
        // so keying on it is what makes that switch rebake.
        static string Key(Texture2D src)
        {
            string name = string.IsNullOrEmpty(src.name) ? "bg" : src.name;
            return $"{name}-{src.width}x{src.height}-{Stages}-v{Version}";
        }

        static string Dir(string key) =>
            Path.Combine(Path.Combine(CacheRoot(), "slopworld"), Path.Combine("bg", key));

        // Not GenFilePaths: that is the game's config and saves, and a derived texture is
        // regenerable, which is what makes it safe for a user to delete.
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

        // Order is load-bearing. The smear goes first, because blurring after the
        // contrast crush undoes it; the tint next, because a colour bias applied before a
        // stretch comes back out of it; the fire last, because it is emissive - light
        // arriving at the lens, not a property of the surface.
        static Color[] Rot(Color[] src, float[] fuel, int w, int h, float k, int stage)
        {
            if (k <= 0f) return (Color[])src.Clone();

            // Horizontal, because it reads as a signal being dragged rather than a lens out
            // of focus. Separable, so the cost is the radius rather than its square.
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

                // Multiplied in rather than blended over: a blend fogs the whole frame evenly,
                // where this leaves what is dark dark and puts the colour where there is light.
                c.r = Mathf.Lerp(c.r, c.r * Sick.r, tint);
                c.g = Mathf.Lerp(c.g, c.g * Sick.g, tint);
                c.b = Mathf.Lerp(c.b, c.b * Sick.b, tint);

                px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }

            Burn(px, fuel, w, h, k, stage);
            return px;
        }

        // Read off the clean picture rather than the graded one, so the fire sits on the
        // planet's own lit face. One upward sweep with a decay is the cheap way to say
        // heat rises, and this runs over ten megapixels. Pixels run bottom-up, so the
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

        // What stands between "the planet is on fire" and a sky full of vertical streaks.
        // The starfield is single bright pixels on black, and brightness alone cannot
        // tell one from a lit planet - what can is size: a star vanishes under a window
        // this wide and a planet does not notice it.
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

        // Gated by Fuel so it only burns where there is something lit, and rolled in with
        // k so the planet catches as it rots. The noise is drawn at a fraction of the
        // frame and read back bilinear: four octaves over ten megapixels is seconds of
        // bake, and fire has no fine detail to lose. Each stage draws from a different
        // offset, which is what makes the flames move as the breath walks the stages.
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

                    // Fuel says where fire is allowed and the shaped noise what shape it takes there.
                    // Multiplying the raw field in would let the noise decide both.
                    float shaped = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NoiseLow, NoiseHigh, n));
                    float t = f * shaped * heat;
                    if (t <= 0f) continue;

                    // Added rather than blended, so it lights the picture instead of painting over
                    // it.
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

        // Each octave is stretched twice as far across x as up y, which gives the field
        // its vertical grain - isotropic noise is a cloud.
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

        // Box rather than gaussian: at this radius the difference is invisible through
        // the contrast crush, and a running sum makes it independent of the radius.
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

        // Averaging rather than sampling: this is a starfield, and point-sampling one at
        // half scale throws away half the stars.
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

        // Core textures come out of the bundles unreadable, so the pixels have to be
        // fetched back off the GPU - the trip DeadCursor makes too.
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

        // A generated texture is an asset nothing in the scene references, so the
        // Resources.UnloadUnusedAssets on any map switch is entitled to take it - the
        // trap TerminalFont documents.
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

    // A prefix on the draw rather than a postfix on MainMenuDrawer.Init: Init runs
    // once where this has to change every frame, and the loading screen draws this
    // same background without going near Init.
    //
    // overrideBGImage is an instance field and vanilla's BackgroundOnGUI reads its
    // size to do the aspect fit, so handing it our frame is the whole integration -
    // scaling, letterboxing and the expansion crossfade all keep working.
    [HarmonyPatch(typeof(UI_BackgroundMain), nameof(UI_BackgroundMain.BackgroundOnGUI))]
    public static class Patch_MenuBackgroundRot
    {
        static void Prefix(UI_BackgroundMain __instance)
        {
            Texture2D src = __instance.overrideBGImage;

            // Handing our own frame back as a source would key the cache off a rotted picture
            // and rot it again.
            if (MenuBackground.IsOurs(src)) src = null;

            // A null field means vanilla is about to draw BGPlanet without consulting it, so
            // we fetch the same texture - but only when nothing is baked, because
            // ContentFinder.Get walks every loaded mod and this runs once a frame.
            if (src == null && !MenuBackground.HasFrames)
                src = ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false);

            Texture2D frame = MenuBackground.Current(src);
            if (frame != null) __instance.overrideBGImage = frame;
        }
    }
}
