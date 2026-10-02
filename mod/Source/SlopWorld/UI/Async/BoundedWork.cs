using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Stale queued work never reaches the transport. Already running work still owns a slot
    // until completion, so replacing a search cannot exceed the limit.
    public sealed class BoundedWork
    {
        readonly int _limit;
        sealed class WorkItem
        {
            public Func<bool> IsCurrent;
            public Action<Action> Run;
        }
        readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        int _running;
        public BoundedWork(int limit)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
            _limit = limit;
        }
        public void Add(Func<bool> current, Action<Action> run)
        {
            _queue.Enqueue(new WorkItem { IsCurrent = current, Run = run });
            Pump();
        }
        void Pump()
        {
            while (_running < _limit && _queue.Count > 0)
            {
                var work = _queue.Dequeue();
                if (!work.IsCurrent()) continue;
                _running++;
                bool finished = false;
                work.Run(() => { if (finished) return; finished = true; _running--; Pump(); });
            }
        }
    }
}
