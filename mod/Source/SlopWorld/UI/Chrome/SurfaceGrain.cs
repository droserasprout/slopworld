using UnityEngine;

namespace SlopWorld
{
    // Owns the shared static grain tile. Callers draw it over faces, before borders and content.
    internal static class SurfaceGrain
    {
        const int TileSize = 128;
        static Texture2D _texture;

        internal static void Draw(Rect rect, float opacity, Vector2 screenScale)
        {
            opacity *= VisualEffectsMode.GrainOpacity(Settings.VisualEffects);
            if (opacity <= 0f || rect.width <= 0f || rect.height <= 0f)
                return;
            if (_texture == null)
                _texture = CreateTexture();

            // Keep grains one physical pixel wide, including at fractional UI scales.
            // Local UVs let the texture travel with its widget rather than shimmer on scroll.
            var uv = new Rect(
                0f,
                0f,
                rect.width * screenScale.x / TileSize,
                rect.height * screenScale.y / TileSize
            );
            var color = GUI.color;
            try
            {
                GUI.color = new Color(1f, 1f, 1f, opacity);
                GUI.DrawTextureWithTexCoords(rect, _texture, uv);
            }
            finally
            {
                GUI.color = color;
            }
        }

        static Texture2D CreateTexture()
        {
            var texture = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, false)
            {
                name = "SlopWorld surface grain",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            var pixels = new Color32[TileSize * TileSize];
            // Private deterministic PRNG: constructing chrome must not advance game randomness.
            uint state = 0x6d2b79f5u;
            for (int i = 0; i < pixels.Length; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                byte shade = (state & 1u) == 0 ? (byte)0 : (byte)255;
                byte alpha = (byte)((state >> 8) & 255u);
                pixels[i] = new Color32(shade, shade, shade, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            // Unity may unload the tile between maps; Draw recreates it on demand.
            return texture;
        }
    }
}
