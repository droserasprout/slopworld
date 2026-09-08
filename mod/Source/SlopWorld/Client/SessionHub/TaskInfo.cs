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

    // The daemon's durable mailbox record. Task state is deliberately separate from a session's
    // terminal state: a running agent can still have queued work, and a down agent can still be
    // the recipient of work waiting for it to return.
    public sealed class TaskInfo
    {
        public const string Host = WireContract.HostIdentity;
        // RimWorld's GenText.Truncate removes and remeasures one character at a time.
        // Never hand a compact mailbox row an entire delegated prompt; the detail dialog
        // remains the place for the complete body.
        public const int SummaryChars = 160;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0,
            DateTimeKind.Utc);

        public string Id = "";
        public string From = "";
        public string To = "";
        public string Body = "";
        public DelegatedTaskStatus Status;
        public string Note;
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

        public static TaskInfo FromJson(JVal v)
        {
            var task = new TaskInfo
            {
                Id = v["id"].AsString(),
                From = v["from"].AsString(),
                To = v["to"].AsString(),
                Body = v["body"].AsString(),
                Note = v["note"].IsNull ? null : v["note"].AsString(),
                CreatedMs = v["created_ms"].AsLong(),
                UpdatedMs = v["updated_ms"].AsLong(),
            };
            task.Status = ParseStatus(v["status"].AsString());
            var worker = v["worker"];
            if (!worker.IsNull)
            {
                task.WorkerSession = worker["session"].AsString();
                task.WorkerParent = worker["parent"].AsString();
                task.WorkerDurable = worker["durable"].AsBool(false);
            }
            return task;
        }

        public static DelegatedTaskStatus ParseStatus(string value)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case WireContract.TaskStatus.Accepted: return DelegatedTaskStatus.Accepted;
                case WireContract.TaskStatus.Working: return DelegatedTaskStatus.Working;
                case WireContract.TaskStatus.Done: return DelegatedTaskStatus.Done;
                case WireContract.TaskStatus.Failed: return DelegatedTaskStatus.Failed;
                case WireContract.TaskStatus.Canceled: return DelegatedTaskStatus.Canceled;
                default: return DelegatedTaskStatus.Queued;
            }
        }

        public static string StatusText(DelegatedTaskStatus status)
        {
            switch (status)
            {
                case DelegatedTaskStatus.Accepted: return WireContract.TaskStatus.Accepted;
                case DelegatedTaskStatus.Working: return WireContract.TaskStatus.Working;
                case DelegatedTaskStatus.Done: return WireContract.TaskStatus.Done;
                case DelegatedTaskStatus.Failed: return WireContract.TaskStatus.Failed;
                case DelegatedTaskStatus.Canceled: return WireContract.TaskStatus.Canceled;
                default: return WireContract.TaskStatus.Queued;
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
                    string one = OneLine(Body);
                    _summary = one.Length <= SummaryChars
                        ? one
                        : one.Substring(0, SummaryChars).TrimEnd() + "...";
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
