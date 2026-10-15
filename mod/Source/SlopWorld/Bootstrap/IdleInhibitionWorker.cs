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
            if (refreshMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(refreshMilliseconds));
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
                using (var lease = _acquire())
                {
                    // A quit during acquisition releases the late lease without refreshing it.
                    while (!_stop.WaitOne(0))
                    {
                        lease.Refresh();
                        if (_stop.WaitOne(_refreshMilliseconds))
                            break;
                    }
                }
            }
            catch (Exception error)
            {
                _warn(error);
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
            // Native bus connection/authentication can stall. Never hold game shutdown
            // indefinitely; process exit also closes the private connection's socket.
            if (Thread.CurrentThread != _thread)
                _thread.Join(500);
        }
    }
}
