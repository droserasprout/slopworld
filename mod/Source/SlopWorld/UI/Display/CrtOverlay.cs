using UnityEngine;

namespace SlopWorld
{
    // Owns the Unity presentation of the profile's CRT overlay, independently of pane caches.
    // One combined texture shades the finished GUI and stays visible during resize bakes.
    internal sealed class CrtOverlay : MonoBehaviour
    {
        const float ResizeSettleSeconds = 0.2f;

        string _mode;
        Texture2D _scanlines;
        Texture2D _vignette;
        float _scanlineScale;
        float _pendingScale;
        int _pendingWidth;
        int _pendingHeight;
        float _vignetteReadyAt;
        bool _vignetteDirty;

        internal static void Install()
        {
            var owner = new GameObject("SlopWorld CRT overlay");
            DontDestroyOnLoad(owner);
            owner.AddComponent<CrtOverlay>();
        }

        void Update()
        {
            string mode = Settings.VisualEffects;
            if (_mode != mode)
            {
                ReleaseTextures();
                _mode = mode;
            }
            if (mode == VisualEffectsMode.Off)
                return;

            // Prepare textures outside IMGUI repaint; sample dimensions once.
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
            RefreshVignette(width, height, scale);
        }

        void OnGUI()
        {
            // This separate behaviour stays above windows and tooltips without controls.
            GUI.depth = -10000;
            if (Event.current.type != EventType.Repaint || Settings.VisualEffects != _mode)
                return;
            if (_mode == VisualEffectsMode.Off || _scanlines == null)
                return;
            int width = Screen.width;
            int height = Screen.height;
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
                GUI.DrawTexture(
                    screen,
                    _vignette != null ? _vignette : _scanlines,
                    ScaleMode.StretchToFill,
                    true
                );
            }
            finally
            {
                GUI.matrix = matrix;
                GUI.color = color;
                GUI.enabled = enabled;
            }
        }

        void RefreshVignette(int width, int height, float scale)
        {
            if (_pendingWidth != width || _pendingHeight != height || _pendingScale != scale)
            {
                // Keep the previous texture visible while resizing settles before another
                // full-screen CPU bake and upload. Real time also advances while paused.
                _vignetteReadyAt =
                    Time.realtimeSinceStartup + (_pendingWidth == 0 ? 0f : ResizeSettleSeconds);
                _pendingWidth = width;
                _pendingHeight = height;
                _pendingScale = scale;
                _vignetteDirty = true;
            }
            if (_vignetteDirty && Time.realtimeSinceStartup >= _vignetteReadyAt)
            {
                // A brief stretch of the old dither is preferable to dropping the shading.
                // Replace only after the new texture is ready.
                var replacement = CreateVignette(width, height, scale, _mode);
                if (_vignette != null)
                    Destroy(_vignette);
                _vignette = replacement;
                _vignetteDirty = false;
            }
        }

        static Texture2D CreateScanlines(int height, float scale, float opacity)
        {
            var texture = CreateTexture("SlopWorld scanlines", 1, height);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[height];
            for (int y = 0; y < height; y++)
            {
                // Texture rows count from the bottom; scanlines anchor at the top.
                pixels[y] = new Color32(
                    0,
                    0,
                    0,
                    CrtVignettePixels.ScanlineAlpha(height - 1 - y, scale, opacity)
                );
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D CreateVignette(int width, int height, float scale, string mode)
        {
            // Dither at the final pixel resolution. Scaling a small noisy texture would
            // filter away the noise or turn it into visible patches.
            var texture = CreateTexture("SlopWorld vignette", width, height);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = CrtVignettePixels.Build(
                width,
                height,
                VisualEffectsMode.CornerOpacity(mode),
                VisualEffectsMode.ScanlineOpacity(mode),
                scale
            );
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
            _vignetteDirty = false;
        }
    }
}
