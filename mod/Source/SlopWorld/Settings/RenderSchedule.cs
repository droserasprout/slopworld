using System;

namespace SlopWorld
{
    // Rendering has its own deadline. Service ticks bound idle network/update latency;
    // window events can wake the main thread earlier, regardless of the render limit.
    internal sealed class RenderSchedule
    {
        internal const double ServiceInterval = 1d / 240d;
        double _nextService;
        double _nextRender;
        int? _fps;
        bool _surfaceAvailable = true;

        internal void Configure(int fps)
        {
            if (_fps == fps)
                return;
            _fps = fps;
            _nextRender = 0;
        }

        internal double WaitSeconds(double now, bool renderQueueAvailable)
        {
            double wait = Math.Clamp(_nextService - now, 0, ServiceInterval);
            if (_surfaceAvailable && renderQueueAvailable)
                wait = Math.Min(wait, Math.Max(0, _nextRender - now));
            return wait;
        }

        internal void Polled(double now) => _nextService = now + ServiceInterval;

        internal bool TryRender(double now, bool renderQueueAvailable)
        {
            _surfaceAvailable = true;
            if (!renderQueueAvailable || now < _nextRender)
                return false;
            // Missed presentation deadlines never accumulate a catch-up burst.
            _nextRender = _fps > 0 ? now + 1d / _fps.Value : now;
            return true;
        }

        internal void SurfaceUnavailable() => _surfaceAvailable = false;
    }
}
