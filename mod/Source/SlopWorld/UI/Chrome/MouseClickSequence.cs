using UnityEngine;

namespace SlopWorld
{
    // IMGUI's clickCount is not reliable after another window has consumed the event. Keep
    // enough state to recognize multi-click gestures from the presses that reach the view.
    internal sealed class MouseClickSequence
    {
        const float MaxInterval = 0.75f;
        const float MaxDistance = 8f;

        readonly bool _useNativeClickCount;
        int _count;
        int _button = -1;
        Vector2 _position;
        float _at = -1f;

        // Routed rows reset on intervening presses, so only locally observed clicks may
        // extend their sequence. Other consumers retain IMGUI's multi-click fallback.
        public MouseClickSequence(bool useNativeClickCount = true) =>
            _useNativeClickCount = useNativeClickCount;

        public int Observe(Event e, float now)
        {
            bool sameSequence = _count > 0 && _button == e.button &&
                now >= _at && now - _at <= MaxInterval &&
                (e.mousePosition - _position).sqrMagnitude <= MaxDistance * MaxDistance;
            _count = sameSequence ? _count + 1 : 1;
            if (_useNativeClickCount && e.clickCount > _count) _count = e.clickCount;

            _button = e.button;
            _position = e.mousePosition;
            _at = now;
            return _count;
        }

        public void Reset()
        {
            _count = 0;
            _button = -1;
            _at = -1f;
        }
    }
}
