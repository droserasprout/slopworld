using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Host task board backed by HTTP requests.
    // The daemon has no task socket event, so poll periodically for the sidebar badge.
    // Refresh immediately when the tab opens.
    sealed class TaskStore
    {
        public List<TaskInfo> Tasks = new List<TaskInfo>();

        bool _loading;
        double _nextPoll;
        int _refreshSerial;
        readonly TaskBatchQueue _cancellations;
        readonly TaskBatchQueue _removals;

        public TaskStore()
        {
            _cancellations = new TaskBatchQueue(SendCancellation, () => Refresh());
            _removals = new TaskBatchQueue(SendRemoval, () => Refresh());
        }

        public int OpenTasks => Tasks.Count(t => !t.Terminal);

        public void Update(bool needsData)
        {
            // Poll visible task data during WebSocket reconnection because HTTP is independent.
            // Use monotonic time for this local deadline.
            if (!needsData || _loading || UnityEngine.Time.realtimeSinceStartupAsDouble < _nextPoll)
                return;
            Refresh();
        }

        public void Refresh(Action ok = null, Action<string> fail = null)
        {
            if (_loading) return;

            _loading = true;
            _nextPoll = UnityEngine.Time.realtimeSinceStartupAsDouble + 10.0;
            int serial = ++_refreshSerial;
            // The host UI is the operator's task board, so it needs agent-to-agent work too.
            // Scoped callers and the CLI keep using the default participant mailbox.
            DaemonClient.Get<Wire.TasksReply>(WireProtocol.Routes.Tasks + "?all=true", j =>
            {
                bool current = serial == _refreshSerial;
                if (current)
                {
                    Tasks = j.Tasks.Select(TaskInfo.FromWire)
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
            DaemonClient.Post<Wire.TaskResult>(WireProtocol.Routes.Tasks,
                new Wire.CreateTaskReq { To = to ?? "", Body = body ?? "" },
                j =>
                {
                    var task = TaskInfo.FromWire(j.Task);
                    Upsert(task);
                    ok?.Invoke(task);
                }, fail, TaskInfo.Host);
        }

        public void UpdateStatus(string id, DelegatedTaskStatus status, string note,
                                 Action<TaskInfo> ok = null, Action<string> fail = null)
        {
            InvalidateRefresh();
            var request = new Wire.UpdateTaskReq { Status = TaskInfo.StatusText(status) };
            if (note != null) request.Note = note;
            DaemonClient.Post<Wire.TaskResult>($"{WireProtocol.Routes.Tasks}/{HubWire.Esc(id)}", request,
                j =>
                {
                    var task = TaskInfo.FromWire(j.Task);
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
            InvalidateRefresh();
            _cancellations.Enqueue(ids, ok, fail);
        }

        void SendCancellation(List<string> ids, Action<TaskBatchQueue.Outcome> done)
        {
            var body = new Wire.RemoveTasksReq { Ids = { ids } };
            DaemonClient.Post<Wire.TaskBatchResult>(WireProtocol.Routes.TasksCancel, body, j =>
            {
                InvalidateRefresh();
                foreach (var task in j.Tasks.Select(TaskInfo.FromWire)) Upsert(task);
                done(BatchOutcome(j));
            }, error => done(new TaskBatchQueue.Outcome(error)), TaskInfo.Host);
        }

        public void RemoveMany(IEnumerable<string> ids, Action ok = null,
                               Action<string> fail = null)
        {
            InvalidateRefresh();
            _removals.Enqueue(ids, ok, fail);
        }

        void SendRemoval(List<string> ids, Action<TaskBatchQueue.Outcome> done)
        {
            var body = new Wire.RemoveTasksReq { Ids = { ids } };
            DaemonClient.Post<Wire.TaskBatchResult>(WireProtocol.Routes.TasksRemove, body, j =>
            {
                ApplyRemoval(j);
                done(BatchOutcome(j));
            }, error => done(new TaskBatchQueue.Outcome(error)), TaskInfo.Host);
        }

        public void Prune(Action ok = null, Action<string> fail = null)
        {
            InvalidateRefresh();
            DaemonClient.Delete<Wire.TaskBatchResult>(WireProtocol.Routes.Tasks + "?all=true", j =>
            {
                ApplyRemoval(j);
                var outcome = BatchOutcome(j);
                if (outcome.Succeeded) ok?.Invoke();
                else fail?.Invoke(outcome.Error);
                Refresh();
            }, error => { fail?.Invoke(error); Refresh(); }, TaskInfo.Host);
        }

        void ApplyRemoval(Wire.TaskBatchResult result)
        {
            InvalidateRefresh();
            var removed = new HashSet<string>(result.Committed.Concat(result.Absent));
            Tasks = Tasks.Where(t => !removed.Contains(t.Id)).ToList();
        }

        static TaskBatchQueue.Outcome BatchOutcome(Wire.TaskBatchResult result)
        {
            if (result.Failed.Count == 0 && result.Unattempted.Count == 0)
                return new TaskBatchQueue.Outcome(null);
            string details = string.Join("; ", result.Failed.Select(f => f.Id + ": " + f.Error));
            string error = $"Changed {result.Committed.Count} tasks; failed {result.Failed.Count}; not attempted {result.Unattempted.Count}. {details}";
            return new TaskBatchQueue.Outcome(error,
                result.Failed.Select(f => f.Id).Concat(result.Unattempted));
        }

        void InvalidateRefresh() => _refreshSerial++;

        public void Add(TaskInfo task)
        {
            if (task == null) return;
            // Worker creation can finish while an older task-board snapshot is in flight.
            InvalidateRefresh();
            Upsert(task);
        }

        void Upsert(TaskInfo task)
        {
            int at = Tasks.FindIndex(t => t.Id == task.Id);
            if (at < 0) Tasks.Add(task);
            else Tasks[at] = task;
            Tasks = Tasks.OrderByDescending(t => t.UpdatedMs).ToList();
        }
    }
}
