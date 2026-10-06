using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Orders HTTP and pushed snapshots; callers own publication of their feature state.
    sealed class SnapshotRequest
    {
        sealed class Waiter
        {
            public Action Loaded;
            public Action<string> Failed;
        }
        readonly List<Waiter> _waiters = new List<Waiter>();
        public int Revision { get; private set; }

        // Keep refresh callers pending through a mutation. Its reload supplies the
        // winning snapshot; a failed mutation must also release those callers.
        public Action<string> Invalidate(Action<string> fail)
        {
            int revision = ++Revision;
            return error =>
            {
                if (revision == Revision) Settle(null, error);
                fail?.Invoke(error);
            };
        }

        public void Apply<T>(T value, Action<T> apply)
        {
            Revision++;
            Settle(() => apply(value), null);
        }

        public void Refresh<T>(string path, Action<T> apply, Action<string> fail,
                            Action loaded = null) where T : Google.Protobuf.IMessage<T>, new()
        {
            int revision = ++Revision;
            _waiters.Add(new Waiter { Loaded = loaded, Failed = fail });
            DaemonClient.Get<T>(path,
                value => { if (revision == Revision) Settle(() => apply(value), null); },
                error => { if (revision == Revision) Settle(null, error); });
        }

        void Settle(Action publish, string error)
        {
            // Detach before publishing/callbacks so reentrant refreshes belong to
            // the next outcome. Every success callback sees the winning snapshot.
            var waiters = _waiters.ToArray();
            _waiters.Clear();
            try { publish?.Invoke(); }
            catch (Exception e) { error = e.Message; }
            foreach (var waiter in waiters)
            {
                try
                {
                    if (error == null) waiter.Loaded?.Invoke();
                    else waiter.Failed?.Invoke(error);
                }
                catch (Exception e) { Verse.Log.Warning("[SlopWorld] snapshot callback: " + e); }
            }
        }
    }
}
