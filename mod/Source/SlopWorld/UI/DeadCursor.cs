using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The pointer, replaced with the Tame designator's hand: mirrored so it
    /// reaches up-left the way the vanilla arrow points, and drained to a
    /// lifeless grey, because there is nothing left down there to tame.
    ///
    /// Except the cat. <see cref="Pat"/> waggles the hand for half a second when
    /// one is patted - the one thing on this map the pointer still has a use for,
    /// and the only time the grey moves.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DeadCursor
    {
        /// The vanilla arrow is 32, which is too polite to notice; X11 has no
        /// trouble with a hardware cursor this size.
        const int N = 48;
        /// The outline keeps its weight, the skin flattens out: grey, not pale.
        const float Floor = 0.20f;
        const float Range = 0.68f;

        /// A turned square is wider than the square: at the swing below it needs
        /// N*(cos+sin), a shade over 62, so the spun frames get a canvas of their
        /// own or the hand loses a corner. Still inside what X11 is safe at.
        const int M = 64;

        /// How far the hand swings, how many times it goes out and back, and how
        /// long all of it takes. Counter-clockwise and back rather than side to
        /// side: a hand that crosses straight reads as a metronome.
        const float SpinDegrees = 22f;
        const int SpinWaggles = 2;
        const float SpinSeconds = 0.5f;
        /// Frames cut across the swing. Twelve is smooth at this size and it is
        /// twelve textures, built the first time a cat is patted and then kept.
        const int SpinSteps = 12;

        static Texture2D _tex;
        static Vector2 _hotspot;
        /// The resting pixels, kept because every spun frame is cut from them.
        static Color[] _px;

        static readonly Texture2D[] _spun = new Texture2D[SpinSteps + 1];
        static readonly Vector2[] _spunHot = new Vector2[SpinSteps + 1];

        /// Real time the waggle ends, negative while the hand is still; and which
        /// frame the pointer is actually wearing, so a still hand costs no calls.
        static float _spinUntil = -1f;
        static int _shown;

        static DeadCursor()
        {
            Apply();
        }

        /// <summary>Hands the cursor to Unity. Cheap enough to call on every
        /// prefs change, which is the only time the game touches the cursor.</summary>
        public static void Apply()
        {
            if (_tex == null) Build();
            Cursor.SetCursor(_tex, _hotspot, CursorMode.Auto);
            // Whatever frame was up is gone; a waggle in progress puts its next
            // one back on the very next Update.
            _shown = 0;
        }

        /// <summary>Sets the hand waggling, which is what a pat looks like from
        /// this end - see <see cref="Pets.Poke"/>, the only caller. A pat while it
        /// is already going is one that has been answered, so it is dropped rather
        /// than snapping the swing back to straight.</summary>
        public static void Pat()
        {
            if (_spinUntil >= 0f) return;
            if (_tex == null) Build();
            _spinUntil = Time.realtimeSinceStartup + SpinSeconds;
        }

        /// <summary>Drives the waggle, once a frame off <c>Root.Update</c>. A
        /// hardware cursor is one still image, so an animation is a texture per
        /// frame and there is nowhere else to put it.</summary>
        public static void Tick()
        {
            if (_spinUntil < 0f) return;

            float left = _spinUntil - Time.realtimeSinceStartup;
            if (left <= 0f)
            {
                _spinUntil = -1f;
                Show(0);
                return;
            }

            // Half a sine per waggle, so the hand goes out and comes back without
            // ever crossing to the other side of straight.
            float t = 1f - left / SpinSeconds;
            float f = Mathf.Abs(Mathf.Sin(t * SpinWaggles * Mathf.PI));
            Show(Mathf.RoundToInt(f * SpinSteps));
        }

        static void Show(int step)
        {
            if (step == _shown) return;
            _shown = step;

            if (step <= 0)
            {
                Cursor.SetCursor(_tex, _hotspot, CursorMode.Auto);
                return;
            }

            if (_spun[step] == null)
                _spun[step] = Spin(step * SpinDegrees / SpinSteps, out _spunHot[step]);

            Cursor.SetCursor(_spun[step], _spunHot[step], CursorMode.Auto);
        }

        /// <summary>The resting hand turned <paramref name="deg"/> degrees
        /// counter-clockwise about the middle of its canvas, with the hotspot
        /// carried along - so the fingertip stays under the mouse and it is the
        /// hand that swings, not the pointer.
        ///
        /// Sampled nearest-neighbour: this is a hand-drawn icon with a black
        /// outline, and blending it into its own transparent margin greys the
        /// outline out at every angle.</summary>
        static Texture2D Spin(float deg, out Vector2 hotspot)
        {
            float rad = deg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            // Pixels run bottom-up, which is also where a positive angle turns
            // counter-clockwise; the hotspot is flipped back at the end.
            float c = (N - 1) * 0.5f;
            int o = (M - N) / 2;

            var px = new Color[M * M];

            for (int y = 0; y < M; y++)
            {
                for (int x = 0; x < M; x++)
                {
                    // Read where this pixel came from: the turn, undone.
                    float dx = x - o - c, dy = y - o - c;
                    int sxp = Mathf.RoundToInt(c + dx * cos + dy * sin);
                    int syp = Mathf.RoundToInt(c - dx * sin + dy * cos);
                    if (sxp < 0 || sxp >= N || syp < 0 || syp >= N) continue;
                    px[y * M + x] = _px[syp * N + sxp];
                }
            }

            float hx = _hotspot.x - c, hy = (N - 1 - _hotspot.y) - c;
            hotspot = new Vector2(
                o + c + hx * cos - hy * sin,
                (M - 1) - (o + c + hx * sin + hy * cos));

            var tex = new Texture2D(M, M, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static void Build()
        {
            var src = ContentFinder<Texture2D>.Get("UI/Designators/Tame");
            int w = src.width, h = src.height;
            var srcPx = ReadBack(src);

            var px = new Color[N * N];
            float sx = (float)w / N, sy = (float)h / N;

            for (int y = 0; y < N; y++)
            {
                int y0 = Mathf.FloorToInt(y * sy);
                int y1 = Mathf.Min(h, Mathf.Max(y0 + 1, Mathf.FloorToInt((y + 1) * sy)));

                for (int x = 0; x < N; x++)
                {
                    // The mirror: column x of the cursor reads the far side of the hand.
                    int mx = N - 1 - x;
                    int x0 = Mathf.FloorToInt(mx * sx);
                    int x1 = Mathf.Min(w, Mathf.Max(x0 + 1, Mathf.FloorToInt((mx + 1) * sx)));

                    float aSum = 0f, lumSum = 0f;
                    int n = 0;

                    for (int j = y0; j < y1; j++)
                    {
                        for (int i = x0; i < x1; i++)
                        {
                            var c = srcPx[j * w + i];
                            // Weighting the shade by coverage keeps the transparent
                            // margin from dragging the outline towards black.
                            aSum += c.a;
                            lumSum += c.grayscale * c.a;
                            n++;
                        }
                    }

                    float a = aSum / n;
                    float lum = aSum > 0f ? lumSum / aSum : 0f;
                    float v = Floor + Range * lum;
                    px[y * N + x] = new Color(v, v, v, a);
                }
            }

            _hotspot = Tip(px);
            _px = px;

            _tex = new Texture2D(N, N, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _tex.SetPixels(px);
            _tex.Apply();
        }

        /// <summary>Core textures come out of the bundles unreadable, so the pixels
        /// have to be fetched back off the GPU.</summary>
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
            Object.Destroy(copy);
            return px;
        }

        /// <summary>Where the pointer actually points: the solid pixel nearest the
        /// top-left corner, which is the fingertip here and is exactly where the
        /// vanilla arrow puts its own hotspot.</summary>
        static Vector2 Tip(Color[] px)
        {
            var tip = Vector2.zero;
            int best = int.MaxValue;

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    if (px[y * N + x].a < 0.5f) continue;
                    // Pixels run bottom-up, hotspots run top-down.
                    int top = N - 1 - y;
                    if (x + top >= best) continue;
                    best = x + top;
                    tip = new Vector2(x, top);
                }
            }

            return tip;
        }
    }

    /// <summary>The game only reaches for the cursor when prefs are applied, and it
    /// gets ours either way - the arrow is not coming back.</summary>
    [HarmonyPatch(typeof(CustomCursor), nameof(CustomCursor.Activate))]
    public static class Patch_CustomCursor_Activate
    {
        static bool Prefix()
        {
            DeadCursor.Apply();
            return false;
        }
    }

    [HarmonyPatch(typeof(CustomCursor), nameof(CustomCursor.Deactivate))]
    public static class Patch_CustomCursor_Deactivate
    {
        static bool Prefix()
        {
            DeadCursor.Apply();
            return false;
        }
    }
}
