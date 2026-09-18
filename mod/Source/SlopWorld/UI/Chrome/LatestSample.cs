using System;
using System.Threading;

namespace SlopWorld
{
    // A slow native query must not stall IMGUI. Keep one query in flight, no request queue,
    // and one replaceable result. The dedicated worker owns the native connection.
    internal sealed class LatestSample<T> : IDisposable
    {
        readonly object _gate = new object();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly Func<T> _read;
        readonly Thread _worker;
        bool _busy, _fresh, _stopped;
        T _value;
        Exception _error;

        public LatestSample(Func<T> read)
        {
            _read = read;
            _worker = new Thread(Run) { IsBackground = true, Name = "SlopWorld scroll sample" };
            _worker.Start();
        }

        public bool TryRead(out T value, out Exception error)
        {
            lock (_gate)
            {
                value = _value;
                error = _error;
                bool fresh = _fresh;
                _fresh = false;
                if (!_stopped && !_busy)
                {
                    _busy = true;
                    _wake.Set();
                }
                return fresh;
            }
        }

        void Run()
        {
            try
            {
                while (true)
                {
                    _wake.WaitOne();
                    lock (_gate) { if (_stopped) return; }
                    T value;
                    try { value = _read(); }
                    catch (Exception error)
                    {
                        lock (_gate) { _error = error; _stopped = true; }
                        return;
                    }
                    lock (_gate)
                    {
                        if (_stopped) return;
                        _value = value;
                        _fresh = true;
                        _busy = false;
                    }
                }
            }
            finally { _wake.Dispose(); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_stopped) return;
                _stopped = true;
                _wake.Set();
            }
        }
    }
}
