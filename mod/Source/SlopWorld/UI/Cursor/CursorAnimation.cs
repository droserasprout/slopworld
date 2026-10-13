using UnityEngine;

namespace SlopWorld
{
    // Tracks animation timing. Pats interrupt clicks and ignore further input.
    internal sealed class CursorAnimation
    {
        const int SpinWaggles = 2;
        const float SpinSeconds = 0.5f;

        // CursorTextures caches each rotation frame on first use.
        public const int Steps = 12;

        // Clicks use a quarter of the pat rotation.
        const int ClickStep = 3;
        const float ClickSeconds = 0.12f;

        float _spinUntil = -1f;
        float _clickUntil = -1f;

        public bool PatActive => _spinUntil >= 0f;

        public void Pat(float now)
        {
            if (PatActive)
                return;
            _clickUntil = -1f;
            _spinUntil = now + SpinSeconds;
        }

        public void Click(float now)
        {
            if (PatActive)
                return;
            _clickUntil = now + ClickSeconds;
        }

        public void Reset()
        {
            _spinUntil = -1f;
            _clickUntil = -1f;
        }

        public int Step(float now)
        {
            if (_spinUntil >= 0f)
            {
                float left = _spinUntil - now;
                if (left <= 0f)
                {
                    _spinUntil = -1f;
                    return 0;
                }

                // Each half-sine swings out and back on the same side.
                float t = 1f - left / SpinSeconds;
                float f = Mathf.Abs(Mathf.Sin(t * SpinWaggles * Mathf.PI));
                return Mathf.RoundToInt(f * Steps);
            }

            if (_clickUntil >= 0f)
            {
                if (now >= _clickUntil)
                {
                    _clickUntil = -1f;
                    return 0;
                }
                return ClickStep;
            }

            return 0;
        }
    }
}
