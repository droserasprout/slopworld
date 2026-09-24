using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Stale queued work never reaches the transport. Already running work still owns a slot
    // until completion, so replacing a search cannot exceed the limit.
    public sealed class BoundedWork
    {
        readonly int _limit;
        readonly Queue<Tuple<Func<bool>, Action<Action>>> _queue = new Queue<Tuple<Func<bool>, Action<Action>>>();
        int _running;
        public BoundedWork(int limit) { _limit = limit; }
        public void Add(Func<bool> current, Action<Action> run)
        {
            _queue.Enqueue(Tuple.Create(current, run));
            Pump();
        }
        void Pump()
        {
            while (_running < _limit && _queue.Count > 0)
            {
                var work = _queue.Dequeue();
                if (!work.Item1()) continue;
                _running++;
                bool finished = false;
                work.Item2(() => { if (finished) return; finished = true; _running--; Pump(); });
            }
        }
    }
}
