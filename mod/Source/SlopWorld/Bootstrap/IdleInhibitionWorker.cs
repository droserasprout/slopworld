using System;
using System.Threading;

namespace SlopWorld
{
    internal interface IIdleInhibitionLease : IDisposable
    {
        void Refresh();
    }

    // One background thread owns acquisition, the lease, and release. Quit may race
    // acquisition: a late lease is still disposed, without publishing it to game state.
    internal sealed class IdleInhibitionWorker : IDisposable
    {
        readonly object _gate = new object();
        readonly ManualResetEvent _stop = new ManualResetEvent(false);
        readonly Thread _thread;
        readonly Func<IIdleInhibitionLease> _acquire;
        readonly Action<Exception> _warn;
        readonly int _refreshMilliseconds;
        bool _stopped;

        public IdleInhibitionWorker(
            Func<IIdleInhibitionLease> acquire,
            Action<Exception> warn,
            int refreshMilliseconds = 30000
        )
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshMilliseconds);
            _acquire = acquire;
            _warn = warn;
            _refreshMilliseconds = refreshMilliseconds;
            _thread = new Thread(Run) { IsBackground = true, Name = "SlopWorld idle inhibition" };
            _thread.Start();
        }

        void Run()
        {
            try
            {
                lock (_gate)
                    if (_stopped)
                        return;
                while (!_stop.WaitOne(0))
                {
                    try
                    {
                        using var lease = _acquire();
                        while (!_stop.WaitOne(0))
                        {
                            lease.Refresh();
                            if (_stop.WaitOne(_refreshMilliseconds))
                                return;
                        }
                    }
                    catch (Exception error)
                    {
                        _warn(error);
                        if (_stop.WaitOne(_refreshMilliseconds))
                            return;
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _stopped = true;
                    _stop.Dispose();
                }
            }
        }

        // Live settings changes only signal the owner; native cleanup stays off the UI thread.
        public void RequestStop()
        {
            lock (_gate)
            {
                if (!_stopped)
                {
                    _stopped = true;
                    _stop.Set();
                }
            }
        }

        public void Dispose()
        {
            RequestStop();
            // Desktop service calls can stall. Never hold game shutdown
            // indefinitely; process exit also closes private desktop connections.
            if (Thread.CurrentThread != _thread)
                _thread.Join(500);
        }
    }
}
