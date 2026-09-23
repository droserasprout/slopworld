namespace SlopWorld
{
    // Schedule work by real time. After a delay, run once without repeating missed intervals.
    internal struct PeriodicWork
    {
        double _next, _last;
        bool _scheduled;

        public bool Due(double now, double interval)
        {
            bool due = !_scheduled || now < _last || now >= _next;
            _last = now;
            if (due) Delay(now, interval);
            return due;
        }

        public void Delay(double now, double interval)
        {
            _scheduled = true;
            _last = now;
            _next = now + interval;
        }
    }
}
