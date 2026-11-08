using System;

namespace SlopWorld
{
    // Simulation-time charge; Aura owns pet identity, effects, and map lifetime.
    sealed class AuraCharge
    {
        const int TicksPerPat = 10 * 60;
        long _until;

        public bool Active(int now) => now < _until;

        public void Pat(int now) => _until = Math.Max(_until, now) + TicksPerPat;
    }
}
