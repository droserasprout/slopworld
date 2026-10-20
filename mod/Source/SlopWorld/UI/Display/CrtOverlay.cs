using UnityEngine;

namespace SlopWorld
{
    // Owns the Unity presentation of the profile's CRT overlay, independently of pane caches.
    // Two alpha textures shade the finished GUI without capturing or distorting it.
    internal sealed class CrtOverlay : MonoBehaviour
    {
        const int ScanlinePeriod = 3;
        const float ResizeSettleSeconds = 0.2f;

        string _mode;
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
            string mode = Settings.VisualEffects;
            if (_mode != mode)
            {
                ReleaseTextures();
                _mode = mode;
            }
            if (mode == VisualEffectsMode.Off)
                return;
            if (Event.current.type != EventType.Repaint)
                return;

            // Screen dimensions cross into Unity; sample them once for this repaint.
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0)
                return;
            float scale = UiScale.Current;
            if (_scanlines != null && (_scanlines.height != height || _scanlineScale != scale))
            {
                Destroy(_scanlines);
                _scanlines = null;
            }
            if (_scanlines == null)
            {
                _scanlines = CreateScanlines(
                    height,
                    scale,
                    VisualEffectsMode.ScanlineOpacity(mode)
                );
                _scanlineScale = scale;
            }
            RefreshVignette(width, height);

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
                var screen = new Rect(0f, 0f, width, height);
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
                _vignette = CreateVignette(width, height, VisualEffectsMode.CornerOpacity(_mode));
        }

        static Texture2D CreateScanlines(int height, float scale, float opacity)
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
                pixels[y] = new Color(0f, 0f, 0f, opacity * Mathf.Clamp01(coverage));
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

        static Texture2D CreateVignette(int width, int height, float cornerOpacity)
        {
            // Dither at the final pixel resolution. Scaling a small noisy texture would
            // filter away the noise or turn it into visible patches.
            var texture = CreateTexture("SlopWorld vignette", width, height);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = CrtVignettePixels.Build(width, height, cornerOpacity);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D CreateTexture(string name, int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                // The overlay outlives maps and menus. OnDestroy and the live setting own
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
