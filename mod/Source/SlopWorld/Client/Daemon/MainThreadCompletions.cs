using System;
using System.Collections.Concurrent;

namespace SlopWorld
{
    // Owns lossless callback admission and bounded main-thread delivery. DaemonClient owns
    // HTTP requests and tracing; the host owns exception reporting and calling Pump each frame.
    sealed class MainThreadCompletions
    {
        readonly ConcurrentQueue<Action> _pending = new ConcurrentQueue<Action>();
        readonly int _maxCallbacks;
        readonly long _budgetTicks;
        readonly Func<long> _timestamp;

        public MainThreadCompletions(int maxCallbacks, long budgetTicks, Func<long> timestamp)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCallbacks);
            ArgumentOutOfRangeException.ThrowIfNegative(budgetTicks);
            _timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));
            _maxCallbacks = maxCallbacks;
            _budgetTicks = budgetTicks;
        }

        public int Count => _pending.Count;

        public void Enqueue(Action action)
        {
            if (action != null)
                _pending.Enqueue(action);
        }

        public int Pump(Action<Exception> reportError)
        {
            ArgumentNullException.ThrowIfNull(reportError);
            long started = _timestamp();
            int count = 0;
            while (count < _maxCallbacks && _pending.TryDequeue(out var action))
            {
                count++;
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    reportError(e);
                }
                // Always deliver one callback. A running callback cannot be interrupted;
                // retain the rest in FIFO order once either frame budget is spent.
                if (_timestamp() - started >= _budgetTicks)
                    break;
            }
            return count;
        }
    }
}
