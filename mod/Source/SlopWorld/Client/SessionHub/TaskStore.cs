using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // REST-backed host task board. The daemon has no task socket event yet, so this store polls at a
    // quiet cadence for the sidebar badge and refreshes immediately when the tab is opened.
    sealed class TaskStore
    {
        public List<TaskInfo> Tasks = new List<TaskInfo>();

        bool _loading;
        long _nextPoll;
        int _refreshSerial;
        readonly Queue<CancellationBatch> _cancellations = new Queue<CancellationBatch>();
        readonly HashSet<string> _cancelingIds = new HashSet<string>();
        bool _cancellationInFlight;
        readonly Queue<RemovalBatch> _removals = new Queue<RemovalBatch>();
        readonly HashSet<string> _removingIds = new HashSet<string>();
        bool _removalInFlight;

        sealed class RemovalBatch
        {
            public readonly List<string> Ids;
            public readonly Action Ok;
            public readonly Action<string> Fail;

            public RemovalBatch(List<string> ids, Action ok, Action<string> fail)
            {
                Ids = ids;
                Ok = ok;
                Fail = fail;
            }
        }

        sealed class CancellationBatch
        {
            public readonly List<string> Ids;
            public readonly Action Ok;
            public readonly Action<string> Fail;

            public CancellationBatch(List<string> ids, Action ok, Action<string> fail)
            {
                Ids = ids;
                Ok = ok;
                Fail = fail;
            }
        }

        public int OpenTasks => Tasks.Count(t => !t.Terminal);

        public void Update()
        {
            // Tasks use HTTP rather than the session WebSocket. Keep polling when the socket is
            // down too: the daemon can answer the mailbox even while the live pane reconnects.
            if (_loading || SessionInfo.NowMs < _nextPoll) return;
            Refresh();
        }

        public void Refresh(Action ok = null, Action<string> fail = null)
        {
            if (_loading) return;

            _loading = true;
            _nextPoll = SessionInfo.NowMs + 10000L;
            int serial = ++_refreshSerial;
            // The host UI is the operator's task board, so it needs agent-to-agent work too.
            // Scoped callers and the CLI keep using the default participant mailbox.
            DaemonClient.Get("/api/tasks?all=true", j =>
            {
                bool current = serial == _refreshSerial;
                if (current)
                {
                    Tasks = j["tasks"].Items.Select(TaskInfo.FromJson)
                        .OrderByDescending(t => t.UpdatedMs).ToList();
                }
                _loading = false;
                if (current) ok?.Invoke();
            }, error =>
            {
                _loading = false;
                if (serial == _refreshSerial) fail?.Invoke(error);
            }, TaskInfo.Host);
        }

        public void Create(string to, string body, Action<TaskInfo> ok = null,
                           Action<string> fail = null)
        {
            InvalidateRefresh();
            DaemonClient.Post("/api/tasks",
                "{" + $"\"to\":{JVal.Q(to ?? "")}," +
                $"\"body\":{JVal.Q(body ?? "")}" + "}",
                j =>
                {
                    var task = TaskInfo.FromJson(j["task"]);
                    Upsert(task);
                    ok?.Invoke(task);
                }, fail, TaskInfo.Host);
        }

        public void UpdateStatus(string id, DelegatedTaskStatus status, string note,
                                 Action<TaskInfo> ok = null, Action<string> fail = null)
        {
            InvalidateRefresh();
            DaemonClient.Post($"/api/tasks/{HubWire.Esc(id)}",
                "{" + $"\"status\":{JVal.Q(TaskInfo.StatusText(status))}," +
                $"\"note\":{(note == null ? "null" : JVal.Q(note))}" + "}",
                j =>
                {
                    var task = TaskInfo.FromJson(j["task"]);
                    Upsert(task);
                    ok?.Invoke(task);
                }, fail, TaskInfo.Host);
        }

        public void Remove(string id, Action ok = null, Action<string> fail = null)
        {
            RemoveMany(new[] { id }, ok, fail);
        }

        public void CancelMany(IEnumerable<string> ids, Action ok = null,
                               Action<string> fail = null)
        {
            if (ids == null) return;

            var batchIds = new List<string>();
            foreach (string id in ids.Where(value => !string.IsNullOrEmpty(value)).Distinct())
            {
                if (!_cancelingIds.Add(id)) continue;
                batchIds.Add(id);
            }

            if (batchIds.Count == 0) return;
            _cancellations.Enqueue(new CancellationBatch(batchIds, ok, fail));
            InvalidateRefresh();
            PumpCancellations();
        }

        void PumpCancellations()
        {
            if (_cancellationInFlight || _cancellations.Count == 0) return;

            var pending = _cancellations.Dequeue();
            _cancellationInFlight = true;
            string body = "{\"ids\":[" +
                string.Join(",", pending.Ids.Select(JVal.Q).ToArray()) + "]}";
            DaemonClient.Post("/api/tasks/cancel", body, j =>
            {
                _cancellationInFlight = false;
                foreach (string id in pending.Ids) _cancelingIds.Remove(id);
                foreach (var task in j["tasks"].Items.Select(TaskInfo.FromJson)) Upsert(task);
                pending.Ok?.Invoke();
                FinishCancellations();
            }, error =>
            {
                _cancellationInFlight = false;
                foreach (string id in pending.Ids) _cancelingIds.Remove(id);
                pending.Fail?.Invoke(error);
                FinishCancellations();
            }, TaskInfo.Host);
        }

        void FinishCancellations()
        {
            if (_cancellations.Count == 0) Refresh();
            PumpCancellations();
        }

        public void RemoveMany(IEnumerable<string> ids, Action ok = null,
                               Action<string> fail = null)
        {
            if (ids == null) return;

            var batchIds = new List<string>();
            foreach (string id in ids.Where(value => !string.IsNullOrEmpty(value)).Distinct())
            {
                // A second click while a batch is draining must not create a duplicate
                // request that can only return "no such task" after the first one wins.
                if (!_removingIds.Add(id)) continue;
                batchIds.Add(id);
            }

            if (batchIds.Count == 0) return;
            _removals.Enqueue(new RemovalBatch(batchIds, ok, fail));
            InvalidateRefresh();
            PumpRemovals();
        }

        void PumpRemovals()
        {
            if (_removalInFlight || _removals.Count == 0) return;

            var pending = _removals.Dequeue();
            _removalInFlight = true;
            string body = "{\"ids\":[" +
                string.Join(",", pending.Ids.Select(JVal.Q).ToArray()) + "]}";
            DaemonClient.Post("/api/tasks/remove", body, _ =>
            {
                _removalInFlight = false;
                foreach (string id in pending.Ids) _removingIds.Remove(id);
                var removed = new HashSet<string>(pending.Ids);
                Tasks = Tasks.Where(t => !removed.Contains(t.Id)).ToList();
                pending.Ok?.Invoke();
                FinishRemoval();
            }, error =>
            {
                _removalInFlight = false;
                foreach (string id in pending.Ids) _removingIds.Remove(id);
                pending.Fail?.Invoke(error);
                FinishRemoval();
            }, TaskInfo.Host);
        }

        void FinishRemoval()
        {
            // Reconcile with the daemon after the last response. This also repairs the local view
            // if a request failed or another participant changed the board.
            if (_removals.Count == 0) Refresh();
            PumpRemovals();
        }

        public void Prune(Action ok = null, Action<string> fail = null)
        {
            InvalidateRefresh();
            DaemonClient.Delete("/api/tasks", j =>
            {
                Tasks = Tasks.Where(t => !t.Terminal).ToList();
                ok?.Invoke();
            }, fail, TaskInfo.Host);
        }

        void InvalidateRefresh() => _refreshSerial++;

        void Upsert(TaskInfo task)
        {
            int at = Tasks.FindIndex(t => t.Id == task.Id);
            if (at < 0) Tasks.Add(task);
            else Tasks[at] = task;
            Tasks = Tasks.OrderByDescending(t => t.UpdatedMs).ToList();
        }
    }
}
