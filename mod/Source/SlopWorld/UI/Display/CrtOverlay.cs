using UnityEngine;

namespace SlopWorld
{
    // Owns the Unity presentation of the profile's CRT overlay, independently of pane caches.
    // Two alpha textures shade the finished GUI without capturing or distorting it.
    internal sealed class CrtOverlay : MonoBehaviour
    {
        const int ScanlinePeriod = 3;
        const float ScanlineOpacity = 0.11f;
        const float CornerOpacity = 0.17f;
        const float DitherRange = 2f / 255f;
        const float ResizeSettleSeconds = 0.2f;

        Texture2D _scanlines;
        Texture2D _vignette;
        float _scanlineScale;
        int _pendingWidth;
        int _pendingHeight;
        float _vignetteReadyAt;

        internal static void Install()
        {
            var owner = new GameObject("SlopWorld CRT overlay");
            DontDestroyOnLoad(owner);
            owner.AddComponent<CrtOverlay>();
        }

        void OnGUI()
        {
            // Depth belongs to this separate GUI behaviour. Root's windows, tooltips and
            // screensaver must all remain below it; drawing allocates no input controls.
            GUI.depth = -10000;
            if (!Settings.CrtOverlay)
            {
                ReleaseTextures();
                return;
            }
            if (Event.current.type != EventType.Repaint || Screen.width <= 0 || Screen.height <= 0)
                return;

            float scale = UiScale.Current;
            if (
                _scanlines != null
                && (_scanlines.height != Screen.height || _scanlineScale != scale)
            )
            {
                Destroy(_scanlines);
                _scanlines = null;
            }
            if (_scanlines == null)
            {
                _scanlines = CreateScanlines(Screen.height, scale);
                _scanlineScale = scale;
            }
            RefreshVignette(Screen.width, Screen.height);

            var matrix = GUI.matrix;
            var color = GUI.color;
            bool enabled = GUI.enabled;
            try
            {
                // Draw in physical pixels; the strip already incorporates the game's UI
                // scale. Each texel maps to one screen row without texture repetition.
                GUI.matrix = Matrix4x4.identity;
                GUI.color = Color.white;
                GUI.enabled = true;
                var screen = new Rect(0f, 0f, Screen.width, Screen.height);
                GUI.DrawTexture(screen, _scanlines, ScaleMode.StretchToFill, true);
                if (_vignette != null)
                    GUI.DrawTexture(screen, _vignette, ScaleMode.StretchToFill, true);
            }
            finally
            {
                GUI.matrix = matrix;
                GUI.color = color;
                GUI.enabled = enabled;
            }
        }

        void RefreshVignette(int width, int height)
        {
            if (_pendingWidth != width || _pendingHeight != height)
            {
                // Enable immediately, but wait for resizing to settle before doing another
                // full-screen CPU bake and upload. Real time also advances while paused.
                _vignetteReadyAt =
                    Time.realtimeSinceStartup + (_pendingWidth == 0 ? 0f : ResizeSettleSeconds);
                _pendingWidth = width;
                _pendingHeight = height;
                if (_vignette != null)
                    Destroy(_vignette);
                // Hide while resizing: stretching the old dither makes its noise visible.
                _vignette = null;
            }
            if (_vignette == null && Time.realtimeSinceStartup >= _vignetteReadyAt)
                _vignette = CreateVignette(width, height);
        }

        static Texture2D CreateScanlines(int height, float scale)
        {
            var texture = CreateTexture("SlopWorld scanlines", 1, height);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[height];
            for (int y = 0; y < height; y++)
            {
                // Texture rows count from the bottom. Integrate the dark part of each
                // screen row in logical coordinates, anchored at the top. Fractional UI
                // scales get partial edge rows instead of uneven rounded line spacing.
                int row = height - 1 - y;
                float start = row / scale;
                float end = (row + 1f) / scale;
                float coverage = (DarkRowsBefore(end) - DarkRowsBefore(start)) * scale;
                pixels[y] = new Color(0f, 0f, 0f, ScanlineOpacity * Mathf.Clamp01(coverage));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static float DarkRowsBefore(float position)
        {
            float periods = Mathf.Floor(position / ScanlinePeriod);
            return periods + Mathf.Clamp01(position - periods * ScanlinePeriod);
        }

        static Texture2D CreateVignette(int width, int height)
        {
            // Dither at the final pixel resolution. Scaling a small noisy texture would
            // filter away the noise or turn it into visible patches.
            var texture = CreateTexture("SlopWorld vignette", width, height);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float nx = 2f * (x + 0.5f) / width - 1f;
                float ny = 2f * (y + 0.5f) / height - 1f;
                // A continuous quartic falloff spreads the shading across the whole image,
                // with a flat, clear centre and no threshold outlining an inner ellipse.
                float radiusSquared = (nx * nx + ny * ny) * 0.5f;
                float opacity = CornerOpacity * radiusSquared * radiusSquared;
                // Alpha-only noise vanishes over dark panels. Encode a tiny neutral RGB
                // contribution too, so the blended result varies by up to two display
                // levels even on black. The added attenuation keeps the mean shift within
                // roughly one display level. Quantize alpha first to account for RGBA32.
                byte alpha = (byte)Mathf.RoundToInt((opacity + DitherRange) * 255f);
                float lift = PixelNoise(x, y) * DitherRange;
                byte gray = (byte)Mathf.RoundToInt(lift * 255f * 255f / alpha);
                pixels[y * width + x] = new Color32(gray, gray, gray, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // A stable coordinate hash avoids both repeating grids and frame-to-frame shimmer.
        static float PixelNoise(int x, int y)
        {
            unchecked
            {
                uint bits = (uint)x * 0x9e3779b9u ^ (uint)y * 0x85ebca6bu;
                bits ^= bits >> 16;
                bits *= 0x7feb352du;
                bits ^= bits >> 15;
                bits *= 0x846ca68bu;
                bits ^= bits >> 16;
                return (bits & 0xffffffu) / 16777216f;
            }
        }

        static Texture2D CreateTexture(string name, int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                // The overlay outlives maps and menus. OnDestroy and the live toggle own
                // cleanup rather than scene asset unloading.
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };

        void OnDestroy() => ReleaseTextures();

        void ReleaseTextures()
        {
            if (_scanlines != null)
                Destroy(_scanlines);
            if (_vignette != null)
                Destroy(_vignette);
            _scanlines = null;
            _vignette = null;
            _pendingWidth = 0;
            _pendingHeight = 0;
            _vignetteReadyAt = 0f;
        }
    }
}
