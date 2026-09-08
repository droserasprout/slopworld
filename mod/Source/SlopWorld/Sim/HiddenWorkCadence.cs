namespace SlopWorld
{
    // Hidden maintenance stays warm at a bounded cadence; reveal always runs immediately.
    internal sealed class HiddenWorkCadence
    {
        bool _hidden;
        PeriodicWork _work;

        public bool Run(bool hidden, double now)
        {
            bool entering = hidden && !_hidden;
            _hidden = hidden;
            if (!hidden) return true;
            if (entering)
            {
                _work.Delay(now, 0.25);
                return true;
            }
            return _work.Due(now, 0.25);
        }
    }
}
