using System;
using System.IO;
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

        // The artefacts are the point: 8x8 blocking and smeared chroma are the failure the
        // rest of this file imitates by hand, and the encoder does them for free. Also keeps
        // the cache to a few megabytes where PNG would be most of a gigabyte.
        const int JpegQuality = 10;

        // Bump it and every install rebakes - what a change to any constant above needs.
        const int Version = 1;

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
                // A background that will not bake is a cosmetic loss.
                Log.Warning($"[SlopWorld] background bake failed, leaving it clean: {e.Message}");
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

            var frames = new Texture2D[Stages];
            for (int i = 0; i < Stages; i++)
            {
                Color[] px = Rot(clean, fuel, bw, bh, i / (float)(Stages - 1), i);

                var tex = new Texture2D(bw, bh, TextureFormat.RGB24, false);
                tex.SetPixels(px);
                tex.Apply();

                byte[] jpg = tex.EncodeToJPG(JpegQuality);
                File.WriteAllBytes(Path.Combine(dir, $"{i:D2}.jpg"), jpg);

                // Back in through the decoder: at this quality the difference is most of the
                // look, and a first launch that came up clean-edged then blocked itself on
                // restart would be reported as a bug.
                tex.LoadImage(jpg);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Keep(tex);
                frames[i] = tex;
            }

            Log.Message($"[SlopWorld] baked {Stages} background frames at {bw}x{bh}, jpeg q{JpegQuality}, into {dir}");
            return frames;
        }

        // Order is load-bearing: smear first, because blurring after the contrast crush undoes
        // it; tint next, because a colour bias applied before a stretch comes back out of it;
        // fire last, being emissive. The encoder's damage lands after all of it, being the
        // transmission rather than the scene.
        static Color[] Rot(Color[] src, float[] fuel, int w, int h, float k, int stage)
        {
            if (k <= 0f) return (Color[])src.Clone();

            // Horizontal: a signal being dragged rather than a lens out of focus.
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

                // Multiplied rather than blended: a blend fogs the frame evenly, where this
                // leaves the dark dark and puts colour where there is light.
                c.r = Mathf.Lerp(c.r, c.r * Sick.r, tint);
                c.g = Mathf.Lerp(c.g, c.g * Sick.g, tint);
                c.b = Mathf.Lerp(c.b, c.b * Sick.b, tint);

                px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }

            Burn(px, fuel, w, h, k, stage);
            return px;
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

                    // Fuel says where fire is allowed, the shaped noise what shape it takes.
                    // Multiplying the raw field in would let the noise decide both.
                    float shaped = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NoiseLow, NoiseHigh, n));
                    float t = f * shaped * heat;
                    if (t <= 0f) continue;

                    // Added rather than blended, so it lights the picture rather than painting
                    // over it.
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

        // Box rather than gaussian: through the contrast crush the difference is invisible, and
        // a running sum makes the cost independent of the radius.
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
