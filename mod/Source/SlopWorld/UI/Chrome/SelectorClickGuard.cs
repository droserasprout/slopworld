using UnityEngine;

namespace SlopWorld
{
    // A source press can dismiss its menu before the selector handles that same press.
    // Suppress reopening once, or retire the dismissal on a later physical press.
    internal sealed class SelectorClickGuard
    {
        Vector2? _dismissed;
        int _pressFrame = -1;

        // Physical input is visible on every GUI pass in its frame. A dismissal can
        // precede the selector's first draw, so observing the same press must be idempotent.
        public void BeginPress(int frame)
        {
            if (_pressFrame == frame)
                return;
            _pressFrame = frame;
            _dismissed = null;
        }

        public void Dismiss(Vector2 source, int frame)
        {
            BeginPress(frame);
            _dismissed = source;
        }

        public bool TryBlock(Vector2 source)
        {
            if (!_dismissed.HasValue || (_dismissed.Value - source).sqrMagnitude >= 0.25f)
                return false;
            _dismissed = null;
            return true;
        }
    }
}
