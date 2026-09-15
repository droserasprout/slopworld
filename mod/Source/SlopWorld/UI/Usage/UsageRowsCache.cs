using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Usage arrives as replacement snapshots, but the settings editor mutates poll flags.
    internal sealed class UsageRowsCache
    {
        public readonly List<string> Rows = new List<string>();
        public int Revision { get; private set; }
        UsageInfo _usage;
        bool _hasConfig, _ready;
        readonly Dictionary<string, bool?> _polls = new Dictionary<string, bool?>();

        public bool Prepare(UsageInfo usage, DaemonConfig config)
        {
            if (_ready && ReferenceEquals(_usage, usage) && Matches(config)) return false;
            _ready = true;
            _usage = usage;
            _hasConfig = config != null;
            _polls.Clear();
            if (config?.UsageItems != null)
                foreach (var pair in config.UsageItems) _polls[pair.Key] = pair.Value?.Poll;
            Rows.Clear();
            if (usage != null)
                foreach (var row in usage.Rows)
                    if (row.Poll) Add(row.Key);
            Rows.Sort(Compare);
            Revision++;
            return true;
        }

        bool Matches(DaemonConfig config)
        {
            if (_hasConfig != (config != null) || _polls.Count != (config?.UsageItems?.Count ?? 0)) return false;
            if (config?.UsageItems != null)
                foreach (var pair in config.UsageItems)
                    if (!_polls.TryGetValue(pair.Key, out var poll) || poll != pair.Value?.Poll) return false;
            return true;
        }

        void Add(string key)
        {
            if (Polled(key) && !Rows.Contains(key)) Rows.Add(key);
        }

        bool Polled(string key)
        {
            // The daemon already resolved this row's poll state. The client must not infer
            // provider defaults or briefly hide a row from its last authoritative snapshot.
            return true;
        }

        int Compare(string a, string b) => Rank(a).CompareTo(Rank(b));

        int Rank(string key)
        {
            if (_usage != null)
                foreach (var row in _usage.Rows)
                    if (row.Key == key) return row.Rank;
            return int.MaxValue;
        }
    }
}
