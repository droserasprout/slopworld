using System;

namespace SlopWorld
{
    // X11 valuators accumulate motion between samples, not one physical wheel packet.
    // A claimed sample may cover several Unity legacy packets in this frame or the next.
    // There is no native pointer/modifier stamp to correlate with an IMGUI event.
    internal sealed class NativeScrollCoverage
    {
        int _frame = -1;
        float _dx, _dy;

        public void Clear() => _frame = -1;

        public void Record(int frame, float dx, float dy)
        {
            _frame = frame;
            _dx = dx;
            _dy = dy;
        }

        public bool Covers(int frame, float dx, float dy) =>
            _frame >= 0 && frame >= _frame && frame - _frame <= 1 &&
            (Math.Abs(dx) > 0.0001f || Math.Abs(dy) > 0.0001f) &&
            AxisCovered(dx, _dx) && AxisCovered(dy, _dy);

        // A vertical legacy packet need not report the touchpad's tiny horizontal drift.
        // Actual logical movement on an uncovered/reversed axis must still fall back.
        static bool AxisCovered(float logical, float native) =>
            Math.Abs(logical) <= 0.0001f ||
            (logical > 0f && native > 0.0001f) || (logical < 0f && native < -0.0001f);
    }
}
