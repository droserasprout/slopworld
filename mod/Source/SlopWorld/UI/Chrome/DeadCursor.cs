using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The pointer, replaced with a small game-asset designator: some assets are mirrored
    // to reach up-left the way the vanilla arrow points. It can be drained to a lifeless
    // grey for legibility over the map. The hand still waggles when a cat is patted.
    [StaticConstructorOnStartup]
    public static class DeadCursor
    {
        public sealed class Choice
        {
            public readonly string Key;
            public readonly string Label;
            public readonly string TexturePath;
            public readonly bool Reversed;

            public Choice(string key, string label, string texturePath, bool reversed)
            {
                Key = key;
                Label = label;
                TexturePath = texturePath;
                Reversed = reversed;
            }
        }

        // These are the designs that read as pointers rather than as tiny UI buttons. The
        // reverse flag preserves the useful orientation of each source icon: the hand and
        // fists point the same way as the old cursor, while the tools stay as drawn.
        static readonly Choice[] _choices =
        {
            new Choice("tame", "Tame", "UI/Designators/Tame", true),
            new Choice("attack", "Attack", "UI/Commands/Attack", true),
            new Choice("extract_skull", "Extract skull", "UI/Designators/ExtractSkull", false),
            new Choice("harvest_wood", "Harvest wood", "UI/Designators/HarvestWood", true),
            new Choice("mine", "Mine", "UI/Designators/Mine", false),
            new Choice("slaughter", "Slaughter", "UI/Designators/Slaughter", false),
            new Choice("attack_melee", "Attack melee", "UI/Commands/AttackMelee", true),
            new Choice("take_drug", "Take drug", "UI/Commands/TakeDrug", true),
        };

        public static Choice[] Choices => _choices;

        public static string CurrentKey =>
            ChoiceFor(Settings.Cursor) != null ? Settings.Cursor : _choices[0].Key;

        // The vanilla arrow is 32, which is too polite to notice; X11 has no trouble with
        // a hardware cursor this size.
        const int N = 48;
        // The outline keeps its weight, the skin flattens out: grey, not pale.
        const float Floor = 0.20f;
        const float Range = 0.68f;

        // A turned square is wider than the square: at the swing below it needs
        // N*(cos+sin), a shade over 62, so the spun frames get a canvas of their own.
        const int M = 64;

        // Counter-clockwise and back rather than side to side: a hand that crosses
        // straight reads as a metronome.
        const float SpinDegrees = 22f;
        const int SpinWaggles = 2;
        const float SpinSeconds = 0.5f;
        // Twelve is smooth at this size, built the first time a cat is patted and kept.
        const int SpinSteps = 12;

        // The click is the same swing with most of it taken away: one dip and back, a
        // quarter of the pat's angle, because a click is a glance rather than a fuss.
        const int ClickStep = 3;
        const float ClickSeconds = 0.12f;

        static Texture2D _tex;
        static Vector2 _hotspot;
        static string _builtKey;
        static bool _builtGrayscale;
        // The resting pixels, kept because every spun frame is cut from them.
        static Color[] _px;

        static readonly Texture2D[] _spun = new Texture2D[SpinSteps + 1];
        static readonly Vector2[] _spunHot = new Vector2[SpinSteps + 1];

        // Which frame the pointer is actually wearing, so a still hand costs no calls.
        static float _spinUntil = -1f;
        static int _shown;

        // The two never run at once: a click during a pat is dropped, the pat being the
        // bigger answer, and a pat cuts a click short.
        static float _clickUntil = -1f;

        static DeadCursor()
        {
            Apply();
        }

        public static Choice ChoiceFor(string key)
        {
            foreach (var choice in _choices)
                if (choice.Key == key) return choice;
            return null;
        }

        public static string LabelFor(string key)
        {
            var choice = ChoiceFor(key);
            return choice != null ? choice.Label : _choices[0].Label;
        }

        // The source image is used in the picker. The cursor builder below makes its own
        // readable, scaled copy because core-bundle textures cannot be read back directly.
        public static Texture2D Preview(Choice choice) =>
            choice == null ? null : ContentFinder<Texture2D>.Get(choice.TexturePath, false);

        public static void Choose(string key)
        {
            if (ChoiceFor(key) == null) key = _choices[0].Key;
            Settings.S.cursor = key;
            Settings.S.Write();
            Apply();
        }

        // Cheap enough to call on every prefs change. Vanilla also calls the patched
        // CustomCursor methods while changing cursor modes, so preserve an animation that
        // is already running instead of briefly putting the resting cursor back on screen.
        public static void Apply()
        {
            var choice = ChoiceFor(Settings.Cursor) ?? _choices[0];
            if (_tex == null || _builtKey != choice.Key ||
                _builtGrayscale != Settings.CursorGrayscale)
                Build(choice);
            _shown = -1;
            Show(AnimationStep(Time.realtimeSinceStartup));
        }

        // A pat while it is already going is one that has been answered, so it is dropped
        // rather than snapping the swing back to straight.
        public static void Pat()
        {
            if (_spinUntil >= 0f) return;
            if (_tex == null) Apply();
            if (_tex == null) return;
            _clickUntil = -1f; // the pat speaks over the click
            _spinUntil = Time.realtimeSinceStartup + SpinSeconds;
        }

        // Answers a mouse button going down anywhere, which is honest: the pointer cannot
        // tell a click on dead ground from one on a portrait.
        public static void Click()
        {
            if (_spinUntil >= 0f) return; // a pat is the bigger answer
            if (_tex == null) Apply();
            if (_tex == null) return;
            _clickUntil = Time.realtimeSinceStartup + ClickSeconds;
        }

        // A hardware cursor is one still image, so an animation is a texture per frame.
        public static void Tick()
        {
            Show(AnimationStep(Time.realtimeSinceStartup));
        }

        static int AnimationStep(float now)
        {
            if (_spinUntil >= 0f)
            {
                float left = _spinUntil - now;
                if (left <= 0f)
                {
                    _spinUntil = -1f;
                    return 0;
                }

                // Half a sine per waggle, so the hand goes out and back without ever crossing
                // to the other side of straight.
                float t = 1f - left / SpinSeconds;
                float f = Mathf.Abs(Mathf.Sin(t * SpinWaggles * Mathf.PI));
                return Mathf.RoundToInt(f * SpinSteps);
            }

            if (_clickUntil >= 0f)
            {
                if (now >= _clickUntil)
                {
                    _clickUntil = -1f;
                    return 0;
                }
                return ClickStep;
            }

            return 0;
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

        // The hotspot is carried along, so the fingertip stays under the mouse and it is
        // the hand that swings, not the pointer. Sampled nearest-neighbour: blending a
        // black-outlined icon into its own transparent margin greys the outline out.
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

        static void Build(Choice choice)
        {
            ClearBuilt();
            var src = ContentFinder<Texture2D>.Get(choice.TexturePath, false);
            if (src == null)
            {
                Log.Error("[SlopWorld] cursor asset missing: " + choice.TexturePath);
                _builtKey = choice.Key;
                return;
            }

            int w = src.width, h = src.height;
            var srcPx = TextureReadback.ReadBack(src);

            var px = new Color[N * N];
            float sx = (float)w / N, sy = (float)h / N;

            for (int y = 0; y < N; y++)
            {
                int y0 = Mathf.FloorToInt(y * sy);
                int y1 = Mathf.Min(h, Mathf.Max(y0 + 1, Mathf.FloorToInt((y + 1) * sy)));

                for (int x = 0; x < N; x++)
                {
                    int mx = choice.Reversed ? N - 1 - x : x;
                    int x0 = Mathf.FloorToInt(mx * sx);
                    int x1 = Mathf.Min(w, Mathf.Max(x0 + 1, Mathf.FloorToInt((mx + 1) * sx)));

                    float aSum = 0f, lumSum = 0f;
                    float rSum = 0f, gSum = 0f, bSum = 0f;
                    int n = 0;

                    for (int j = y0; j < y1; j++)
                    {
                        for (int i = x0; i < x1; i++)
                        {
                            var c = srcPx[j * w + i];
                            // Weighting the shade by coverage keeps the transparent margin from dragging the
                            // outline towards black.
                            aSum += c.a;
                            lumSum += c.grayscale * c.a;
                            rSum += c.r * c.a;
                            gSum += c.g * c.a;
                            bSum += c.b * c.a;
                            n++;
                        }
                    }

                    float a = aSum / n;
                    float lum = aSum > 0f ? lumSum / aSum : 0f;
                    if (Settings.CursorGrayscale)
                    {
                        float v = Floor + Range * lum;
                        px[y * N + x] = new Color(v, v, v, a);
                    }
                    else
                    {
                        px[y * N + x] = new Color(
                            aSum > 0f ? rSum / aSum : 1f,
                            aSum > 0f ? gSum / aSum : 1f,
                            aSum > 0f ? bSum / aSum : 1f,
                            a);
                    }
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
            _builtKey = choice.Key;
            _builtGrayscale = Settings.CursorGrayscale;
        }

        static void ClearBuilt()
        {
            if (_tex != null) Object.Destroy(_tex);
            _tex = null;
            _px = null;
            _builtKey = null;
            _builtGrayscale = false;
            for (int i = 1; i < _spun.Length; i++)
            {
                if (_spun[i] != null) Object.Destroy(_spun[i]);
                _spun[i] = null;
                _spunHot[i] = Vector2.zero;
            }
            _spinUntil = -1f;
            _clickUntil = -1f;
            _shown = 0;
        }

        // The solid pixel nearest the top-left corner, which is the fingertip and exactly
        // where the vanilla arrow puts its own hotspot.
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

    // The game only reaches for the cursor when prefs are applied, and it gets ours
    // either way.
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
