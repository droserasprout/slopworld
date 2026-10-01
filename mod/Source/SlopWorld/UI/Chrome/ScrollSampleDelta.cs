using System;

namespace SlopWorld
{
    // Native valuators are cumulative. Late snapshots must not replay logical fallback
    // movement, or movement collected while no viewport was accepting input.
    internal sealed class ScrollSampleDelta
    {
        const double MaxGap = 0.25;
        bool _baseline;
        int _generation;
        double _x, _y, _time, _discardBefore;

        public void Discard(double now) { _baseline = false; _discardBefore = now; }

        public bool Read(double x, double y, double sampled, double now, out double dx, out double dy)
            => Read(x, y, sampled, now, 0, out dx, out dy);

        public bool Read(double x, double y, double sampled, double now, int generation,
                         out double dx, out double dy)
        {
            if (_generation != generation) _baseline = false;
            _generation = generation;
            dx = dy = 0;
            if (sampled <= _discardBefore || now - sampled > MaxGap || !Finite(x) || !Finite(y))
            {
                _baseline = false;
                return false;
            }
            bool contiguous = _baseline && sampled >= _time && sampled - _time <= MaxGap;
            double changeX = x - _x, changeY = y - _y;
            _x = x; _y = y; _time = sampled; _baseline = true;
            if (!contiguous || Math.Abs(changeX) > 100 || Math.Abs(changeY) > 100) return false;
            dx = changeX; dy = changeY;
            return true;
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
