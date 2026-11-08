using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Verse;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Owns the offline cache and image-generation side of the menu background.
    internal static partial class MenuBackgroundBake
    {
        // Source content is hashed once per immutable content-pack texture. Names and sizes
        // can collide across expansions or mods; instance IDs do not survive a restart.
        internal static string Key(Texture2D src, MenuBackgroundPreset preset)
        {
            string identity =
                $"{MenuBackgroundSource.Identity(src)}-{src.width}x{src.height}-{preset.Name}{preset.Total}-{Tuning}-v{MenuBackgroundTuning.Version}";
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }

        // Every number the pixels depend on, hashed in. Under Version alone, a tuning change
        // that forgot to bump it rendered the old numbers - indistinguishable from a constant
        // that did nothing.
        static readonly string Tuning = HashTuning();

        static string HashTuning()
        {
            var sb = new StringBuilder();
            foreach (
                float f in new[]
                {
                    BreatheLow,
                    BreatheMid,
                    BreatheHigh,
                    OnsetSecs,
                    FireFrom,
                    FuelFloor,
                    FuelFull,
                    FuelDecay,
                    NoiseLow,
                    NoiseHigh,
                    FireGain,
                    NoiseFreq,
                    StagePhase,
                    Sick.r,
                    Sick.g,
                    Sick.b,
                    Ember.r,
                    Ember.g,
                    Ember.b,
                    Flame.r,
                    Flame.g,
                    Flame.b,
                }
            )
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture)).Append(';');

            foreach (
                int i in new[]
                {
                    Onset,
                    MaxSide,
                    JpegQuality,
                    FuelErode,
                    NoiseDiv,
                    Octaves,
                    LutSide,
                }
            )
                sb.Append(i).Append(';');

            uint h = 2166136261;
            string all = sb.ToString();
            for (int i = 0; i < all.Length; i++)
            {
                h ^= all[i];
                h *= 16777619;
            }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }

        static string Root() => Path.Combine(Path.Combine(CacheRoot(), "slopworld"), "bg");

        static string Dir(string key) => Path.Combine(Root(), key);

        // Not GenFilePaths, which is config and saves. A derived texture is regenerable and so
        // safe to delete.
        static string CacheRoot()
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdg))
                return xdg;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache"
            );
        }

        // Load touches the write time so regularly used source caches stay resident.
        internal static void Sweep(string keep)
        {
            try
            {
                string root = Root();
                if (!Directory.Exists(root))
                    return;
                DateTime cutoff = DateTime.UtcNow.AddDays(-KeepDays);
                foreach (string dir in Directory.GetDirectories(root))
                {
                    if (Path.GetFileName(dir) == keep)
                        continue;
                    if (Directory.GetLastWriteTimeUtc(dir) >= cutoff)
                        continue;
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] background cache left unswept: {e.Message}");
            }
        }

        static bool _compressionFailed;

        // Keep the JPEG cache portable. Compress one decoded frame at a time, before dropping
        // CPU pixels. LoadImage's implicit DXT conversion differs between Unity versions.
        // Both fresh bakes and cache hits pass through here for identical playback pixels.
        static bool LoadFrame(Texture2D tex, byte[] jpg)
        {
            if (!tex.LoadImage(jpg, false))
                return false;
            if (
                !_compressionFailed
                && SystemInfo.SupportsTextureFormat(TextureFormat.DXT1)
                && tex.width % 4 == 0
                && tex.height % 4 == 0
            )
            {
                try
                {
                    tex.Compress(true);
                    if (tex.format != TextureFormat.DXT1)
                        throw new InvalidOperationException($"expected DXT1, got {tex.format}");
                }
                catch (Exception e)
                {
                    _compressionFailed = true;
                    Log.Warning(
                        $"[SlopWorld] background BC1 unavailable, using RGB24: {e.Message}"
                    );
                    // Compression may have changed the texture before failing. Restore from
                    // the JPEG in an explicit RGB24 texture, avoiding implicit DXT loading.
                    if (!tex.Reinitialize(2, 2, TextureFormat.RGB24, false))
                        return false;
                    if (!tex.LoadImage(jpg, false))
                        return false;
                }
            }
            tex.Apply(false, true);
            return true;
        }

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
                ? Mathf.Lerp(
                    BreatheLow,
                    BreatheHigh,
                    (at / preset.Phases) / (float)(preset.Depths - 1)
                )
                : 1f;
        }

        static void Stage(
            MenuBackgroundPreset preset,
            Color[] clean,
            Color[] dst,
            float[] fuel,
            int w,
            int h,
            int stage
        )
        {
            Where(preset, stage, out float k, out int phase);
            Rot(clean, dst, fuel, w, h, k, phase);
        }

        // Averaging rather than sampling: point-sampling a starfield at half scale throws away
        // half the stars.
        static Color[] Downsample(Color[] src, int w, int h, int bw, int bh)
        {
            if (bw == w && bh == h)
                return src;

            var dst = new Color[bw * bh];
            float sx = (float)w / bw,
                sy = (float)h / bh;

            for (int y = 0; y < bh; y++)
            {
                int y0 = Mathf.FloorToInt(y * sy);
                int y1 = Mathf.Min(h, Mathf.Max(y0 + 1, Mathf.FloorToInt((y + 1) * sy)));

                for (int x = 0; x < bw; x++)
                {
                    int x0 = Mathf.FloorToInt(x * sx);
                    int x1 = Mathf.Min(w, Mathf.Max(x0 + 1, Mathf.FloorToInt((x + 1) * sx)));

                    float r = 0f,
                        g = 0f,
                        b = 0f;
                    int n = 0;

                    for (int j = y0; j < y1; j++)
                    {
                        for (int i = x0; i < x1; i++)
                        {
                            Color c = src[j * w + i];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            n++;
                        }
                    }

                    dst[y * bw + x] = new Color(r / n, g / n, b / n, 1f);
                }
            }

            return dst;
        }

        // Every returned set is owned by its caller until it is installed or released.
        internal static void DestroyFrames(Texture2D[] frames)
        {
            if (frames == null)
                return;
            for (int i = 0; i < frames.Length; i++)
                if (frames[i] != null)
                    UnityEngine.Object.Destroy(frames[i]);
        }

        static readonly ConditionalWeakTable<Texture2D, object> Generated = new();
        static readonly object GeneratedMarker = new();

        // Native background fields may still reference a retired frame until their next
        // draw. Recognize those too, without rooting retired textures or rebaking from them.
        internal static bool IsGenerated(Texture2D texture) =>
            !ReferenceEquals(texture, null) && Generated.TryGetValue(texture, out _);

        // Root generated textures across map switches.
        static void Keep(Texture2D tex)
        {
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Generated.Add(tex, GeneratedMarker);
        }
    }
}
