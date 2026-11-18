using System;

namespace SlopWorld
{
    internal sealed class CreditsScroll
    {
        const float Speed = 10f;
        const float PauseSeconds = 3f;
        double? _lastRepaint;
        float _pause;
        bool _down = true;

        public void Reset()
        {
            _lastRepaint = null;
            _pause = 0f;
            _down = true;
        }

        public float Advance(double frameTime, float position, float max)
        {
            // Repaints may skip Unity update frames. Cap catch-up when returning to a hidden tab.
            float elapsed = (float)Math.Min(1d, frameTime - (_lastRepaint ?? frameTime));
            _lastRepaint = frameTime;
            if (elapsed <= 0f || max <= 0f)
                return position;

            float paused = Math.Min(elapsed, _pause);
            _pause -= paused;
            float distance = (elapsed - paused) * Speed;
            if (distance == 0f)
                return position;
            float next = _down ? position + distance : position - distance;
            if (_down ? next < max : next > 0f)
                return next;

            float edge = _down ? max : 0f;
            _down = !_down;
            _pause = PauseSeconds - Math.Abs(next - edge) / Speed;
            return edge;
        }
    }
}
