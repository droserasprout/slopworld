using UnityEngine;
using Verse;

namespace SlopWorld
{
    // IMGUI consumes completed samples without touching Xlib. The sampler owns native
    // recovery; this owner rejects stale motion and restarts baselines after source changes.
    static class X11ScrollInput
    {
        static readonly ScrollSampleDelta Motion = new ScrollSampleDelta();
        static readonly ScrollSampleBudget SampleBudget = new ScrollSampleBudget();
        static readonly X11ScrollSampler Sampler = new X11ScrollSampler();
        static LatestSample<X11ScrollSampler.Sample> _reader;
        static double _readerRetryAt;
        static bool _unavailable;
        static bool _sampleUsable;
        static Vector2 _sample;

        // Logical fallback already moved the viewport. Late native samples must not replay it.
        public static void DiscardPendingMovement()
        {
            Motion.Discard(X11ScrollSampler.Now);
            _sample = Vector2.zero;
            _sampleUsable = false;
        }

        public static bool TryRead(out Vector2 units, bool refresh = false)
        {
            units = Vector2.zero;
            if (!SampleBudget.Take(Time.frameCount, refresh))
            {
                units = _sample;
                return _sampleUsable;
            }

            _sample = Vector2.zero;
            _sampleUsable = false;
            if (Application.platform != RuntimePlatform.LinuxPlayer ||
                X11ScrollSampler.Now < _readerRetryAt) return false;
            if (_reader == null) _reader = new LatestSample<X11ScrollSampler.Sample>(Sampler.Read);
            bool fresh = _reader.TryRead(out var snapshot, out var error);
            if (error != null)
            {
                // The sampler handles native failures itself. An unexpected worker failure
                // has ended that worker before we hand its native owner to a replacement.
                _reader.Dispose();
                _reader = null;
                _readerRetryAt = X11ScrollSampler.Now + 1;
                DiscardPendingMovement();
                Log.Warning("[SlopWorld] precise X11 scrolling sampler will retry: " + error.Message);
                return false;
            }
            if (!fresh) return false;
            if (!snapshot.Available)
            {
                DiscardPendingMovement();
                if (!_unavailable && snapshot.UnavailableReason != null)
                {
                    _unavailable = true;
                    Log.Warning("[SlopWorld] precise X11 scrolling unavailable: " +
                        snapshot.UnavailableReason + ". Using wheel input.");
                }
                return false;
            }
            if (_unavailable)
            {
                _unavailable = false;
                Log.Message("[SlopWorld] precise X11 scrolling recovered.");
            }
            if (!Motion.Read(snapshot.X, snapshot.Y, snapshot.Time, X11ScrollSampler.Now,
                             snapshot.Generation, out double dx, out double dy)) return false;
            _sample = new Vector2((float)dx, (float)dy);
            _sampleUsable = true;
            units = _sample;
            return true;
        }
    }
}
