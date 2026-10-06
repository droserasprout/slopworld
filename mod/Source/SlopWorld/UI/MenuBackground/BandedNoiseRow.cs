using System;
using System.Runtime.CompilerServices;

namespace SlopWorld
{
    // Shared bilinear sampling for fire and haze. Construct once per output row to
    // keep vertical interpolation outside the pixel loop; callers own scales and bands.
    internal readonly struct BandedNoiseRow
    {
        readonly float[] _noise;
        readonly int _width, _row0, _row1;
        readonly float _fractionY;

        public BandedNoiseRow(float[] noise, int width, int height, float y)
        {
            _noise = noise;
            _width = width;
            int y0 = (int)y, y1 = Math.Min(height - 1, y0 + 1);
            _fractionY = y - y0;
            _row0 = y0 * width;
            _row1 = y1 * width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Sample(float x, float low, float band)
        {
            int x0 = (int)x, x1 = Math.Min(_width - 1, x0 + 1);
            float fractionX = x - x0;
            float a = _noise[_row0 + x0], b = _noise[_row1 + x0];
            a += (_noise[_row0 + x1] - a) * fractionX;
            b += (_noise[_row1 + x1] - b) * fractionX;
            float n = a + (b - a) * _fractionY;
            float t = (n - low) * band;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * t * (3f - 2f * t);
        }
    }
}
