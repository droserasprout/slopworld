using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Cached daemon renderings belong to one connection generation. Entries and failures
    // expire, and callbacks may update only the exact entry that issued their request.
    public sealed class DaemonRenderedBreadcrumbCache
    {
        internal const int MaxEntries = 64;
        internal const int MaxAttempts = 3;
        internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        readonly Func<string> _connection;
        readonly Func<DateTime> _now;
        readonly Action<string, Action<JVal>, Action<string>> _request;
        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        string _scope;

        sealed class Entry
        {
            public DateTime Expires, RetryAt;
            public int Attempts;
            public bool Pending;
            public string Value;
        }

        public DaemonRenderedBreadcrumbCache(Func<string> connection, Func<DateTime> now,
            Action<string, Action<JVal>, Action<string>> request)
        {
            _connection = connection;
            _now = now;
            _request = request;
        }

        string Scope()
        {
            string scope = _connection();
            if (_scope != scope)
            {
                _entries.Clear();
                _scope = scope;
            }
            return scope;
        }

        public string GetOrRequest(ProjectInfo project, SessionInfo agent, DaemonConfig config)
        {
            string scope = Scope();
            if (config == null || !config.MetadataAvailable || agent == null ||
                !config.ExperimentalInstructions || !config.ExperimentalBreadcrumbs ||
                !agent.SlopworldMd || !agent.InstructionsBreadcrumb ||
                !config.InstructionsBreadcrumbEnabled) return null;

            // The serialized request is an immutable, unambiguous key even when templates
            // contain newlines. A draft edit cannot rename an outstanding request's entry.
            string body = "{" +
                $"\"project\":{JVal.Q(project?.Name ?? "")}," +
                $"\"template\":{JVal.Q(config.InstructionsTemplate)}," +
                $"\"mount_path\":{JVal.Q(config.InstructionsMountPath)}," +
                $"\"breadcrumb\":{JVal.Q(config.InstructionsBreadcrumb)}" +
                "}";
            DateTime now = _now();
            Trim(now);
            if (!_entries.TryGetValue(body, out var entry))
            {
                if (_entries.Count >= MaxEntries) EvictOldest();
                entry = new Entry { Expires = now + Lifetime };
                _entries.Add(body, entry);
            }
            if (entry.Value != null) return entry.Value;
            if (entry.Pending || entry.Attempts >= MaxAttempts || now < entry.RetryAt) return null;
            entry.Pending = true;
            entry.Attempts++;
            _request(body, j =>
            {
                if (!IsCurrent(scope, body, entry)) return;
                if (j["breadcrumb"].IsNull)
                {
                    Failed(entry);
                    return;
                }
                entry.Value = j["breadcrumb"].AsString();
                entry.Pending = false;
            }, _ =>
            {
                if (IsCurrent(scope, body, entry)) Failed(entry);
            });
            return entry.Value;
        }

        bool IsCurrent(string scope, string body, Entry entry) =>
            Scope() == scope && _entries.TryGetValue(body, out var current) &&
            ReferenceEquals(entry, current) && _now() < entry.Expires;

        void Failed(Entry entry)
        {
            entry.Pending = false;
            entry.RetryAt = _now().AddSeconds(1 << (entry.Attempts - 1));
        }

        void Trim(DateTime now)
        {
            var expired = new List<string>();
            foreach (var pair in _entries)
                if (now >= pair.Value.Expires) expired.Add(pair.Key);
            foreach (string key in expired) _entries.Remove(key);
        }

        void EvictOldest()
        {
            string oldest = null;
            DateTime expires = DateTime.MaxValue;
            foreach (var pair in _entries)
                if (oldest == null || pair.Value.Expires < expires)
                {
                    oldest = pair.Key;
                    expires = pair.Value.Expires;
                }
            if (oldest != null) _entries.Remove(oldest);
        }
    }
}
