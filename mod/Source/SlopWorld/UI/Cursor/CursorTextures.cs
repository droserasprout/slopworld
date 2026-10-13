using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Builds, caches and destroys cursor textures; displays the selected frame.
    internal sealed class CursorTextures
    {
        const int CursorSize = 48;

        // Keep grayscale pixels dark enough to preserve the outline.
        const float GrayscaleFloor = 0.20f;
        const float GrayscaleRange = 0.68f;

        // Extra padding prevents clipping at the maximum rotation.
        const int RotatedSize = 64;

        const float MaxRotationDegrees = 22f;

        Texture2D _tex;
        Vector2 _hotspot;
        string _builtKey;
        bool _builtGrayscale;

        // Source pixels for rotated frames.
        Color[] _px;

        readonly Texture2D[] _spun = new Texture2D[CursorAnimation.Steps + 1];
        readonly Vector2[] _spunHot = new Vector2[CursorAnimation.Steps + 1];

        // Skip cursor updates when the frame is unchanged.
        int _shown = -1;

        public bool Ready => _tex != null;

        public bool Matches(GameCursor.Choice choice, bool grayscale) =>
            Ready && _builtKey == choice.Key && _builtGrayscale == grayscale;

        public void InvalidateShown() => _shown = -1;

        public void Show(int step)
        {
            if (step == _shown)
                return;
            _shown = step;

            if (step <= 0)
            {
                Cursor.SetCursor(_tex, _hotspot, CursorMode.Auto);
                return;
            }

            if (_spun[step] == null)
                _spun[step] = BuildRotatedFrame(
                    step * MaxRotationDegrees / CursorAnimation.Steps,
                    out _spunHot[step]
                );

            Cursor.SetCursor(_spun[step], _spunHot[step], CursorMode.Auto);
        }

        // Rotate the hotspot with the icon to keep the tip under the mouse.
        // Nearest-neighbor sampling preserves the outline.
        Texture2D BuildRotatedFrame(float deg, out Vector2 hotspot)
        {
            float rad = deg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad),
                sin = Mathf.Sin(rad);
            // Pixels run bottom-up; hotspots run top-down.
            float c = (CursorSize - 1) * 0.5f;
            int o = (RotatedSize - CursorSize) / 2;

            var px = new Color[RotatedSize * RotatedSize];

            for (int y = 0; y < RotatedSize; y++)
            {
                for (int x = 0; x < RotatedSize; x++)
                {
                    // Inverse rotation maps each output pixel to its source.
                    float dx = x - o - c,
                        dy = y - o - c;
                    int sxp = Mathf.RoundToInt(c + dx * cos + dy * sin);
                    int syp = Mathf.RoundToInt(c - dx * sin + dy * cos);
                    if (sxp < 0 || sxp >= CursorSize || syp < 0 || syp >= CursorSize)
                        continue;
                    px[y * RotatedSize + x] = _px[syp * CursorSize + sxp];
                }
            }

            float hx = _hotspot.x - c,
                hy = (CursorSize - 1 - _hotspot.y) - c;
            hotspot = new Vector2(
                o + c + hx * cos - hy * sin,
                (RotatedSize - 1) - (o + c + hx * sin + hy * cos)
            );

            return CreateTexture(RotatedSize, px);
        }

        public void Build(GameCursor.Choice choice, bool grayscale)
        {
            ClearBuilt();
            var src = ContentFinder<Texture2D>.Get(choice.TexturePath, false);
            if (src == null)
            {
                Log.Error("[SlopWorld] cursor asset missing: " + choice.TexturePath);
                _builtKey = choice.Key;
                return;
            }

            int w = src.width,
                h = src.height;
            var srcPx = TextureReadback.ReadBack(src);

            var px = PreparePixels(srcPx, w, h, choice.Reversed, grayscale);

            _hotspot = FindHotspot(px);
            _px = px;

            _tex = CreateTexture(CursorSize, px);
            _builtKey = choice.Key;
            _builtGrayscale = grayscale;
        }

        static Color[] PreparePixels(Color[] srcPx, int w, int h, bool reversed, bool grayscale)
        {
            var px = new Color[CursorSize * CursorSize];
            float sx = (float)w / CursorSize,
                sy = (float)h / CursorSize;

            for (int y = 0; y < CursorSize; y++)
            {
                int y0 = Mathf.FloorToInt(y * sy);
                int y1 = Mathf.Min(h, Mathf.Max(y0 + 1, Mathf.FloorToInt((y + 1) * sy)));

                for (int x = 0; x < CursorSize; x++)
                {
                    int mx = reversed ? CursorSize - 1 - x : x;
                    int x0 = Mathf.FloorToInt(mx * sx);
                    int x1 = Mathf.Min(w, Mathf.Max(x0 + 1, Mathf.FloorToInt((mx + 1) * sx)));

                    float aSum = 0f,
                        lumSum = 0f;
                    float rSum = 0f,
                        gSum = 0f,
                        bSum = 0f;
                    int n = 0;

                    for (int j = y0; j < y1; j++)
                    {
                        for (int i = x0; i < x1; i++)
                        {
                            var c = srcPx[j * w + i];
                            // Alpha weighting prevents transparent pixels from darkening the color.
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
                    if (grayscale)
                    {
                        float v = GrayscaleFloor + GrayscaleRange * lum;
                        px[y * CursorSize + x] = new Color(v, v, v, a);
                    }
                    else
                    {
                        px[y * CursorSize + x] = new Color(
                            aSum > 0f ? rSum / aSum : 1f,
                            aSum > 0f ? gSum / aSum : 1f,
                            aSum > 0f ? bSum / aSum : 1f,
                            a
                        );
                    }
                }
            }
            return px;
        }

        void ClearBuilt()
        {
            if (_tex != null)
                Object.Destroy(_tex);
            _tex = null;
            _px = null;
            _builtKey = null;
            _builtGrayscale = false;
            for (int i = 1; i < _spun.Length; i++)
            {
                if (_spun[i] != null)
                    Object.Destroy(_spun[i]);
                _spun[i] = null;
                _spunHot[i] = Vector2.zero;
            }
            _shown = -1;
        }

        // Use the solid pixel nearest the top-left as the hotspot.
        static Vector2 FindHotspot(Color[] px)
        {
            var tip = Vector2.zero;
            int best = int.MaxValue;

            for (int y = 0; y < CursorSize; y++)
            {
                for (int x = 0; x < CursorSize; x++)
                {
                    if (px[y * CursorSize + x].a < 0.5f)
                        continue;
                    // Pixels run bottom-up, hotspots run top-down.
                    int top = CursorSize - 1 - y;
                    if (x + top >= best)
                        continue;
                    best = x + top;
                    tip = new Vector2(x, top);
                }
            }

            return tip;
        }

        static Texture2D CreateTexture(int size, Color[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
