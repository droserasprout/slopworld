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
        // Mutable sample state is protected by _gate. An outstanding read includes
        // the wake-up request before the worker starts the native query.
        bool _readOutstanding, _resultPending, _stopped;
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
                bool fresh = _resultPending;
                _resultPending = false;
                if (!_stopped && !_readOutstanding)
                {
                    _readOutstanding = true;
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
                        _resultPending = true;
                        _readOutstanding = false;
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
