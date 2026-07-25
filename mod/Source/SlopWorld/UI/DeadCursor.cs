using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The pointer, replaced with the Tame designator's hand: mirrored so it
    /// reaches up-left the way the vanilla arrow points, and drained to a
    /// lifeless grey, because there is nothing left down there to tame.
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

        static Texture2D _tex;
        static Vector2 _hotspot;

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
