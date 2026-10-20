using UnityEngine;

namespace SlopWorld
{
    // Owns CPU vignette pixels. CrtOverlay owns texture uploads, drawing and cache lifetime.
    internal static class CrtVignettePixels
    {
        const float DitherRange = 2f / 255f;

        internal static Color32[] Build(int width, int height, float cornerOpacity)
        {
            var pixels = new Color32[width * height];
            // Horizontal coordinates repeat on every row. Preserve the original float
            // operations so precomputing their squares cannot move quantization boundaries.
            var horizontalSquared = new float[width];
            for (int x = 0; x < width; x++)
            {
                float nx = 2f * (x + 0.5f) / width - 1f;
                horizontalSquared[x] = nx * nx;
            }
            for (int y = 0; y < height; y++)
            {
                float ny = 2f * (y + 0.5f) / height - 1f;
                float verticalSquared = ny * ny;
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    // A continuous quartic falloff spreads the shading across the whole image,
                    // with a flat, clear centre and no threshold outlining an inner ellipse.
                    float radiusSquared = (horizontalSquared[x] + verticalSquared) * 0.5f;
                    float opacity = cornerOpacity * radiusSquared * radiusSquared;
                    // Alpha-only noise vanishes over dark panels. Encode a tiny neutral RGB
                    // contribution too, so the blended result varies by up to two display
                    // levels even on black. The added attenuation keeps the mean shift within
                    // roughly one display level. Quantize alpha first to account for RGBA32.
                    byte alpha = (byte)Mathf.RoundToInt((opacity + DitherRange) * 255f);
                    float lift = PixelNoise(x, y) * DitherRange;
                    byte gray = (byte)Mathf.RoundToInt(lift * 255f * 255f / alpha);
                    pixels[row + x] = new Color32(gray, gray, gray, alpha);
                }
            }
            return pixels;
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
    }
}
