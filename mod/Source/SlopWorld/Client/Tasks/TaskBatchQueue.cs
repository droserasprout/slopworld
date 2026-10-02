using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Serializes one mutation kind. Overlapping callers share reserved IDs and their outcomes.
    sealed class TaskBatchQueue
    {
        internal readonly struct Outcome
        {
            public readonly string Error;
            public bool Succeeded => Error == null;
            public Outcome(string error) { Error = error; }
        }

        sealed class Batch
        {
            public readonly List<string> Ids = new List<string>();
            public readonly List<Action<Outcome>> Waiters = new List<Action<Outcome>>();
        }

        sealed class Caller
        {
            public int Remaining;
            public string Error;
            public Action Ok;
            public Action<string> Fail;

            public void Complete(Outcome outcome)
            {
                if (!outcome.Succeeded && Error == null) Error = outcome.Error;
                if (--Remaining != 0) return;
                if (Error == null) Ok?.Invoke();
                else Fail?.Invoke(Error);
            }
        }

        readonly Queue<Batch> _pending = new Queue<Batch>();
        readonly Dictionary<string, Batch> _reserved = new Dictionary<string, Batch>();
        readonly Action<List<string>, Action<Outcome>> _send;
        readonly Action _idle;
        Batch _active;

        public TaskBatchQueue(Action<List<string>, Action<Outcome>> send, Action idle)
        {
            _send = send;
            _idle = idle;
        }

        public void Enqueue(IEnumerable<string> ids, Action ok, Action<string> fail)
        {
            var dependencies = new HashSet<Batch>();
            var fresh = new Batch();
            foreach (string id in (ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrEmpty(id)).Distinct())
            {
                if (!_reserved.TryGetValue(id, out var batch))
                {
                    batch = fresh;
                    fresh.Ids.Add(id);
                    _reserved.Add(id, batch);
                }
                dependencies.Add(batch);
            }
            if (dependencies.Count == 0) { ok?.Invoke(); return; }
            var caller = new Caller { Remaining = dependencies.Count, Ok = ok, Fail = fail };
            foreach (var dependency in dependencies) dependency.Waiters.Add(caller.Complete);
            if (fresh.Ids.Count > 0) _pending.Enqueue(fresh);
            Pump();
        }

        void Pump()
        {
            if (_active != null || _pending.Count == 0) return;
            var batch = _active = _pending.Dequeue();
            _send(batch.Ids, outcome => Complete(batch, outcome));
        }

        void Complete(Batch batch, Outcome outcome)
        {
            if (_active != batch) return;
            foreach (string id in batch.Ids) _reserved.Remove(id);
            // Keep the operation active through callbacks so reentrant work queues behind it.
            foreach (var waiter in batch.Waiters)
            {
                try { waiter(outcome); }
                catch (Exception e) { Verse.Log.Warning("[SlopWorld] task batch callback: " + e); }
            }
            _active = null;
            Pump();
            if (_active == null && _pending.Count == 0) _idle();
        }
    }
}
