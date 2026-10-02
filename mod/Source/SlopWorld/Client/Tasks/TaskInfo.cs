using System;
using System.Text;

namespace SlopWorld
{
    public enum DelegatedTaskStatus
    {
        Queued,
        Accepted,
        Working,
        Done,
        Failed,
        Canceled,
    }

    // Persistent mailbox record from the daemon.
    // Task state is separate from terminal state.
    // Running agents can have queued work. Stopped agents can receive work for their next run.
    public sealed class TaskInfo
    {
        public const string Host = WireProtocol.HostIdentity;
        // RimWorld's GenText.Truncate removes and measures one character at a time.
        // Limit text before supplying it to a compact mailbox row.
        // Show the complete task body in the detail dialog.
        public const int SummaryChars = 160;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0,
            DateTimeKind.Utc);

        public string Id = "";
        public string From = "";
        public string To = "";
        public string Body = "";
        public DelegatedTaskStatus Status;
        public string Note;
        public string GeneratedSummary;
        public long CreatedMs;
        public long UpdatedMs;
        public string WorkerSession = "";
        public string WorkerParent = "";
        public bool WorkerDurable;

        public bool Worker => !string.IsNullOrEmpty(WorkerSession);

        string _direction;
        string _summary;

        public bool Terminal => Status == DelegatedTaskStatus.Done ||
            Status == DelegatedTaskStatus.Failed ||
            Status == DelegatedTaskStatus.Canceled;

        public bool Incoming => To == Host;
        public bool Outgoing => From == Host;

        public static TaskInfo FromWire(Wire.Task v)
        {
            var task = new TaskInfo
            {
                Id = v.Id,
                From = v.From,
                To = v.To,
                Body = v.Body,
                Note = !v.HasNote ? null : v.Note,
                GeneratedSummary = !v.HasSummary ? null : v.Summary,
                CreatedMs = (long)v.CreatedMs,
                UpdatedMs = (long)v.UpdatedMs,
            };
            task.Status = ParseStatus(v.Status);
            var worker = v.Worker;
            if (worker != null)
            {
                task.WorkerSession = worker.Session;
                task.WorkerParent = worker.Parent;
                task.WorkerDurable = worker.Durable;
            }
            return task;
        }

        public static DelegatedTaskStatus ParseStatus(string value)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case WireProtocol.TaskStatus.Accepted: return DelegatedTaskStatus.Accepted;
                case WireProtocol.TaskStatus.Working: return DelegatedTaskStatus.Working;
                case WireProtocol.TaskStatus.Done: return DelegatedTaskStatus.Done;
                case WireProtocol.TaskStatus.Failed: return DelegatedTaskStatus.Failed;
                case WireProtocol.TaskStatus.Canceled: return DelegatedTaskStatus.Canceled;
                default: return DelegatedTaskStatus.Queued;
            }
        }

        public static string StatusText(DelegatedTaskStatus status)
        {
            switch (status)
            {
                case DelegatedTaskStatus.Accepted: return WireProtocol.TaskStatus.Accepted;
                case DelegatedTaskStatus.Working: return WireProtocol.TaskStatus.Working;
                case DelegatedTaskStatus.Done: return WireProtocol.TaskStatus.Done;
                case DelegatedTaskStatus.Failed: return WireProtocol.TaskStatus.Failed;
                case DelegatedTaskStatus.Canceled: return WireProtocol.TaskStatus.Canceled;
                default: return WireProtocol.TaskStatus.Queued;
            }
        }

        public static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var one = new StringBuilder(text.Length);
            bool gap = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    gap = one.Length > 0;
                    continue;
                }
                if (gap) one.Append(' ');
                one.Append(c);
                gap = false;
            }
            return one.ToString();
        }

        public string Counterpart => Incoming ? From : To;

        public string Direction
        {
            get
            {
                if (_direction == null)
                    _direction = Incoming ? $"{From} -> you" :
                        Outgoing ? $"you -> {To}" : $"{From} -> {To}";
                return _direction;
            }
        }

        public string Summary
        {
            get
            {
                if (_summary == null)
                {
                    if (!string.IsNullOrWhiteSpace(GeneratedSummary))
                    {
                        _summary = OneLine(GeneratedSummary);
                        return _summary;
                    }
                    string one = OneLine(Body);
                    int end = Math.Min(one.Length, SummaryChars);
                    // UTF-16 truncation must keep both halves of a scalar together.
                    if (end < one.Length && end > 0 && char.IsHighSurrogate(one[end - 1]) &&
                        char.IsLowSurrogate(one[end])) end--;
                    _summary = one.Length <= SummaryChars
                        ? one
                        : one.Substring(0, end).TrimEnd() + "...";
                }
                return _summary;
            }
        }

        public string Age(bool updated = false)
        {
            long then = updated ? UpdatedMs : CreatedMs;
            long seconds = (DateTime.UtcNow.Ticks - Epoch.Ticks -
                then * TimeSpan.TicksPerMillisecond) /
                TimeSpan.TicksPerSecond;
            if (seconds < 0) return "now";
            if (seconds < 60) return seconds == 0 ? "now" : seconds + "s";
            if (seconds < 3600) return seconds / 60 + "m";
            if (seconds < 86400) return seconds / 3600 + "h";
            return seconds / 86400 + "d";
        }
    }
}
