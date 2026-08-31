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
            SlopClient.Get("/api/tasks?all=true", j =>
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
            SlopClient.Post("/api/tasks",
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
            SlopClient.Post($"/api/tasks/{HubWire.Esc(id)}",
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
            InvalidateRefresh();
            SlopClient.Delete($"/api/tasks/{HubWire.Esc(id)}", j =>
            {
                Tasks = Tasks.Where(t => t.Id != id).ToList();
                ok?.Invoke();
            }, fail, TaskInfo.Host);
        }

        public void Prune(Action ok = null, Action<string> fail = null)
        {
            InvalidateRefresh();
            SlopClient.Delete("/api/tasks", j =>
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
