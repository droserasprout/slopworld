namespace SlopWorld
{
    // Schedule work by real time. After a delay, run once without repeating missed intervals.
    internal struct PeriodicWork
    {
        double _nextDueAt, _lastObservedAt;
        bool _scheduled;

        public bool Due(double now, double interval)
        {
            bool due = !_scheduled || now < _lastObservedAt || now >= _nextDueAt;
            _lastObservedAt = now;
            if (due) Delay(now, interval);
            return due;
        }

        public void Delay(double now, double interval)
        {
            _scheduled = true;
            _lastObservedAt = now;
            _nextDueAt = now + interval;
        }
    }
}
